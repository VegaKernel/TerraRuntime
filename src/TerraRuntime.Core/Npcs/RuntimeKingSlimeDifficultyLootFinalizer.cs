using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public readonly record struct RuntimeKingSlimeDifficultyLootResult(
    NpcHandle Target,
    NpcRevision FinalRevision,
    KingSlimeDifficultyLootExecutionResult Loot)
{
    public bool IsValid => Target.IsAssigned && FinalRevision.IsAssigned && Loot.IsValid;
}

/// <summary>
/// Generation-safe finalization boundary for Expert/Master King Slime loot. Interactions are recorded separately
/// from player liveness, matching NPC.playerInteraction; at death time only currently active slots are projected into
/// the source-ordered delivery loop. Normal mode is intentionally rejected and remains owned by the normal transaction.
/// </summary>
public sealed class RuntimeKingSlimeDifficultyLootFinalizer
{
        private readonly RuntimeNpcStore _store;
    private readonly RuntimeNpcPlayerInteractionLedger _interactions;
    private readonly IRuntimePlayerSlotSnapshotLookup _players;
    private readonly PlayerSlotId[] _interactionSlots =
        new PlayerSlotId[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
    private readonly VanillaKingSlimeLootPlayer[] _activePlayers =
        new VanillaKingSlimeLootPlayer[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];

    public RuntimeKingSlimeDifficultyLootFinalizer(
        RuntimeNpcStore store,
        RuntimeNpcPlayerInteractionLedger interactions,
        IRuntimePlayerSlotSnapshotLookup players)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        _players = players ?? throw new ArgumentNullException(nameof(players));
    }

    public bool TryFinalize(
        NpcHandle target,
        in VanillaKingSlimeDifficultyLootContext context,
        INpcLootRollSource rolls,
        IKingSlimeDifficultyLootDeliverySink sink,
        out RuntimeKingSlimeDifficultyLootResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;

        if (!context.IsValid ||
            !context.IsExpertMode ||
            !_store.TryGet(target, out NpcSnapshot npc) ||
            npc.Simulation.LifeMax <= 0 ||
            npc.Simulation.Life != 0 ||
            npc.TypeIdentity != VanillaNpcIds.KingSlime ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition) ||
            !_interactions.TryCopyInteractingSlots(target, _interactionSlots, out int interactionCount))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = _interactionSlots[index];
            if (!_players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;

            _activePlayers[activeCount++] = new VanillaKingSlimeLootPlayer(
                slot,
                player.PositionX + VanillaPlayerHitboxFacts.BaseWidth * 0.5f,
                player.PositionY + VanillaPlayerHitboxFacts.BaseHeight * 0.5f);
        }

        var npcOrigin = new NpcLootWorldItemOrigin(
            CenterX: (int)npc.PositionX + definition.Width * 0.5f,
            CenterY: (int)npc.PositionY + definition.Height * 0.5f);
        ReadOnlySpan<VanillaKingSlimeLootPlayer> recipients = _activePlayers.AsSpan(0, activeCount);
        if (!VanillaKingSlimeDifficultyLootEvaluator.TryExecute(
                in context,
                in npcOrigin,
                recipients,
                rolls,
                sink,
                out KingSlimeDifficultyLootExecutionResult loot))
        {
            return false;
        }

        if (!_store.TryDespawn(npc.Handle))
        {
            throw new InvalidOperationException(
                "King Slime difficulty loot was delivered but the exact dead NPC generation could not be finalized.");
        }

        _interactions.Forget(npc.Handle);
        result = new RuntimeKingSlimeDifficultyLootResult(npc.Handle, npc.Revision, loot);
        return result.IsValid;
    }
}
