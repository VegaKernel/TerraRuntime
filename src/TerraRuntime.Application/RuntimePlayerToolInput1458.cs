using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal readonly record struct ToolWireCell1458(int X, int Y);

internal readonly record struct ToolWireObservation1458(
    TerrariaTileManipulationState? Tile, TerrariaTilePickingState1458? Picking);

// One bounded, ordered input cohort within the existing tool-use owner. These
// observations grant no hit credit; the source action/world plan must match them.
internal sealed partial class RuntimePlayerToolInput1458(ToolDefinition1458 definition)
{
    internal const int MaximumObservations = 36; // Nine cells, two PickTile calls, two relevant frames/call.
    private readonly ToolWireObservation1458[] observations = new ToolWireObservation1458[MaximumObservations];
    private RuntimePlayerToolActorCapture1458? actor;
    private int count;
    internal ToolDefinition1458 Definition { get; } = definition;
    internal RuntimePlayerToolActorCapture1458? Actor => actor;
    internal int Count => count;
    internal TerrariaProjectileUpdateState? ShootReport { get; private set; }
    internal TerrariaToolTileSquareState1458? SquareReport { get; private set; }
    internal TerrariaPlaceObjectState? ObjectReport { get; private set; }
    internal TerrariaChestUpdatesState1458? ChestReport { get; private set; }
    internal TerrariaLucyAxeMessageState1458? LucyReport { get; private set; }
    internal int LucyObservationIndex { get; private set; } = -1;
    internal bool LucyPublicationConsumed { get; private set; }

    internal ulong Revision { get; private set; }
    internal bool Invalid { get; private set; }
    internal ReadOnlySpan<ToolWireObservation1458> Observations => observations.AsSpan(0, count);
    internal bool IsCurrent => !Invalid && actor?.IsCurrent == true &&
        (PlacementReads is null || PlacementReads.IsCurrent) && (WiringReads is null || WiringReads.IsCurrent);

    internal void BindActor(RuntimePlayerToolActorCapture1458 value)
    {
        if (actor is not null) throw new InvalidOperationException("A tool input cohort is bound once.");
        actor = value;
    }

    // Called only in the callback-free tail of a completely validated owned
    // player phase. Neither raw ingress nor a provider may use this lineage seam.
    internal bool TryRebaseAfterOwnedPhase(RuntimePlayerToolActorCapture1458 expected,
        ulong expectedRevision, RuntimePlayerToolActorCapture1458 accepted)
    {
        if (Invalid || !ReferenceEquals(actor, expected) || Revision != expectedRevision ||
            Revision == ulong.MaxValue || !accepted.IsCurrent ||
            accepted.Connection != expected.Connection ||
            !ReferenceEquals(accepted.Phase.ToolUse?.Input, this)) return false;
        actor = accepted;
        Revision++; // Old world preparations must not survive the known clock transition.
        return true;
    }

    // Packet13 carries no mouse center. This enumerates only common possible
    // source centers, preserving the x-major/y-minor observation order. The world
    // planner must still prove each reported leg and may not execute unseen cells.
    internal bool TryGetShovelCenters(Span<ToolWireCell1458> centers, out int written)
    {
        written = 0;
        if (Definition.Item.Value != 4711 || !IsCurrent || count == 0 || centers.Length < 9 ||
            observations[0].Tile is not { } first) return false;
        for (int x = first.TileX - 1; x <= first.TileX + 1; x++)
        for (int y = first.TileY - 1; y <= first.TileY + 1; y++)
        {
            int previous = -1, repeats = 0;
            bool possible = true;
            for (int i = 0; i < count; i++)
            {
                if (observations[i].Tile is not { } tile) continue;
                int dx = tile.TileX - x, dy = tile.TileY - y;
                if (tile.Action != 0 || dx is < -1 or > 1 || dy is < -1 or > 1)
                { possible = false; break; }
                int order = (dx + 1) * 3 + dy + 1;
                repeats = order == previous ? repeats + 1 : 1;
                if (order < previous || repeats > 2)
                { possible = false; break; }
                previous = order;
            }
            if (possible) centers[written++] = new(x, y);
        }
        return written != 0;
    }

