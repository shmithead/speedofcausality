using Sim.Core.Events;
using Sim.Core.Horizons;
using Sim.Core.Knowledge;
using Sim.Core.Rng;
using Sim.Core.Time;

namespace Sim.Core.World;

/// <summary>
/// The composition root for a running simulation (roadmap §2): it wires the append-only
/// <see cref="EventLog"/> (source of truth, §2.1), the discrete-event <see cref="Simulation"/>
/// scheduler (§2.4), the <see cref="HorizonManager"/> (sparse ticks, §2.7), the seeded
/// <see cref="RngStreams"/> (§2.3 r4), and the <see cref="KnowledgeProjection"/> (information-lag,
/// §2.2) over one entity registry.
///
/// <para>Everything in Phase 0 was a standalone primitive; this is where they first become a world.
/// The frontend never touches any of these directly — it reads the player's knowledge fold and
/// issues commands (§2, the boundary), which is the render rule made structural.</para>
///
/// <para><b>Determinism of the event queue.</b> The scheduler needs a globally unique
/// <c>(TimeSeconds, Ordinal)</c> per queued event (§2.3 r7). Ordinals are content-derived and
/// partitioned by kind into disjoint ranges (see <see cref="Ordinals"/>) so a horizon expiry and a
/// signal reception can never tie, and two of the same kind differ by their content id.</para>
/// </summary>
public sealed class SimWorld
{
    private readonly SortedDictionary<long, ISpatial> _spatial = new();
    private readonly SortedSet<long> _observers = new();
    private readonly SortedDictionary<long, IPriceSource> _markets = new();
    private long _nextEventId = 1;

    public SimWorld(ulong worldSeed, long startSeconds = 0)
    {
        Sim = new Simulation(startSeconds);
        Horizons = new HorizonManager(Sim);
        Rng = new RngStreams(worldSeed);
        Log = new EventLog();
        Knowledge = new KnowledgeProjection(Log, EntitySpatial, InScope);
    }

    /// <summary>The discrete-event scheduler (§2.4).</summary>
    public Simulation Sim { get; }

    /// <summary>Validity-horizon bookkeeping over <see cref="Sim"/> (§2.7).</summary>
    public HorizonManager Horizons { get; }

    /// <summary>Named, seeded RNG streams, one per subsystem (§2.3 r4).</summary>
    public RngStreams Rng { get; }

    /// <summary>The authoritative append-only event log (§2.1).</summary>
    public EventLog Log { get; }

    /// <summary>The information-lag query: <c>Knowledge(entity, T)</c> (§2.2).</summary>
    public KnowledgeProjection Knowledge { get; }

    /// <summary>Current sim-time in seconds.</summary>
    public long NowSeconds => Sim.NowSeconds;

    /// <summary>
    /// The player firm's cash balance in minor currency units (§2.5). Banking is treated as central and
    /// instant; only the physical <i>reports</i> of a distant trade are light-lagged (§2.2). Updated by
    /// a ship trading at a port.
    /// </summary>
    public long Credits { get; set; }

    /// <summary>Registers a settlement's market so a ship docking there can trade against its real price.</summary>
    public void RegisterMarket(long settlementId, IPriceSource market) => _markets[settlementId] = market;

    /// <summary>The market at <paramref name="settlementId"/>, if the settlement has one.</summary>
    public bool TryGetMarket(long settlementId, out IPriceSource market) => _markets.TryGetValue(settlementId, out market!);

    /// <summary>Ids of entities with a receiver, in a defined (sorted) order (§2.3 r3).</summary>
    public IReadOnlyCollection<long> Observers => _observers;

    /// <summary>
    /// Registers an entity's position provider and whether it has a receiver. Bodies, settlements,
    /// ships, and the player's HQ all live here; only entities that can <i>learn</i> things are
    /// observers.
    /// </summary>
    public void AddEntity(long id, ISpatial spatial, bool isObserver)
    {
        _spatial.Add(id, spatial);
        if (isObserver)
        {
            _observers.Add(id);
        }
    }

    /// <summary>The position provider for <paramref name="id"/> (body, settlement, ship).</summary>
    public ISpatial EntitySpatial(long id) => _spatial[id];

    /// <summary>
    /// Records a domain fact as it occurs: appends it to the log at the current sim-time with the next
    /// event id. This is an <i>occurrence</i>, not a reception — who learns of it, and when, is the
    /// knowledge model's job (§2.2), computed from geometry, never assumed instantaneous.
    /// </summary>
    public EventRecord Emit(long originEntity, IEventPayload payload, long? causalParent = null)
    {
        var ev = new EventRecord(_nextEventId++, Sim.NowSeconds, originEntity, causalParent, payload);
        Log.Append(ev);
        return ev;
    }

    /// <summary>
    /// Schedules a decision-relevant signal to wake <paramref name="observerId"/> when it arrives
    /// (§2.2, §2.7): computes the reception time and enqueues a callback for that instant. Reserved
    /// for signals that could change a decider's plan — a countermand reaching a ship — not for the
    /// ambient facts the player merely renders (those are read on demand via <see cref="Knowledge"/>).
    /// </summary>
    public void ScheduleReception(long observerId, EventRecord ev, Action<ISimContext> onArrive)
    {
        long receptionTime = Knowledge.ReceptionTime(observerId, ev);
        Sim.Schedule(new ReceptionEvent(receptionTime, Ordinals.Reception + ev.Id, onArrive));
    }

    // §2.2 event scope, recomputable from persisted data. Phase 1 fully implements Broadcast (the
    // ambient world), Direct (an addressed message), and Local; Settlement/Regional are treated as
    // Broadcast until Phase 3 signal regulation refines them (§3).
    private static bool InScope(EventRecord ev, long observerId)
    {
        if (ev.Payload is not IScoped scoped)
        {
            return true; // default: broadcast
        }

        return scoped.Scope switch
        {
            EventScope.Local => observerId == ev.OriginEntity,
            EventScope.Direct => observerId == scoped.Recipient,
            _ => true, // Settlement, Regional, Broadcast — full reach in Phase 1
        };
    }

    private sealed class ReceptionEvent : ISimEvent
    {
        private readonly Action<ISimContext> _onArrive;

        public ReceptionEvent(long timeSeconds, long ordinal, Action<ISimContext> onArrive)
        {
            TimeSeconds = timeSeconds;
            Ordinal = ordinal;
            _onArrive = onArrive;
        }

        public long TimeSeconds { get; }

        public long Ordinal { get; }

        public void Apply(ISimContext ctx) => _onArrive(ctx);
    }
}
