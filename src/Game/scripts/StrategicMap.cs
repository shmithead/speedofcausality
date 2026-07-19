using Godot;
using Sim.Core.Diagnostics;
using Sim.Core.Economy;
using Sim.Core.Knowledge;
using Sim.Core.Ships;
using Sim.Core.World;

namespace SpeedOfCausality.Game;

/// <summary>
/// The Phase 1 strategic map, now a small game you play (roadmap §5, plus deliberate scope creep). It
/// drives the headless <see cref="SimWorld"/> in compressed time and renders <b>only the player's
/// knowledge fold</b> (§2.2) — never ground truth. You command persistent freighters by right-clicking
/// a body; the order, the ship's SITREP position reports, and every price all travel at c, so the map
/// is always a stale, lagged picture and acting on it is a bet.
///
/// <para><b>The render rule (§5):</b> bodies are drawn true (common-knowledge almanac, §2.7); a ship
/// under way shows a green <i>predicted</i> position (from its filed plan) and a grey/red <i>ghost</i>
/// (last arrived SITREP, reddened when off-plan); a docked ship is drawn at its port because HQ heard
/// it arrive. Nothing is drawn in flight that no signal reported.</para>
///
/// <para>Authored without a Godot binary in the loop — the Sim.Core calls are all tested; the UI is
/// immediate-mode (drawn + hit-tested) to minimize engine surface. First-run touch-ups may be needed.</para>
/// </summary>
public partial class StrategicMap : Node2D
{
    private const double AuMm = 149_597_870_700_000.0;
    private const long Day = 86_400L;
    private const long ObserverId = Phase1Scenario.HqId;

    private static readonly long[] PlayerShips = { Phase1Scenario.PlayerShipId };

    // Body id, its port settlement, colour, display name.
    private static readonly (long Body, long Port, Color Color, string Name)[] Worlds =
    {
        (SolSystem.EarthId, Phase1Scenario.HqId, new Color(0.40f, 0.65f, 1.00f), "Earth-HQ"),
        (SolSystem.MarsId, Phase1Scenario.MarsPortId, new Color(0.90f, 0.45f, 0.30f), "Mars"),
        (SolSystem.CeresId, Phase1Scenario.CeresPortId, new Color(0.70f, 0.70f, 0.75f), "Ceres"),
    };

    private enum UiMode { None, Menu, Sitrep }

    private SimWorld _world = null!;
    private double _simSeconds;
    private double _timeScale = 3.0 * Day;
    private bool _paused;
    private float _pxPerAu = 130f;

    private (long Target, long IssuedAt, long Reception)? _pending;

    private UiMode _mode = UiMode.None;
    private Vector2 _menuPos;
    private long _menuPort;
    private long _sitrepShip;
    private string _sitrepDigits = "";

    private const float MenuRow = 22f;
    private const float MenuWidth = 300f;

    public override void _Ready() => _world = Phase1Scenario.Build(seed: 42);

    public override void _Process(double delta)
    {
        if (!_paused && _mode != UiMode.Sitrep)
        {
            _simSeconds += _timeScale * delta;
            _world.Sim.RunUntil((long)_simSeconds);
        }

        QueueRedraw();
    }

