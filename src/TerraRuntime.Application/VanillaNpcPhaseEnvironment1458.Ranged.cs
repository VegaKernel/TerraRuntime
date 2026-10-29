using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class VanillaNpcPhaseEnvironment1458
{
    private sealed partial class Capture
    {
        private static bool IsHarmlessMaggotPair(in NpcSnapshot actor, in NpcSnapshot peer) =>
            // Actual source pairs overlap without damage or RNG offers, including
            // the tick after their 90-tick catch protection expires. Unknown or
            // nonzero live damage still uses the existing whole-contact fence.
            actor.TypeIdentity == VanillaNpcIds.Maggot && actor.NetId == actor.Type &&
            peer.TypeIdentity == VanillaNpcIds.Maggot && peer.NetId == peer.Type &&
            actor.Simulation.Life > 0 && peer.Simulation.Life > 0 &&
            (actor.Simulation.DamageOverride ?? actor.Simulation.BaseDamage) is 0 &&
            (peer.Simulation.DamageOverride ?? peer.Simulation.BaseDamage) is 0;

        private bool TryHasScutlixRider(byte mountSlot, out bool mounted)
        {
            mounted = false;
            if (!IsCurrent) return false;
            int active = npcs.CopyActive(peerScratch);
            foreach (var peer in peerScratch.AsSpan(0, active))
                if (peer.TypeIdentity == VanillaNpcIds.ScutlixRider && peer.Ai.Ai0 == mountSlot)
                { mounted = true; break; }
            return IsCurrent;
        }

        private bool? RockFrameVisibility(in NpcStateUpdate state, in VanillaNpcRawPlayer1458 target)
        {
            if (state.Type != 631 || state.VelocityY != 0f || state.Ai.Ai2 == 0f || state.Ai.Ai2 >= 100f ||
                !VanillaNpcDefinitionCatalog.TryGet(new(state.Type), new(state.NetId), out var definition) ||
                !definition.TryResolveHitbox(state.Simulation, out var body)) return null;
            bool facing = target.PositionX + target.Width * .5f < state.PositionX + body.Width * .5f
                ? state.Simulation.DirectionX == -1 : state.Simulation.DirectionX == 1;
            bool overlaps = (int)target.PositionX < (int)state.PositionX + body.Width &&
                (int)target.PositionX + target.Width > (int)state.PositionX &&
                (int)target.PositionY < (int)state.PositionY + body.Height &&
                (int)target.PositionY + target.Height > (int)state.PositionY;
            return facing && !overlaps ? CanHit(target.PositionX, target.PositionY, target.Width, target.Height,
                state.PositionX, state.PositionY, body.Width, body.Height) : null;
        }

        public bool TryCountNpcType(NpcTypeId type, out int count)
        {
            count = 0;
            if (!IsCurrent) return false;
            int active = npcs.CopyActive(peerScratch);
            foreach (var npc in peerScratch.AsSpan(0, active))
                if (npc.TypeIdentity == type) count++;
            // The enclosing AiSpawnPreview fences the complete NPC-store mutation
            // serial, including additions to slots absent from this active census.
            return IsCurrent;
        }

        public bool TryGetTurretSite(int tileX, int tileY, out bool eligible)
        {
            eligible = false;
            if (!IsCurrent || tileX < 1 || tileX >= tiles.Dimensions.WidthTiles - 1 ||
                tileY < 4 || tileY >= tiles.Dimensions.HeightTiles) return false;
            terrain.FenceBody((tileX - 1) * 16f, (tileY - 4) * 16f, 0f, 0f, 48, 80);
            var floor = tiles.Get(tileX, tileY);
            if (!floor.IsActive || floor.IsActuated || !VanillaTileCollisionCatalog.IsSolid(floor.TileType) ||
                tiles.Get(tileX, tileY - 1).LiquidKind == WorldLiquidKind.Lava) return IsCurrent;
            // Collision.SolidTiles rejects this lower world band before scanning.
            // Its clearance excludes solid-top tiles, unlike the selected floor.
            if (tileY - 1 >= tiles.Dimensions.HeightTiles - 40) return IsCurrent;
            for (int x = tileX - 1; x <= tileX + 1; x++)
                for (int y = tileY - 4; y < tileY; y++)
                {
                    var cell = tiles.Get(x, y);
                    if (cell.IsActive && !cell.IsActuated && VanillaTileCollisionCatalog.IsSolid(cell.TileType) &&
                        !VanillaTileCollisionCatalog.IsSolidTop(cell.TileType)) return IsCurrent;
                }
            eligible = true;
            return IsCurrent;
        }

        public bool TryFinishMartianChild(RuntimeNpcStore.AiSpawnPreview preview, in NpcSnapshot birth,
            float finalX, float finalY, in NpcAiState finalAi, out NpcSnapshot finished)
        {
            finished = default;
            if (!IsCurrent || !VanillaNpcDefinitionCatalog.TryGet(birth.TypeIdentity, birth.NetIdentity, out var definition) ||
                !definition.TryResolveHitbox(birth.Simulation, out var body)) return false;
            // NewNPC skips WetCollision for noTileCollide. Otherwise it observes
            // liquid before AI003 applies its subsequent position/AI writes.
            bool wet = birth.Simulation.Wet;
            if (!birth.Simulation.NoTileCollide)
            {
                terrain.FenceBody(birth.PositionX, birth.PositionY, 0f, 0f, body.Width, body.Height);
                wet = VanillaWorldCollision.TryGetWetContact(tiles, birth.PositionX, birth.PositionY,
                    body.Width, body.Height, out _);
            }
            return IsCurrent && preview.TryFinishMartianChild(in birth, finalX, finalY, in finalAi, wet, out finished);
        }
    }
}
