using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
using TerraRuntime.Gameplay.Players;

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
    private readonly PlayerStateSnapshot[] serverPlayerBuffer = new PlayerStateSnapshot[byte.MaxValue + 1];
    private const byte GhostFlag = 1 << 6;

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
            (float width, float height) = player.HasMount
                ? VanillaPlayerMountHitbox1458.Resolve(player.MountType)
                : (PlayerAuthority.VanillaBasePlayerWidth, PlayerAuthority.VanillaBasePlayerHeight);
            if (player.Slot.Value < byte.MaxValue && !player.IsDead && (player.MovementFlags & GhostFlag) == 0 && Intersects(
                    player.PositionX,
                    player.PositionY,
                    width,
                    height,
                    tileLeft,
                    tileTop,
                    tileRight,
                    tileBottom))
            {
                return false;
            }
        }

        int serverCount = serverPlayers?.CopySnapshots(serverPlayerBuffer) ?? 0;
        for (int index = 0; index < serverCount; index++)
        {
            PlayerStateSnapshot player = serverPlayerBuffer[index];
            if (player.Player.Slot.Value == byte.MaxValue || player.IsDead || (player.MovementFlags & GhostFlag) != 0)
                continue;
            (float width, float height) = player.HasMount
                ? VanillaPlayerMountHitbox1458.Resolve(player.MountType)
                : (PlayerAuthority.VanillaBasePlayerWidth, PlayerAuthority.VanillaBasePlayerHeight);
            if (Intersects(player.PositionX, player.PositionY, width, height, tileLeft, tileTop, tileRight, tileBottom))
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
        if (npc.Simulation.HitboxOverride is NpcHitboxDimensions physical)
            return !physical.IsValid || Intersects(npc.PositionX, npc.PositionY, physical.Width, physical.Height,
                tileLeft, tileTop, tileRight, tileBottom);
        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId type) ||
            !VanillaNpcDefinitionCatalog.TryGet(type, npc.NetIdentity, out VanillaNpcDefinition definition))
        {
            // Unknown physical metadata cannot prove an actor-free tile.
            return true;
        }
        if (!definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox))
            return true;

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
        (int)actorX < tileRight && (int)actorX + actorWidth > tileLeft &&
        (int)actorY < tileBottom && (int)actorY + actorHeight > tileTop;
}
