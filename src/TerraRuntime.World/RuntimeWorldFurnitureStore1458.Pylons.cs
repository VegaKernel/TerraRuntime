using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public readonly record struct WorldPylonInfo1458(short X, short Y, int SourceStyle);

public sealed partial class RuntimeWorldFurnitureStore1458
{
    // A derived index over this SAME canonical tile-entity image, not another entity store.
    // Keep it inside Image so a prepared mutation preserves the exact existing projection.
    public WorldPylonInfo1458[] CapturePylonSnapshot() => Volatile.Read(ref image).Pylons.ToArray();

    internal int PylonUpdateCooldown => Volatile.Read(ref image).PylonCooldown;

    internal void RequestImmediatePylonUpdate(bool sourceClient = false)
    {
        if (sourceClient) return;
        if (!VanillaMultiTileObjectCatalog.TryGet(VanillaTileIds.TeleportationPylon, out var definition))
            throw new InvalidOperationException("Missing source pylon object definition.");
        var current = Volatile.Read(ref image);
        var pylons = new List<WorldPylonInfo1458>();
        foreach (int id in current.Positions.Values)
        {
            var row = current.Slots[current.Indices[id]]!;
            if (row.Kind != WorldTileEntityKind.TeleportationPylon) continue;
            var tile = Tiles.Get(row.X, row.Y);
            if (tile.IsActive && tile.TileType == VanillaTileIds.TeleportationPylon)
                pylons.Add(new(row.X, row.Y, tile.FrameX / definition.FrameXPeriod));
        }
        Volatile.Write(ref image, current with { Pylons = pylons.ToArray(), PylonCooldown = int.MaxValue });
    }

}
