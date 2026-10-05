using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class VanillaSlimeContainedWorld1458(WorldTileStore tiles,
    RuntimeTileManipulationReplicationRegistry? tileReplication = null,
    RuntimeWorldItemStore? worldItems = null,
    RuntimeNpcStore? protectionNpcs = null) : IVanillaSlimeContainedEnvironment1458
{
    public bool TryCapture(in NpcSnapshot parent, in VanillaNpcTargetCandidate target,
        out IVanillaSlimeContainedWorld1458 world)
    {
        world = default!;
        if (!VanillaNpcDefinitionCatalog.TryGet(parent.TypeIdentity, parent.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(parent.Simulation, out var body))
            return false;

        // Include pre-motion birth, contained-body expansion and the complete collision sweep.
        float marginX = body.Width * Math.Max(2f, parent.Simulation.Scale * 2f) + Math.Abs(parent.VelocityX) + 64f;
        float marginY = body.Height * Math.Max(2f, parent.Simulation.Scale * 2f) + Math.Abs(parent.VelocityY) + 64f;
        float left = Math.Min(parent.PositionX - marginX, target.CenterX - target.Width);
        float right = Math.Max(parent.PositionX + body.Width + marginX, target.CenterX + target.Width);
        float top = Math.Min(parent.PositionY - marginY, target.CenterY - target.Height);
        float bottom = Math.Max(parent.PositionY + body.Height + marginY, target.CenterY + target.Height);
        if (!float.IsFinite(left) || !float.IsFinite(right) || !float.IsFinite(top) || !float.IsFinite(bottom))
            return false;

        int x0 = (int)Math.Clamp(left / 16f, 0f, tiles.Dimensions.WidthTiles - 1);
        int x1 = (int)Math.Clamp(right / 16f, 0f, tiles.Dimensions.WidthTiles - 1);
        int y0 = (int)Math.Clamp(top / 16f, 0f, tiles.Dimensions.HeightTiles - 1);
        int y1 = (int)Math.Clamp(bottom / 16f, 0f, tiles.Dimensions.HeightTiles - 1);
        var first = TerrariaSectionGeometry.FromTile(tiles.Dimensions, x0, y0);
        var last = TerrariaSectionGeometry.FromTile(tiles.Dimensions, x1, y1);
        int columns = last.X - first.X + 1;
        int rows = last.Y - first.Y + 1;
        if ((long)columns * rows > 2048)
            return false;

        var versions = new long[columns * rows];
        for (int index = 0; index < versions.Length; index++)
        {
            long version = tiles.GetSectionVersion(new(first.X + index % columns, first.Y + index / columns));
            if ((version & 1) != 0)
                return false;
            versions[index] = version;
        }
        var captured = new CapturedWorld(tiles, first, columns, versions, tileReplication, worldItems, protectionNpcs,
            VanillaWorldCanHit.HasLineOfSight(tiles, parent.PositionX, parent.PositionY, body.Width, body.Height,
                target.CenterX - target.Width * .5f, target.CenterY - target.Height * .5f,
                (int)target.Width, (int)target.Height));
        world = captured;
        return world.IsCurrent;
    }

    private sealed partial class CapturedWorld(WorldTileStore tiles, WorldSectionId first, int columns,
        long[] versions, RuntimeTileManipulationReplicationRegistry? tileReplication, RuntimeWorldItemStore? worldItems,
        RuntimeNpcStore? protectionNpcs,
        bool canHit) : IVanillaSlimeContainedWorld1458
    {
        public bool CanHit => canHit;
        public bool IsCurrent
        {
            get
            {
                for (int index = 0; index < versions.Length; index++)
                    if (tiles.GetSectionVersion(new(first.X + index % columns, first.Y + index / columns)) != versions[index])
                        return false;
                return ProducerIsCurrent();
            }
        }

        public bool TryReadBirthWet(in NpcSnapshot birth, out bool wet)
        {
            wet = false;
            if (!IsCurrent || !VanillaNpcDefinitionCatalog.TryGet(birth.TypeIdentity, birth.NetIdentity, out var definition) ||
                !definition.TryResolveHitbox(birth.Simulation, out var body))
                return false;
            // NewNPC initializes only npc.wet. Honey/lava/shimmer flags wait for the next outer update.
            wet = VanillaWorldCollision.GetLiquidContacts(tiles, birth.PositionX, birth.PositionY,
                body.Width, body.Height).Wet;
            return IsCurrent;
        }
    }
}