    internal bool TryObserve(in TerrariaToolTileSquareState1458 report)
    {
        if (!IsCurrent || Definition.Hammer <= 0 || report.Tile.LiquidAmount != 0 ||
            !TerrariaTileSquareCodec.TryEncodeToolFrame(in report, out _)) return false;
        if (SquareReport is { } existing) return TerrariaTileSquareCodec.HasSameToolWire(in existing, in report);
        if (Revision == ulong.MaxValue) { Invalid = true; return false; }
        SquareReport = report;
        Revision++;
        return true;
    }

    internal bool TryObserve(in TerrariaLucyAxeMessageState1458 report)
    {
        if (!IsCurrent || Definition.Item.Value != 5095 || !report.IsValid || report.Source is < 4 or > 6)
            return false;
        if (LucyReport is { } existing) return existing == report;
        if (Revision == ulong.MaxValue) { Invalid = true; return false; }
        LucyReport = report;
        LucyObservationIndex = count;
        Revision++;
        return true;
    }

    // Pure one-shot visual publication marker, consumed BEFORE any relay callback.
    // This is not an RNG/world entitlement and deliberately does not bump Revision.
    internal bool TryConsumeLucyPublication()
    {
        if (LucyReport is null || LucyPublicationConsumed) return false;
        LucyPublicationConsumed = true;
        return true;
    }

    internal bool TryObserve(in TerrariaChestUpdatesState1458 report)
    {
        if (!IsCurrent || report.Action is not (1 or 3 or 5) || count == 0 ||
            observations[count - 1].Tile is not { Action: 0, Data: 1 } failed ||
            failed.TileX != report.TileX || failed.TileY != report.TileY) return false;
        if (ChestReport is { } existing) return existing == report;
        if (Revision == ulong.MaxValue) { Invalid = true; return false; }
        // This completes the observed CLIENT chronology, not a successful local
        // tile removal. Local Prune still sees the chest; the SERVER plan owns
        // any actual object/chest removal, and ignores raw claimed custody/index.
        ChestReport = report;
        Revision++;
        return true;
    }

    internal bool TryObserve(in TerrariaPlaceObjectState report)
    {
        if (!IsCurrent || Definition.Item.Value != 5295) return false;
        if (ObjectReport is { } existing) return existing == report;
        if (Revision == ulong.MaxValue) { Invalid = true; return false; }
        // The correlated tree plan owns the actual TileObject output, including
        // Alternate/Random/Direction. This observation supplies none of them as
        // authority and consumes no acorn or unrelated inventory quantity.
        ObjectReport = report;
        Revision++;
        return true;
    }

    internal bool TryObserve(in TerrariaProjectileUpdateState report)
    {
        if (!IsCurrent || Definition.Shoot <= 0 || report.ProjectileType != Definition.Shoot ||
            report.Key.Spawner != actor!.Connection.Player.Slot.Value) return false;
        if (ShootReport is { } existing) return existing == report;
        if (Revision == ulong.MaxValue) { Invalid = true; return false; }
        // This is a bounded observation, never a birth or trusted AI assignment.
        // The source launch plan and exact epoch still have to match it.
        ShootReport = report;
        Revision++;
        return true;
    }

    internal bool TryObserve(in TerrariaTileManipulationState report)
    {
        if (!IsCurrent || count == MaximumObservations || Revision == ulong.MaxValue)
        {
            Invalid = true;
            return false;
        }
        observations[count++] = new(report, null);
        Revision++;
        return true;
    }

    internal bool TryObserve(in TerrariaTilePickingState1458 report)
    {
        if (!IsCurrent || count == 0 || count == MaximumObservations || Revision == ulong.MaxValue ||
            observations[count - 1].Tile is not { Action: 0, Data: 1 } failed ||
            failed.TileX != report.TileX || failed.TileY != report.TileY)
            return false;
        observations[count++] = new(null, report);
        Revision++;
        return true;
    }
}
