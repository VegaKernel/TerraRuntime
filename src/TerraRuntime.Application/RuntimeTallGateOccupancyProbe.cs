using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Runtime-owned Collision.EmptyTile(ignoreTiles:true) projection used by the authoritative tall-gate path.
/// </summary>
internal sealed class RuntimeTallGateOccupancyProbe : IVanillaTallGateOccupancyProbe
{
    private readonly Func<int, int, bool>? overrideProbe;
    private readonly PlayerAuthority? players;
    private readonly ServerPlayerAuthority? serverPlayers;
    private readonly RuntimeNpcStore? npcs;
    private readonly NpcSnapshot[] npcBuffer;

    public RuntimeTallGateOccupancyProbe(Func<int, int, bool> isFree)
    {
        overrideProbe = isFree ?? throw new ArgumentNullException(nameof(isFree));
        npcBuffer = [];
    }

    public RuntimeTallGateOccupancyProbe(
        PlayerAuthority players,
        ServerPlayerAuthority? serverPlayers,
        RuntimeNpcStore npcs)
    {
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.serverPlayers = serverPlayers;
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        npcBuffer = new NpcSnapshot[npcs.Capacity];
    }

    public bool IsActorFree(int tileX, int tileY)
    {
        if (overrideProbe is not null)
            return overrideProbe(tileX, tileY);
        if (players is null || npcs is null)
            return false;

        int tileLeft = checked(tileX * 16);
        int tileTop = checked(tileY * 16);
        int tileRight = checked(tileLeft + 16);
        int tileBottom = checked(tileTop + 16);
        foreach (RuntimePlayerMember player in players.Members)
        {
            if (!player.IsDead && Intersects(
                    player.PositionX,
                    player.PositionY,
                    PlayerAuthority.VanillaBasePlayerWidth,
                    PlayerAuthority.VanillaBasePlayerHeight,
                    tileLeft,
                    tileTop,
                    tileRight,
                    tileBottom))
            {
                return false;
            }
        }

        if (serverPlayers?.IntersectsLivingPlayer(
                tileLeft,
                tileTop,
                tileRight,
                tileBottom,
                PlayerAuthority.VanillaBasePlayerWidth,
                PlayerAuthority.VanillaBasePlayerHeight) == true)
        {
            return false;
        }

        int npcCount = npcs.CopyActive(npcBuffer);
        for (int i = 0; i < npcCount; i++)
        {
            if (IntersectsNpc(in npcBuffer[i], tileLeft, tileTop, tileRight, tileBottom))
                return false;
        }

        return true;
    }

    private static bool IntersectsNpc(
        in NpcSnapshot npc,
        int tileLeft,
        int tileTop,
        int tileRight,
        int tileBottom)
    {
        if (!npc.IsActive)
            return false;
        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId type) ||
            !VanillaNpcDefinitionCatalog.TryGet(type, npc.NetIdentity, out VanillaNpcDefinition definition))
        {
            return Intersects(npc.PositionX, npc.PositionY, 16f, 16f, tileLeft, tileTop, tileRight, tileBottom);
        }
        if (!definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
            return false;

        return Intersects(
            npc.PositionX,
            npc.PositionY,
            hitbox.Width,
            hitbox.Height,
            tileLeft,
            tileTop,
            tileRight,
            tileBottom);
    }

    private static bool Intersects(
        float actorX,
        float actorY,
        float actorWidth,
        float actorHeight,
        int tileLeft,
        int tileTop,
        int tileRight,
        int tileBottom) =>
        actorX < tileRight && actorX + actorWidth > tileLeft &&
        actorY < tileBottom && actorY + actorHeight > tileTop;
}
