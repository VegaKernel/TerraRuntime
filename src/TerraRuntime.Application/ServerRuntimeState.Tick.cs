using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed partial class ServerRuntimeState
{
    public void Tick()
    {
        _worldTileAuthority.AdvanceTo(Updates);

        _npcs.CommitPending();
        _serverPlayers?.TickPhysics(this);
        _npcs.TickSimulation();
        if (_projectiles.TryTickState())
        {
            _npcs.TickProjectileInteractions();
            _projectiles.ApplyReflections();
        }
        _worldItems.TickInstancedLeases();

        _worldClock?.Tick();
        Updates++;
    }

    private bool IsTileActorFree(int tileX, int tileY)
    {
        if (_worldTiles is null)
            return false;
        if ((uint)tileX >= (uint)_worldTiles.Dimensions.WidthTiles || (uint)tileY >= (uint)_worldTiles.Dimensions.HeightTiles)
            return false;
        int tileLeft = tileX * 16;
        int tileTop = tileY * 16;
        int tileRight = tileLeft + 16;
        int tileBottom = tileTop + 16;
        foreach (RuntimePlayerMember player in _players.Members)
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
        if (_serverPlayers?.IntersectsLivingPlayer(
                tileLeft,
                tileTop,
                tileRight,
                tileBottom,
                PlayerAuthority.VanillaBasePlayerWidth,
                PlayerAuthority.VanillaBasePlayerHeight) == true)
        {
            return false;
        }
        var npcBuffer = new NpcSnapshot[_npcs.Capacity];
        int npcCount = _npcs.CopyActive(npcBuffer);
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
