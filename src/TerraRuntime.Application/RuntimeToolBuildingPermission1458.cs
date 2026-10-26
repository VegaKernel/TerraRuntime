using System.Drawing;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>World-owned DD2 building restriction, separate from the consequent client buff199.</summary>
internal sealed class RuntimeToolBuildingPermission1458
{
    private long revision;
    private bool? ongoing;
    private Rectangle? arena;

    internal RuntimeToolBuildingPermission1458(WorldTileStore tiles) =>
        Tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    internal WorldTileStore Tiles { get; }
    internal bool? KnownEventOngoing => ongoing;

    internal static RuntimeToolBuildingPermission1458 FromLoadedWorld(WorldTileStore tiles)
    {
        var state = new RuntimeToolBuildingPermission1458(tiles);
        // WorldGen.clearWorld calls DD2Event.ResetProgressEntirely; Ongoing is not a .wld field.
        // This is an explicit owner initialization, not a false inferred from a missing provider.
        if (!state.TrySetSourceEvent(false, null))
            throw new InvalidOperationException("Failed to initialize world tool permission.");
        return state;
    }

    // The event owner supplies its source ArenaHitbox. This does not implement DD2 spawning,
    // cooldown scheduling or infer an arena from an untrusted buff/packet/NPC-presence claim.
    internal bool TrySetSourceEvent(bool? eventOngoing, Rectangle? sourceArena)
    {
        if (revision == long.MaxValue) return false;
        if (sourceArena is { Width: < 0 } or { Height: < 0 }) return false;
        if (eventOngoing != true) sourceArena = null;
        if (ongoing == eventOngoing && arena == sourceArena) return true;
        revision++;
        ongoing = eventOngoing;
        arena = sourceArena;
        return true;
    }

    internal bool TryCapture(float centerX, float centerY, out RuntimeToolBuildingCapture1458 capture)
    {
        capture = default;
        if (ongoing is not { } active || revision == long.MaxValue ||
            !float.IsFinite(centerX) || !float.IsFinite(centerY) ||
            centerX < int.MinValue || (double)centerX > int.MaxValue ||
            centerY < int.MinValue || (double)centerY > int.MaxValue || active && arena is null)
            return false;
        // Source ShouldBlockBuilding uses Rectangle.Contains(Vector2.ToPoint()), not floor.
        bool blocked = active && arena!.Value.Contains((int)centerX, (int)centerY);
        capture = new(this, revision, blocked);
        return true;
    }

    internal bool IsCurrent(long expected) => revision != long.MaxValue && expected == revision && ongoing.HasValue;
}

internal readonly record struct RuntimeToolBuildingCapture1458(
    RuntimeToolBuildingPermission1458? Owner, long Revision, bool NoBuilding)
{
    internal bool IsCurrent => Owner is not null && Owner.IsCurrent(Revision);
}
