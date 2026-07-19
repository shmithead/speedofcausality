using Godot;
using Sim.Core.Diagnostics;
using Sim.Core.Economy;
using Sim.Core.Knowledge;
using Sim.Core.Ships;
using Sim.Core.World;

namespace SpeedOfCausality.Game;

/// <summary>
/// The Phase 1 strategic map (roadmap §5). It drives the headless <see cref="SimWorld"/> forward in
/// compressed time and renders <b>only the player's knowledge fold</b> (§2.2) — never ground truth.
///
/// <para><b>The render rule, made structural (§5, learned the hard way in the spike):</b> the screen
/// shows only what has <i>arrived</i>, plus predictions computed from what has arrived. So:
/// <list type="bullet">
/// <item>Celestial bodies are drawn at their true position — they are common-knowledge almanac (§2.7),
/// not news that has to arrive.</item>
/// <item>A ship is drawn as its <b>ghost</b> (last arrived telemetry, one lag stale) and, separately,
/// as a <b>prediction</b> extrapolated from its filed plan — which is legal, and is better drama
/// because it is wrong exactly when the plan is wrong.</item>
/// <item>Prices are last-known quotes with their age. No packet is drawn at a true in-flight position;
/// no wavefront is drawn approaching.</item>
/// </list></para>
///
/// <para>This environment cannot run Godot, so this script is unverified in-engine — open it in
/// Godot 4.4+ locally. The sim logic it calls is fully tested in Sim.Tests.</para>
/// </summary>
public partial class StrategicMap : Node2D
{
    private const double AuMm = 149_597_870_700_000.0;
    private const long Day = 86_400L;
    private const long ObserverId = Phase1Scenario.HqId; // the map renders the player's own receiver

    private static readonly (long Id, Color Color)[] Bodies =
    {
        (SolSystem.EarthId, new Color(0.40f, 0.65f, 1.00f)),
        (SolSystem.MarsId, new Color(0.90f, 0.45f, 0.30f)),
        (SolSystem.CeresId, new Color(0.70f, 0.70f, 0.75f)),
    };

    private SimWorld _world = null!;
    private double _simSeconds;
    private double _timeScale = 3.0 * Day; // sim-seconds advanced per real second
    private bool _paused;
    private float _pxPerAu = 130f;

    public override void _Ready()
    {
        _world = Phase1Scenario.Build(seed: 42);
    }

    public override void _Process(double delta)
    {
        if (!_paused)
        {
            _simSeconds += _timeScale * delta;
            _world.Sim.RunUntil((long)_simSeconds);
        }

        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Space:
                _paused = !_paused;
                break;
            case Key.Equal or Key.Plus:
                _timeScale *= 2.0;
                break;
            case Key.Minus:
                _timeScale = System.Math.Max(_timeScale / 2.0, Day / 4.0);
                break;
            case Key.C:
                DivertPlayerShipToCeres();
                break;
        }
    }

    public override void _Draw()
    {
        Vector2 center = GetViewportRect().Size / 2f;
        long now = (long)_simSeconds;
        Font font = ThemeDB.FallbackFont;

        DrawCircle(center, 6f, new Color(1f, 0.85f, 0.3f)); // the Sun, at the heliocentric origin

        foreach ((long id, Color color) in Bodies)
        {
            (long x, long y, long _) = _world.EntitySpatial(id).PositionMmAt(now);
            Vector2 p = ToScreen(center, x, y);
            DrawCircle(p, 5f, color);
        }

        DrawShips(center, now, font);
        DrawHud(now, font);
    }

    private void DrawShips(Vector2 center, long now, Font font)
    {
        foreach ((long shipId, ShipKnowledge k) in ShipView.Read(_world.Knowledge, ObserverId, now))
        {
            // Prediction: where the filed plan says the ship is now (fair to draw live, §5).
            if (k.Plan is { } plan)
            {
                (long dx, long dy, long _) = plan.PredictedPositionMmAt(now);
                Vector2 predicted = ToScreen(center, dx, dy);
                (long ex, long ey, long _z) = plan.PredictedPositionMmAt(plan.ArriveSeconds);
                DrawLine(ToScreen(center, plan.X, plan.Y), ToScreen(center, ex, ey), new Color(0.3f, 0.5f, 0.4f), 1f);
                DrawCircle(predicted, 3f, new Color(0.4f, 0.9f, 0.6f)); // predicted position
            }

            // Ghost: last arrived telemetry — stale, and reddened if it is off its filed plan (deviation).
            if (k.Ghost is { } ghost)
            {
                Vector2 g = ToScreen(center, ghost.X, ghost.Y);
                bool deviating = k.DeviationMm() is { } d && d > AuMm / 20;
                Color ghostColor = deviating ? new Color(1f, 0.35f, 0.35f) : new Color(0.85f, 0.85f, 0.9f);
                DrawCircle(g, 4f, ghostColor);
                long ageDays = (now - k.GhostOccurredAtSeconds) / Day;
                DrawString(font, g + new Vector2(8, 4), $"#{shipId} {ghost.Cause} ({ageDays}d)",
                    HorizontalAlignment.Left, -1, 12, ghostColor);
            }
        }
    }

    private void DrawHud(long now, Font font)
    {
        var line = new Vector2(16, 24);
        DrawString(font, line, $"T+{now / Day}d   x{_timeScale / Day:0.##}day/s{(_paused ? "  [PAUSED]" : "")}",
            HorizontalAlignment.Left, -1, 14, Colors.White);

        line.Y += 24;
        DrawString(font, line, "prices (last known):", HorizontalAlignment.Left, -1, 13, new Color(0.8f, 0.8f, 0.85f));
        foreach ((long settlementId, PriceQuote q) in PriceBook.Read(_world.Knowledge, ObserverId, now))
        {
            line.Y += 18;
            DrawString(font, line + new Vector2(12, 0),
                $"settlement {settlementId}: {q.PriceMinorUnits}  (age {q.AgeSeconds(now) / Day}d)",
                HorizontalAlignment.Left, -1, 12, new Color(0.75f, 0.85f, 0.8f));
        }

        line.Y += 30;
        DrawString(font, line, "[space] pause   [+/-] time   [C] divert player ship to Ceres",
            HorizontalAlignment.Left, -1, 12, new Color(0.6f, 0.6f, 0.65f));
    }

    private void DivertPlayerShipToCeres()
    {
        if (_world.EntitySpatial(Phase1Scenario.PlayerShipId) is Ship ship)
        {
            ShipCommands.IssueCountermand(_world, ship, ObserverId, Phase1Scenario.CeresPortId);
        }
    }

    private Vector2 ToScreen(Vector2 center, long xMm, long yMm)
        => center + new Vector2((float)(xMm / AuMm) * _pxPerAu, -(float)(yMm / AuMm) * _pxPerAu);
}
