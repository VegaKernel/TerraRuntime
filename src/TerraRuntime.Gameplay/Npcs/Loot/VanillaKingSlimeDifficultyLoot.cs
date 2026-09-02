using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Difficulty inputs that TerrariaServer 1.4.5.8 uses for King Slime's non-normal loot wrappers.
/// Master is a strict subset of Expert in the source world-mode model.
/// </summary>
public readonly record struct VanillaKingSlimeDifficultyLootContext(
    bool IsExpertMode,
    bool IsMasterMode)
{
    public bool IsValid => !IsMasterMode || IsExpertMode;
}

/// <summary>
/// One currently active player slot that previously interacted with the dying NPC. The center is captured before
/// loot execution because MasterModeDropOnAllPlayers places its ordinary world item on each qualifying player.
/// </summary>
public readonly record struct VanillaKingSlimeLootPlayer(
    PlayerSlotId Slot,
    float CenterX,
    float CenterY)
{
    public bool IsValid =>
        Slot.Value < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots &&
        float.IsFinite(CenterX) &&
        float.IsFinite(CenterY);

    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

/// <summary>
/// Delivery boundary for the three source-distinct King Slime difficulty paths. CanDeliver methods must be
/// side-effect free. After a successful preflight, a TryDeliver failure is an invariant violation: the evaluator
/// deliberately materializes each successful item inline so Item.NewItem RNG remains interleaved with later loot RNG.
/// </summary>
public interface IKingSlimeDifficultyLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);

    bool CanDeliverWorldItem(ItemTypeId itemType);

    bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaKingSlimeLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random);

    bool TryDeliverWorldItem(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random);
}

public readonly record struct KingSlimeDifficultyLootExecutionResult(
    int InstancedItemCount,
    int InstancedRecipientCount,
    int WorldItemCount,
    int MasterPetDropCount)
{
    public int TotalLogicalItemCount => checked(InstancedItemCount + WorldItemCount);

    public bool IsValid =>
        InstancedItemCount >= 0 &&
        InstancedRecipientCount >= 0 &&
        WorldItemCount >= 0 &&
        MasterPetDropCount >= 0 &&
        MasterPetDropCount <= WorldItemCount;
}

/// <summary>
/// Exact source-order evaluator for the King Slime difficulty rules registered after the normal-only block.
///
/// TerrariaServer 1.4.5.8 registration order is BossBag(3318), normal-only rules, MasterModeCommonDrop(4929), then
/// MasterModeDropOnAllPlayers(4797, 4). BossBag uses raw Next(1), one stack Next(1,2), creates one no-broadcast item,
/// sends packet 90 to every active interacting player and keeps that item slot unavailable for 54000 ticks after the
/// server turns the item to air. The Master relic uses CommonDropNotScalingWithLuck. The Master pet chooses its stack
/// once, then performs raw Next(4) in ascending player-slot order and immediately materializes each successful drop at
/// that player's center before rolling the next player.
/// </summary>
public static class VanillaKingSlimeDifficultyLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterPetChanceDenominator = 4;

    public static bool TryExecute(
        in VanillaKingSlimeDifficultyLootContext context,
        in NpcLootWorldItemOrigin npcOrigin,
        ReadOnlySpan<VanillaKingSlimeLootPlayer> activeInteractingPlayers,
        INpcLootRollSource rolls,
        IKingSlimeDifficultyLootDeliverySink sink,
        out KingSlimeDifficultyLootExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;

        if (!context.IsValid ||
            !context.IsExpertMode ||
            !npcOrigin.IsValid ||
            !ArePlayersSourceOrdered(activeInteractingPlayers) ||
            !sink.CanDeliverInstanced(VanillaKingSlimeItemIds.KingSlimeBossBag))
        {
            return false;
        }

        if (context.IsMasterMode &&
            (!sink.CanDeliverWorldItem(VanillaKingSlimeItemIds.KingSlimeMasterTrophy) ||
             !sink.CanDeliverWorldItem(VanillaKingSlimeItemIds.KingSlimePetItem)))
        {
            return false;
        }

        // DropLocalPerClientAndResetsNPCMoneyTo0 inherits CommonDrop's chance 1/1, but its implementation uses raw RNG.
        rolls.NextInt32(0, 1);
        short bagStack = checked((short)rolls.NextInt32(1, 2));
        var bag = new NpcLootDrop(VanillaKingSlimeItemIds.KingSlimeBossBag, bagStack);
        if (!sink.TryDeliverInstanced(
                in npcOrigin,
                in bag,
                activeInteractingPlayers,
                InstancedItemSlotLeaseTicks,
                rolls))
        {
            throw new InvalidOperationException(
                "King Slime loot sink advertised instanced-bag support but failed after preflight.");
        }

        int worldItems = 0;
        int petDrops = 0;
        if (context.IsMasterMode)
        {
            // CommonDropNotScalingWithLuck(4929, 1, 1, 1, 1): raw chance RNG, stack RNG, then Item.NewItem.
            rolls.NextInt32(0, 1);
            short relicStack = checked((short)rolls.NextInt32(1, 2));
            var relic = new NpcLootDrop(VanillaKingSlimeItemIds.KingSlimeMasterTrophy, relicStack);
            if (!sink.TryDeliverWorldItem(in npcOrigin, in relic, rolls))
            {
                throw new InvalidOperationException(
                    "King Slime loot sink advertised Master relic support but failed after preflight.");
            }
            worldItems++;

            // DropPerPlayerOnThePlayer chooses one shared stack before the player loop, then rolls raw Next(4)
            // separately for every active interacting slot. Successful Item.NewItem calls occur inside that loop.
            short petStack = checked((short)rolls.NextInt32(1, 2));
            for (int index = 0; index < activeInteractingPlayers.Length; index++)
            {
                if (rolls.NextInt32(0, MasterPetChanceDenominator) != 0)
                    continue;

                VanillaKingSlimeLootPlayer player = activeInteractingPlayers[index];
                NpcLootWorldItemOrigin playerOrigin = player.Origin;
                var pet = new NpcLootDrop(VanillaKingSlimeItemIds.KingSlimePetItem, petStack);
                if (!sink.TryDeliverWorldItem(in playerOrigin, in pet, rolls))
                {
                    throw new InvalidOperationException(
                        "King Slime loot sink advertised Master pet support but failed after preflight.");
                }
                worldItems++;
                petDrops++;
            }
        }

        result = new KingSlimeDifficultyLootExecutionResult(
            InstancedItemCount: 1,
            InstancedRecipientCount: activeInteractingPlayers.Length,
            WorldItemCount: worldItems,
            MasterPetDropCount: petDrops);
        return result.IsValid;
    }

    private static bool ArePlayersSourceOrdered(ReadOnlySpan<VanillaKingSlimeLootPlayer> players)
    {
        int previousSlot = -1;
        for (int index = 0; index < players.Length; index++)
        {
            VanillaKingSlimeLootPlayer player = players[index];
            if (!player.IsValid || player.Slot.Value <= previousSlot)
                return false;
            previousSlot = player.Slot.Value;
        }
        return true;
    }
}
