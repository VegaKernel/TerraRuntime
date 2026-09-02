using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaDeerclopsLootContext(bool IsExpertMode, bool IsMasterMode)
{
    public bool IsValid => !IsMasterMode || IsExpertMode;
}

public readonly record struct VanillaDeerclopsLootPlayer(PlayerSlotId Slot, float CenterX, float CenterY)
{
    public bool IsValid =>
        Slot.Value < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots &&
        float.IsFinite(CenterX) &&
        float.IsFinite(CenterY);

    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

public interface IDeerclopsLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);
    bool CanDeliverWorldItem(ItemTypeId itemType);

    bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaDeerclopsLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random);

    bool TryDeliverWorldItem(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random);
}

public readonly record struct DeerclopsLootExecutionResult(
    int WorldItemCount,
    int InstancedItemCount,
    int InstancedRecipientCount,
    int MasterPetDropCount)
{
    public bool IsValid =>
        WorldItemCount >= 0 &&
        InstancedItemCount >= 0 &&
        InstancedRecipientCount >= 0 &&
        MasterPetDropCount >= 0 &&
        MasterPetDropCount <= WorldItemCount;
}

/// <summary>
/// Source-order implementation of TerrariaServer 1.4.5.8 ItemDropDatabase.RegisterBoss_Deerclops plus the later
/// one-in-ten Deerclops trophy registration. The guaranteed Classic weapon rule preserves the nested
/// OneFromRulesRule -> OneFromOptionsNotScaledWithLuck RNG call order instead of flattening it to a single pick.
/// </summary>
public static class VanillaDeerclopsLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterPetChanceDenominator = 4;

    private static readonly ItemTypeId[] ClassicWeaponOptions =
    [
        VanillaDeerclopsItemIds.PewMaticHorn,
        VanillaDeerclopsItemIds.WeatherPain,
        VanillaDeerclopsItemIds.HoundiusShootius,
        VanillaDeerclopsItemIds.LucyTheAxe
    ];

    public static bool TryExecute(
        in VanillaDeerclopsLootContext context,
        in NpcLootWorldItemOrigin npcOrigin,
        ReadOnlySpan<VanillaDeerclopsLootPlayer> activeInteractingPlayers,
        INpcLootRollSource rolls,
        IDeerclopsLootDeliverySink sink,
        out DeerclopsLootExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;

        if (!context.IsValid ||
            !npcOrigin.IsValid ||
            !ArePlayersSourceOrdered(activeInteractingPlayers) ||
            !CanDeliverAll(in context, sink))
        {
            return false;
        }

        int worldItems = 0;
        int instancedItems = 0;
        int recipients = 0;
        int petDrops = 0;

        if (context.IsExpertMode)
        {
            // BossBag(): the guaranteed local-drop rule consumes its CommonDrop chance and stack calls before Item.NewItem.
            rolls.NextInt32(0, 1);
            var bag = new NpcLootDrop(
                VanillaDeerclopsItemIds.DeerclopsBossBag,
                checked((short)rolls.NextInt32(1, 2)));
            if (!sink.TryDeliverInstanced(
                    in npcOrigin,
                    in bag,
                    activeInteractingPlayers,
                    InstancedItemSlotLeaseTicks,
                    rolls))
            {
                throw new InvalidOperationException("Deerclops loot sink failed advertised Boss Bag delivery.");
            }

            instancedItems = 1;
            recipients = activeInteractingPlayers.Length;
        }

        if (context.IsMasterMode)
        {
            DropGuaranteed(
                VanillaDeerclopsItemIds.DeerclopsMasterTrophy,
                in npcOrigin,
                rolls,
                sink,
                ref worldItems);

            // DropPerPlayerOnThePlayer computes one stack before iterating interacting players, then performs each
            // player's independent 1/4 roll in source player-slot order.
            short petStack = checked((short)rolls.NextInt32(1, 2));
            for (int index = 0; index < activeInteractingPlayers.Length; index++)
            {
                if (rolls.NextInt32(0, MasterPetChanceDenominator) != 0)
                    continue;

                VanillaDeerclopsLootPlayer player = activeInteractingPlayers[index];
                NpcLootWorldItemOrigin playerOrigin = player.Origin;
                var pet = new NpcLootDrop(VanillaDeerclopsItemIds.DeerclopsPetItem, petStack);
                if (!sink.TryDeliverWorldItem(in playerOrigin, in pet, rolls))
                    throw new InvalidOperationException("Deerclops loot sink failed advertised Master pet delivery.");

                worldItems++;
                petDrops++;
            }
        }
        else if (!context.IsExpertMode)
        {
            RollClassic(VanillaDeerclopsItemIds.DeerclopsMask, 7, in npcOrigin, rolls, sink, ref worldItems);
            RollClassic(VanillaDeerclopsItemIds.ChesterPetItem, 3, in npcOrigin, rolls, sink, ref worldItems);
            RollClassic(VanillaDeerclopsItemIds.Eyebrella, 3, in npcOrigin, rolls, sink, ref worldItems);
            RollClassic(VanillaDeerclopsItemIds.DontStarveShaderItem, 3, in npcOrigin, rolls, sink, ref worldItems);
            RollClassic(VanillaDeerclopsItemIds.DizzyHat, 14, in npcOrigin, rolls, sink, ref worldItems);

            // OneFromRulesRule(1, OneFromOptionsNotScalingWithLuck(1, ...)) has two explicit guaranteed
            // Next(1) calls before selecting the option. Preserve both to keep the shared Main.rand stream aligned.
            rolls.NextInt32(0, 1);
            rolls.NextInt32(0, 1);
            ItemTypeId selected = ClassicWeaponOptions[rolls.NextInt32(0, ClassicWeaponOptions.Length)];
            DropGuaranteed(selected, in npcOrigin, rolls, sink, ref worldItems);
        }

        RollClassic(VanillaDeerclopsItemIds.DeerclopsTrophy, 10, in npcOrigin, rolls, sink, ref worldItems);

        result = new DeerclopsLootExecutionResult(worldItems, instancedItems, recipients, petDrops);
        return result.IsValid;
    }

    private static bool CanDeliverAll(in VanillaDeerclopsLootContext context, IDeerclopsLootDeliverySink sink)
    {
        if (context.IsExpertMode && !sink.CanDeliverInstanced(VanillaDeerclopsItemIds.DeerclopsBossBag))
            return false;

        if (context.IsMasterMode &&
            (!sink.CanDeliverWorldItem(VanillaDeerclopsItemIds.DeerclopsMasterTrophy) ||
             !sink.CanDeliverWorldItem(VanillaDeerclopsItemIds.DeerclopsPetItem)))
        {
            return false;
        }

        if (!sink.CanDeliverWorldItem(VanillaDeerclopsItemIds.DeerclopsTrophy))
            return false;

        if (!context.IsExpertMode)
        {
            ReadOnlySpan<ItemTypeId> classic =
            [
                VanillaDeerclopsItemIds.DeerclopsMask,
                VanillaDeerclopsItemIds.ChesterPetItem,
                VanillaDeerclopsItemIds.Eyebrella,
                VanillaDeerclopsItemIds.DontStarveShaderItem,
                VanillaDeerclopsItemIds.DizzyHat,
                VanillaDeerclopsItemIds.PewMaticHorn,
                VanillaDeerclopsItemIds.WeatherPain,
                VanillaDeerclopsItemIds.HoundiusShootius,
                VanillaDeerclopsItemIds.LucyTheAxe
            ];
            for (int index = 0; index < classic.Length; index++)
            {
                if (!sink.CanDeliverWorldItem(classic[index]))
                    return false;
            }
        }

        return true;
    }

    private static void RollClassic(
        ItemTypeId item,
        int denominator,
        in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls,
        IDeerclopsLootDeliverySink sink,
        ref int count)
    {
        if (rolls.RollLuck(denominator) != 0)
            return;

        DropGuaranteed(item, in origin, rolls, sink, ref count);
    }

    private static void DropGuaranteed(
        ItemTypeId item,
        in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls,
        IDeerclopsLootDeliverySink sink,
        ref int count)
    {
        var drop = new NpcLootDrop(item, checked((short)rolls.NextInt32(1, 2)));
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
            throw new InvalidOperationException($"Deerclops loot sink failed advertised world-item support for {item.Value}.");
        count++;
    }

    private static bool ArePlayersSourceOrdered(ReadOnlySpan<VanillaDeerclopsLootPlayer> players)
    {
        int previous = -1;
        for (int index = 0; index < players.Length; index++)
        {
            VanillaDeerclopsLootPlayer player = players[index];
            if (!player.IsValid || player.Slot.Value <= previous)
                return false;
            previous = player.Slot.Value;
        }
        return true;
    }
}
