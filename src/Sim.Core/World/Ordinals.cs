namespace Sim.Core.World;

/// <summary>
/// Disjoint ordinal bands for the discrete-event queue (roadmap §2.3 r7). The scheduler needs a
/// globally unique <c>(TimeSeconds, Ordinal)</c> per queued event so processing order is a function
/// of content alone, never of scheduling order. Each event kind draws its ordinal from its own band
/// plus a content id (entity id, event id, ship id) that is unique within the kind, so no two queued
/// events can ever tie.
///
/// <para>Entity ids therefore must stay below <see cref="Reception"/> — they are allocated as small
/// constants in scenario setup, and are used raw as the horizon-expiry ordinal by
/// <see cref="Horizons.HorizonManager"/>.</para>
/// </summary>
internal static class Ordinals
{
    /// <summary>Horizon expiries (market fixings, decision points): ordinal = entity id, in <c>[0, Reception)</c>.</summary>
    public const long HorizonMax = 1L << 52;

    /// <summary>Signal receptions (§2.2): ordinal = <see cref="Reception"/> + event id.</summary>
    public const long Reception = 1L << 52;

    /// <summary>Ship arrival pings: ordinal = <see cref="Arrival"/> + ship id.</summary>
    public const long Arrival = 1L << 53;
}
