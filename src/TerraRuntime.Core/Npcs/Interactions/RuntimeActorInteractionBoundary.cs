using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Authoritative generation, availability and source-backed vanilla reach validation for NPC interactions.
/// Policy and response generation remain separate boundaries.
/// </summary>
public sealed class RuntimeActorInteractionBoundary
{
    private const int PlayerWidth = 20;
    private const int PlayerHeight = 42;
    private const int TileSize = 16;
    private const int DefaultTileRangeX = 5;
    private const int DefaultTileRangeY = 3;

    private readonly RuntimeNpcStore npcs;
    private readonly IRuntimePlayerSnapshotLookup players;

    public RuntimeActorInteractionBoundary(
        RuntimeNpcStore npcs,
        IRuntimePlayerSnapshotLookup players)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        ArgumentNullException.ThrowIfNull(players);
        this.npcs = npcs;
        this.players = players;
    }

    public ActorInteractionValidationResult TryValidate(
        in ActorInteractionRequest request,
        out ActorInteractionAcceptance acceptance)
    {
        acceptance = default;
        if (!request.Player.IsAssigned ||
            !request.Target.IsAssigned ||
            request.Kind is not ActorInteractionKind.NpcConversation and not ActorInteractionKind.NpcShopOpen)
        {
            return ActorInteractionValidationResult.InvalidRequest;
        }

        if (!players.TryGetPlayer(request.Player, out PlayerStateSnapshot player))
            return ActorInteractionValidationResult.InvalidPlayer;
        if (player.IsDead || player.HasHealth && player.Life <= 0)
            return ActorInteractionValidationResult.PlayerUnavailable;

        if (!npcs.TryGet(request.Target, out NpcSnapshot npc))
            return ActorInteractionValidationResult.InvalidTarget;
        if (npc.Simulation.TimeLeft == 0 ||
            npc.Simulation.LifeMax > 0 && npc.Simulation.Life <= 0)
        {
            return ActorInteractionValidationResult.TargetUnavailable;
        }

        if (!VanillaNpcDefinitionCatalog.TryGet(npc.Type, out VanillaNpcDefinition definition))
            return ActorInteractionValidationResult.UnsupportedTargetType;
        if (!IsInSimpleInteractionRange(in player, in npc, in definition))
            return ActorInteractionValidationResult.OutOfRange;

        acceptance = new ActorInteractionAcceptance(request, player.Revision, npc.Revision);
        return ActorInteractionValidationResult.Accepted;
    }

    private static bool IsInSimpleInteractionRange(
        in PlayerStateSnapshot player,
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition)
    {
        int playerTileLeft = (int)(player.PositionX / TileSize);
        int playerTileRight = checked((int)Math.Ceiling((player.PositionX + PlayerWidth) / TileSize) - 1);
        int playerTileTop = (int)(player.PositionY / TileSize);
        int playerTileBottom = checked((int)Math.Ceiling((player.PositionY + PlayerHeight) / TileSize) - 1);

        int reachLeft = checked((playerTileLeft - DefaultTileRangeX) * TileSize);
        int reachRight = checked((playerTileRight + DefaultTileRangeX) * TileSize + (TileSize - 1));
        int reachTop = checked((playerTileTop - DefaultTileRangeY) * TileSize);
        int reachBottom = checked((playerTileBottom + DefaultTileRangeY) * TileSize + (TileSize - 1));

        int npcLeft = (int)npc.PositionX;
        int npcRight = checked(npcLeft + definition.Width);
        int npcTop = (int)npc.PositionY;
        int npcBottom = checked(npcTop + definition.Height);
        return reachLeft < npcRight &&
               npcLeft < reachRight &&
               reachTop < npcBottom &&
               npcTop < reachBottom;
    }
}