    // ---- input ----

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true } mb:
                OnMouse(mb);
                break;
            case InputEventKey { Pressed: true } key:
                OnKey(key);
                break;
        }
    }

    private void OnMouse(InputEventMouseButton mb)
    {
        if (mb.ButtonIndex == MouseButton.Right)
        {
            long body = BodyAtScreen(mb.Position);
            if (body >= 0)
            {
                _menuPort = PortForBody(body);
                _menuPos = mb.Position;
                _mode = UiMode.Menu;
            }
            else
            {
                _mode = UiMode.None; // right-click empty space cancels
            }

            return;
        }

        if (mb.ButtonIndex == MouseButton.Left && _mode == UiMode.Menu)
        {
            int row = (int)((mb.Position.Y - (_menuPos.Y + MenuRow)) / MenuRow);
            var rect = new Rect2(_menuPos, new Vector2(MenuWidth, MenuRow * (PlayerShips.Length + 1)));
            if (rect.HasPoint(mb.Position) && row >= 0 && row < PlayerShips.Length)
            {
                _sitrepShip = PlayerShips[row];
                _sitrepDigits = "";
                _mode = UiMode.Sitrep;
            }
            else
            {
                _mode = UiMode.None;
            }
        }
    }

    private void OnKey(InputEventKey key)
    {
        if (_mode == UiMode.Sitrep)
        {
            OnSitrepKey(key.Keycode);
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
        }
    }

    private void OnSitrepKey(Key kc)
    {
        if (kc >= Key.Key0 && kc <= Key.Key9 && _sitrepDigits.Length < 4)
        {
            _sitrepDigits += (char)('0' + (kc - Key.Key0));
        }
        else if (kc == Key.Backspace && _sitrepDigits.Length > 0)
        {
            _sitrepDigits = _sitrepDigits[..^1];
        }
        else if (kc is Key.Enter or Key.KpEnter)
        {
            ConfirmDispatch();
        }
        else if (kc == Key.Escape)
        {
            _mode = UiMode.None;
        }
    }

    private void ConfirmDispatch()
    {
        long days = _sitrepDigits.Length > 0 ? long.Parse(_sitrepDigits) : 10;
        days = System.Math.Max(days, 1);

        if (_world.EntitySpatial(_sitrepShip) is Ship ship)
        {
            var ev = ShipCommands.IssueDispatch(_world, ship, ObserverId, _menuPort, days * Day);
            _pending = (_menuPort, _world.NowSeconds, _world.Knowledge.ReceptionTime(ship.Id, ev));
        }

        _mode = UiMode.None;
    }

    // ---- draw ----

    public override void _Draw()
    {
        Vector2 center = Center();
        long now = (long)_simSeconds;
        Font font = ThemeDB.FallbackFont;

        DrawCircle(center, 6f, new Color(1f, 0.85f, 0.3f)); // Sun at the heliocentric origin
        foreach ((long body, long _port, Color color, string name) in Worlds)
        {
            (long x, long y, long _z) = _world.EntitySpatial(body).PositionMmAt(now);
            Vector2 p = ToScreen(center, x, y);
            DrawCircle(p, 5f, color);
            DrawString(font, p + new Vector2(8, -6), name, HorizontalAlignment.Left, -1, 11, new Color(color, 0.7f));
        }

        DrawShips(center, now, font);
        DrawHud(now, font);
        DrawMenu(now, font);
    }

    private void DrawShips(Vector2 center, long now, Font font)
    {
        foreach ((long shipId, ShipKnowledge k) in ShipView.Read(_world.Knowledge, ObserverId, now))
        {
            bool docked = k.Ghost is { Cause: TelemetryCause.Arrived } && k.Plan is not null;
            if (docked)
            {
                // HQ heard it dock; it rides its port's body, whose position is almanac (§2.7).
                long port = k.Plan!.DestSettlementId;
                (long bx, long by, long _z) = _world.EntitySpatial(BodyForPort(port)).PositionMmAt(now);
                Vector2 d = ToScreen(center, bx, by);
                DrawString(font, d + new Vector2(8, 12), $"#{shipId} docked at {PortName(port)}",
                    HorizontalAlignment.Left, -1, 12, new Color(0.6f, 0.9f, 0.7f));
                continue;
            }

            if (k.Plan is { } plan)
            {
                (long dx, long dy, long _p) = plan.PredictedPositionMmAt(now);
                (long ex, long ey, long _e) = plan.PredictedPositionMmAt(plan.ArriveSeconds);
                DrawLine(ToScreen(center, plan.X, plan.Y), ToScreen(center, ex, ey), new Color(0.3f, 0.5f, 0.4f), 1f);
                DrawCircle(ToScreen(center, dx, dy), 3f, new Color(0.4f, 0.9f, 0.6f));
            }

            if (k.Ghost is { } ghost)
            {
                Vector2 g = ToScreen(center, ghost.X, ghost.Y);
                bool deviating = k.DeviationMm() is { } dev && dev > AuMm / 20;
                Color c = deviating ? new Color(1f, 0.35f, 0.35f) : new Color(0.85f, 0.85f, 0.9f);
                DrawCircle(g, 4f, c);
                DrawString(font, g + new Vector2(8, 4),
                    $"#{shipId} {ghost.Cause}  (heard {FmtAge(now - k.GhostOccurredAtSeconds)} ago)",
                    HorizontalAlignment.Left, -1, 12, c);
            }
        }
    }

    private void DrawHud(long now, Font font)
    {
        var line = new Vector2(16, 24);
        DrawString(font, line, $"T+{now / Day}d   x{_timeScale / Day:0.##}day/s{(_paused ? "  [PAUSED]" : "")}   Credits: {_world.Credits:N0}",
            HorizontalAlignment.Left, -1, 14, Colors.White);

        line.Y += 26;
        DrawString(font, line, "Ore prices (last known):", HorizontalAlignment.Left, -1, 13, new Color(0.8f, 0.8f, 0.85f));
        foreach ((long settlementId, PriceQuote q) in PriceBook.Read(_world.Knowledge, ObserverId, now))
        {
            line.Y += 18;
            DrawString(font, line + new Vector2(12, 0),
                $"{PortName(settlementId)}: {q.PriceMinorUnits}  (heard {FmtAge(q.AgeSeconds(now))} ago)",
                HorizontalAlignment.Left, -1, 12, new Color(0.75f, 0.85f, 0.8f));
        }

        line.Y += 26;
        DrawString(font, line, "signal lag from HQ (one way):", HorizontalAlignment.Left, -1, 13, new Color(0.8f, 0.8f, 0.85f));
        foreach ((long body, long _port, Color _c, string name) in Worlds)
        {
            if (body == SolSystem.EarthId)
            {
                continue;
            }

            line.Y += 18;
            DrawString(font, line + new Vector2(12, 0), $"{name}: {FmtLight(LightSeconds(SolSystem.EarthId, body, now))}",
                HorizontalAlignment.Left, -1, 12, new Color(0.75f, 0.8f, 0.9f));
        }

        DrawOrderBanner(now, font);

        DrawString(font, new Vector2(16, GetViewportRect().Size.Y - 14),
            "[space] pause   [+/-] time   right-click a body to send a ship",
            HorizontalAlignment.Left, -1, 12, new Color(0.6f, 0.6f, 0.65f));
    }

    private void DrawMenu(long now, Font font)
    {
        if (_mode == UiMode.Menu)
        {
            int rows = PlayerShips.Length + 1;
            DrawRect(new Rect2(_menuPos, new Vector2(MenuWidth, MenuRow * rows)), new Color(0.05f, 0.07f, 0.12f, 0.95f));
            DrawString(font, _menuPos + new Vector2(8, 15), $"Send to {PortName(_menuPort)}:",
                HorizontalAlignment.Left, -1, 12, new Color(0.9f, 0.9f, 0.6f));

            for (int i = 0; i < PlayerShips.Length; i++)
            {
                (double au, long etaDays) = Estimate(PlayerShips[i], _menuPort, now);
                DrawString(font, _menuPos + new Vector2(12, MenuRow * (i + 1) + 15),
                    $"→ #{PlayerShips[i]}   ({au:0.00} AU, ETA {etaDays}d)",
                    HorizontalAlignment.Left, -1, 12, new Color(0.85f, 0.9f, 0.95f));
            }
        }
        else if (_mode == UiMode.Sitrep)
        {
            var box = new Rect2(_menuPos, new Vector2(MenuWidth, MenuRow * 2));
            DrawRect(box, new Color(0.05f, 0.07f, 0.12f, 0.95f));
            DrawString(font, _menuPos + new Vector2(8, 15), $"SITREP interval for #{_sitrepShip} → {PortName(_menuPort)}",
                HorizontalAlignment.Left, -1, 12, new Color(0.9f, 0.9f, 0.6f));
            DrawString(font, _menuPos + new Vector2(12, MenuRow + 15),
                $"{(_sitrepDigits.Length > 0 ? _sitrepDigits : "10")} days   [Enter] launch  [Esc] cancel",
                HorizontalAlignment.Left, -1, 12, Colors.White);
        }
    }

    private void DrawOrderBanner(long now, Font font)
    {
        if (_pending is not { } order)
        {
            return;
        }

        if (now > order.Reception + (10 * Day))
        {
            _pending = null;
            return;
        }

        var at = new Vector2(16, GetViewportRect().Size.Y - 40);
        if (now < order.Reception)
        {
            DrawString(font, at,
                $"⟳ ORDER → {PortName(order.Target)} in flight: reaches ship in {FmtAge(order.Reception - now)} "
                + $"(travel {FmtAge(order.Reception - order.IssuedAt)})",
                HorizontalAlignment.Left, -1, 13, new Color(1f, 0.8f, 0.4f));
        }
        else
        {
            DrawString(font, at, $"ORDER → {PortName(order.Target)} DELIVERED at T+{order.Reception / Day}d — watch the ghost react",
                HorizontalAlignment.Left, -1, 13, new Color(0.5f, 0.9f, 0.6f));
        }
    }

    // ---- helpers ----

    // AU to the target now, and ETA in days at cruise speed, from the ship's last KNOWN position
    // (predicted from its plan, else its ghost) — an estimate on stale info, like everything (§2.2).
    private (double Au, long EtaDays) Estimate(long shipId, long targetPort, long now)
    {
        (long sx, long sy, long sz) = KnownShipPos(shipId, now);
        (long tx, long ty, long tz) = _world.EntitySpatial(targetPort).PositionMmAt(now);
        double dist = Dist(sx, sy, sz, tx, ty, tz);
        return (dist / AuMm, (long)(dist / Ship.CruiseMmPerSec / Day));
    }

    private (long X, long Y, long Z) KnownShipPos(long shipId, long now)
    {
        IReadOnlyDictionary<long, ShipKnowledge> view = ShipView.Read(_world.Knowledge, ObserverId, now);
        if (view.TryGetValue(shipId, out ShipKnowledge? k))
        {
            if (k.Plan is { } plan && k.Ghost is not { Cause: TelemetryCause.Arrived })
            {
                return plan.PredictedPositionMmAt(now);
            }

            if (k.Ghost is { } g)
            {
                return (g.X, g.Y, g.Z);
            }
        }

        return _world.EntitySpatial(Phase1Scenario.HqId).PositionMmAt(now);
    }

    private long BodyAtScreen(Vector2 pos)
    {
        Vector2 center = Center();
        long now = (long)_simSeconds;
        foreach ((long body, long _port, Color _c, string _n) in Worlds)
        {
            (long x, long y, long _z) = _world.EntitySpatial(body).PositionMmAt(now);
            if (ToScreen(center, x, y).DistanceTo(pos) < 16f)
            {
                return body;
            }
        }

        return -1;
    }

    private double LightSeconds(long fromBodyId, long toBodyId, long t)
    {
        (long ax, long ay, long az) = _world.EntitySpatial(fromBodyId).PositionMmAt(t);
        (long bx, long by, long bz) = _world.EntitySpatial(toBodyId).PositionMmAt(t);
        return Dist(ax, ay, az, bx, by, bz) / Sim.Core.Comms.Reception.SpeedOfLightMmPerSec;
    }

    private static double Dist(long ax, long ay, long az, long bx, long by, long bz)
    {
        double dx = ax - bx, dy = ay - by, dz = az - bz;
        return System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static long PortForBody(long body)
    {
        foreach ((long b, long port, Color _c, string _n) in Worlds)
        {
            if (b == body)
            {
                return port;
            }
        }

        return Phase1Scenario.HqId;
    }

    private static long BodyForPort(long port)
    {
        foreach ((long body, long p, Color _c, string _n) in Worlds)
        {
            if (p == port)
            {
                return body;
            }
        }

        return SolSystem.EarthId;
    }

    private static string PortName(long port)
    {
        foreach ((long _b, long p, Color _c, string name) in Worlds)
        {
            if (p == port)
            {
                return name;
            }
        }

        return $"#{port}";
    }

    private static string FmtLight(double seconds)
        => seconds >= 90 ? $"{seconds / 60.0:0.0} light-min" : $"{seconds:0} light-sec";

    private static string FmtAge(long seconds)
    {
        if (seconds >= Day)
        {
            return $"{seconds / Day}d {(seconds % Day) / 3600}h";
        }

        return seconds >= 3600 ? $"{seconds / 3600}h {(seconds % 3600) / 60}m" : $"{seconds / 60}m {seconds % 60}s";
    }

    private Vector2 Center() => GetViewportRect().Size / 2f;

    private Vector2 ToScreen(Vector2 center, long xMm, long yMm)
        => center + new Vector2((float)(xMm / AuMm) * _pxPerAu, -(float)(yMm / AuMm) * _pxPerAu);
}
