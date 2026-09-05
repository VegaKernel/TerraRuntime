using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class ServerRuntimeState
{
    public void Tick()
    {
        _runtime.Players.AdvanceCombatTick(Updates);
        _runtime.WorldTileAuthority.AdvanceTo(Updates);
        _runtime.WorldTileAuthority.TickLiquids();

        _runtime.Npcs.CommitPending();
        _runtime.ServerPlayers?.TickPhysics(_runtime.PlayerSnapshots);
        _runtime.Npcs.TickSimulation();
        _runtime.NpcPlayerCombat.Tick(Updates);
        if (_runtime.Projectiles.TryTickState())
        {
            ReadOnlySpan<RuntimeProjectileExplosionEvent> explosions = _runtime.Projectiles.PendingExplosions;
            _runtime.Npcs.TickProjectileInteractions(explosions);
            _runtime.ProjectilePlayerCombat.Tick(explosions);
            _runtime.Projectiles.ApplyReflections();
        }
        _runtime.WorldItems.TickInstancedLeases();

        _runtime.WorldClock?.Tick();
        _runtime.Updates.Advance();
    }

    private bool IsTileActorFree(int tileX, int tileY)
    {
        if (_runtime.WorldTiles is null)
            return false;
        if ((uint)tileX >= (uint)_runtime.WorldTiles.Dimensions.WidthTiles || (uint)tileY >= (uint)_runtime.WorldTiles.Dimensions.HeightTiles)
            return false;
        int tileLeft = tileX * 16;
        int tileTop = tileY * 16;
        int tileRight = tileLeft + 16;
        int tileBottom = tileTop + 16;
        foreach (RuntimePlayerMember player in _runtime.Players.Members)
        {
            if (player.IsDead)
                continue;
            if (Intersects(
                    player.PositionX,
                    player.PositionY,
                    PlayerAuthority.VanillaBasePlayerWidth,
                    PlayerAuthority.VanillaBasePlayerHeight,
                    tileLeft,
                    tileTop,
                    tileRight,
                    tileBottom))
                return false;
        }
        if (_runtime.ServerPlayers?.IntersectsLivingPlayer(
                tileLeft,
                tileTop,
                tileRight,
                tileBottom,
                PlayerAuthority.VanillaBasePlayerWidth,
                PlayerAuthority.VanillaBasePlayerHeight) == true)
        {
            return false;
        }
        var npcBuffer = new NpcSnapshot[_runtime.Npcs.Capacity];
        int npcCount = _runtime.Npcs.CopyActive(npcBuffer);
        for (int i = 0; i < npcCount; i++)
        {
            if (!IsNpcFree(npcBuffer[i], tileLeft, tileTop, tileRight, tileBottom))
                return false;
        }
        return true;
    }

    private static bool IsNpcFree(in NpcSnapshot npc, int tileLeft, int tileTop, int tileRight, int tileBottom)
    {
        if (!npc.IsActive)
            return true;
        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId type) ||
            !VanillaNpcDefinitionCatalog.TryGet(type, npc.NetIdentity, out VanillaNpcDefinition definition))
        {
            return !Intersects(npc.PositionX, npc.PositionY, 16f, 16f, tileLeft, tileTop, tileRight, tileBottom);
        }
        if (!definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
            return true;
        return !Intersects(npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height, tileLeft, tileTop, tileRight, tileBottom);
    }

    private static bool Intersects(float rx, float ry, float rw, float rh, int tx0, int ty0, int tx1, int ty1)
    {
        float rx1 = rx + rw;
        float ry1 = ry + rh;
        return rx < tx1 && rx1 > tx0 && ry < ty1 && ry1 > ty0;
    }

    internal bool IsTileActorFreeForTesting(int tileX, int tileY) => IsTileActorFree(tileX, tileY);
}
