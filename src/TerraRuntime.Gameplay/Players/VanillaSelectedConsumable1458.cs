using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Buffs;

namespace TerraRuntime.Gameplay.Players;

public readonly record struct PlayerSelectedConsumableState1458(int ItemTime, int ItemTimeMax,
    int Animation, int AnimationMax, bool ReleaseUseItem, int PotionDelay, int RevolverCritBonus);

public readonly record struct PlayerSelectedConsumableFacts1458(ItemTypeId Item, int Life,
    int LifeMaximum, int Mana, int ManaMaximum, bool ControlUseItem, bool LastUseSuccess,
    bool Cursed, bool CrowdControlled, bool AllowedToHoldItems, bool SelectionBuffered);

public readonly record struct PlayerSelectedConsumableTransition1458(
    PlayerSelectedConsumableState1458 State, int Life, int Mana,
    bool BeganUse, int PotionSicknessOffer, int ManaSicknessOffer)
{
    public BuffTypeId BuffOfferType { get; init; }
    public int BuffDurationOffer { get; init; }
    public bool Consumed { get; init; }
    public int RemainingStack { get; init; }
    public bool PendingItemReuse { get; init; }
    public bool ResetItemRotation { get; init; }
    // ItemCheck_OwnerOnlyCode samples itemTime before potion/buff/permanent
    // effects and before last-stack consumption replaces it with itemAnimation.
    public int? OwnerToolTime { get; init; }
}

public readonly record struct PlayerPermanentConsumableTransition1458(
    PlayerSelectedConsumableTransition1458 Use, int BaseLifeMaximum, int DerivedLifeMaximum,
    int BaseManaMaximum, int DerivedManaMaximum, bool Applied);

/// <summary>Detached personal permanent flags, aligned with packet-4 unlock bits;
/// Demon Heart occupies bit7 here and remains a difficulty flag in the caller.</summary>
public readonly record struct PlayerPermanentUnlockState1458(byte Flags)
{
    public const byte AegisCrystalFlag = 1 << 0;
    public const byte AegisFruitFlag = 1 << 1;
    public const byte ArcaneCrystalFlag = 1 << 2;
    public const byte GalaxyPearlFlag = 1 << 3;
    public const byte GummyWormFlag = 1 << 4;
    public const byte AmbrosiaFlag = 1 << 5;
    public const byte ArtisanLoafFlag = 1 << 6;
    public const byte DemonHeartFlag = 1 << 7;

    public bool AegisCrystal => (Flags & AegisCrystalFlag) != 0;
    public bool AegisFruit => (Flags & AegisFruitFlag) != 0;
    public bool ArcaneCrystal => (Flags & ArcaneCrystalFlag) != 0;
    public bool GalaxyPearl => (Flags & GalaxyPearlFlag) != 0;
    public bool GummyWorm => (Flags & GummyWormFlag) != 0;
    public bool Ambrosia => (Flags & AmbrosiaFlag) != 0;
    public bool ArtisanLoaf => (Flags & ArtisanLoafFlag) != 0;
    public bool DemonHeart => (Flags & DemonHeartFlag) != 0;
}

public readonly record struct PlayerPermanentUnlockTransition1458(
    PlayerSelectedConsumableTransition1458 Use, PlayerPermanentUnlockState1458 Unlocks,
    bool Applied, byte FamilyBit)
{
    public ItemTypeId SelectedItem { get; init; }
}

/// <summary>One detached Torch God favor use. The caller owns payment, appearance
/// and the once-only scripted bestiary credit; this value never publishes.</summary>
public readonly record struct PlayerTorchGodFavorTransition1458(
    PlayerSelectedConsumableTransition1458 Use, bool Unlocked, bool UsingBiomeTorches, bool Applied)
{
    public ItemTypeId SelectedItem { get; init; }
}

/// <summary>Inputs after equipment and before source UpdatePermanentBoosters.</summary>
public readonly record struct PlayerPermanentBoosterState1458(
    int Defense, int FishingSkill, float PickSpeed, float TileSpeed, float WallSpeed);

public readonly record struct PlayerPermanentSpeedState1458(
    float PickSpeed, float TileAnimationMultiplier, float WallAnimationMultiplier);

public readonly record struct PlayerCraftingRegion1458(int Left, int Top, int Right, int Bottom);

public readonly record struct QuickBuffEligibility1458(bool PendingInventoryActions, bool Cursed,
    bool CrowdControlled, bool Dead, bool Spectating, bool NoItems);

public readonly record struct QuickBuffMutation1458(int SourceSlot, WorldItemPickupSlot1458 Before,
    WorldItemPickupSlot1458 After, BuffTypeId OfferType, int OfferDuration);

public sealed record QuickBuffPlan1458(WorldItemPickupSlot1458[] Inventory,
    WorldItemPickupSlot1458[] VoidItems, BuffTypeId[] BuffTypes, int[] BuffTimes,
    QuickBuffMutation1458[] Mutations);

/// <summary>Selected ordinary remote ItemCheck. Offers do not debit inventory or publish client reports.</summary>
public static class VanillaSelectedConsumable1458
{
    public const float ConstructorHeat = 0f;
    public static PlayerSelectedConsumableState1458 ConstructorState => default;

    public static bool TryStep(PlayerSelectedConsumableState1458? previous,
        in PlayerSelectedConsumableFacts1458 facts, Func<int, int, int> nextInteger,
        Func<double> nextDouble, out PlayerSelectedConsumableTransition1458 transition)
        => TryStepCore(previous, in facts, nextInteger, nextDouble, localEffects: false, out transition);

    private static bool TryStepCore(PlayerSelectedConsumableState1458? previous,
        in PlayerSelectedConsumableFacts1458 facts, Func<int, int, int> nextInteger,
        Func<double> nextDouble, bool localEffects, out PlayerSelectedConsumableTransition1458 transition)
    {
        transition = default;
        if (previous is not { } state ||
            !VanillaSelectedConsumableCatalog1458.TryGet(facts.Item, out var item) ||
            state.ItemTime is < 0 or > short.MaxValue || state.ItemTimeMax is < 0 or > short.MaxValue ||
            state.Animation is < 0 or > short.MaxValue || state.AnimationMax is < 0 or > short.MaxValue ||
            state.PotionDelay is < 0 or > 3_600 || state.RevolverCritBonus < int.MinValue + 2 ||
            facts.LifeMaximum is < 20 or > VanillaRemotePlayerHealth1458.MaximumSupportedLifeMax ||
            facts.Life is < 0 or > VanillaRemotePlayerHealth1458.MaximumSupportedLifeMax ||
            facts.ManaMaximum is < 0 or > 400 || facts.Mana is < 0 or > 400 ||
            nextInteger is null || nextDouble is null) return false;

        if (facts.CrowdControlled)
        {
            transition = new(state with { Animation = 0, AnimationMax = 0 },
                facts.Life, facts.Mana, false, 0, 0);
            return true;
        }
        int crit = state.RevolverCritBonus;
        if (nextInteger(0, 3) == 0) crit -= 2;
        bool resetItemRotation = item.UseAnimation > 0 && facts.ControlUseItem && state.ReleaseUseItem &&
            state.Animation == 0 && !facts.SelectionBuffered;
        bool begin = resetItemRotation && facts.LastUseSuccess && !facts.Cursed &&
            (!item.HealingDelay || state.PotionDelay == 0);
        int animation = begin ? item.UseAnimation : state.Animation;
        int animationMax = begin ? item.UseAnimation : state.Animation == 0 ? 0 : state.AnimationMax;
        int delay = begin && item.HealingDelay ? 3_600 : state.PotionDelay;
        bool pending = animation > 0 && animation == 1 && facts.ControlUseItem && state.ReleaseUseItem;
        if (animation > 0) animation--;
        int time = Math.Max(0, state.ItemTime - 1);
        int timeMax = state.ItemTimeMax;
        int life = facts.Life;
        int mana = facts.Mana;
        int manaSickness = 0;
        int buffOffer = 0;
        int buffDuration = 0;
        int? ownerToolTime = facts.AllowedToHoldItems ? time : null;
        if (facts.AllowedToHoldItems)
        {
            if (animation > 0 && item.FoodColourCount > 0 && nextInteger(0, 2) != 0)
            {
                nextDouble();
                nextDouble();
                nextDouble();
                nextInteger(0, item.FoodColourCount);
                nextDouble();
            }
            if (animation > 0 && item.DrinkColourCount > 0)
            {
                // Source drink positions, rotation, colour and scale survive the dedicated visual sink.
                nextDouble();
                nextDouble();
                nextDouble();
                nextInteger(0, item.DrinkColourCount);
                nextDouble();
            }
            if (time == 0 && animation > 0 && (item.HealLife > 0 || item.HealMana > 0))
            {
                life = Math.Min(facts.LifeMaximum, life + item.HealLife);
                mana = Math.Min(facts.ManaMaximum, mana + item.HealMana);
                manaSickness = item.HealMana > 0 ? 300 : 0;
                time = timeMax = item.UseTime;
            }
            if (time == 0 && animation > 0 && item.BuffType > 0)
            {
                if (localEffects)
                {
                    buffOffer = item.BuffType;
                    buffDuration = item.BuffTime;
                }
                time = timeMax = item.UseTime;
            }
        }
        transition = new(new(time, timeMax, animation, animationMax, !facts.ControlUseItem,
            delay, crit), life, mana, begin, begin && item.HealingDelay ? delay : 0, manaSickness)
        {
            BuffOfferType = new BuffTypeId((short)buffOffer),
            BuffDurationOffer = buffDuration,
            OwnerToolTime = ownerToolTime,
            PendingItemReuse = pending,
            ResetItemRotation = resetItemRotation
        };
        return true;
    }

    /// <summary>Local ordinary potion ItemCheck, including its post-effect inventory debit.
    /// A zero stack retains the source item identity until source item cleanup or inventory synchronization.</summary>
    public static bool TryStepOwned(PlayerSelectedConsumableState1458? previous,
        in PlayerSelectedConsumableFacts1458 facts, int selectedStack,
        Func<int, int, int> nextInteger, Func<double> nextDouble,
        out PlayerSelectedConsumableTransition1458 transition)
    {
        transition = default;
        if (!VanillaSelectedConsumableCatalog1458.TryGet(facts.Item, out var item) ||
            item.HealLife == 0 && item.HealMana == 0 && item.BuffType == 0 ||
            !VanillaWorldItemAllocationCatalog1458.TryGet(facts.Item.Value, out var allocation) ||
            selectedStack < 0 || selectedStack > allocation.MaximumStack) return false;

        // Local ItemCheck recomputes the ordinary use result; the retained success bit gates only remote players.
        var localFacts = facts with { LastUseSuccess = true };
        if (!TryStepCore(previous, in localFacts, nextInteger, nextDouble, localEffects: true, out transition)) return false;

        return CompleteOrdinaryConsumption(in facts, selectedStack, in transition, out transition);
    }

    /// <summary>Source selected food ItemCheck with distinct raw cursed and effective noItems inputs.
    /// The explicit local flag owns food offers/debit; remote calls retain clocks and visual RNG only.</summary>
    public static bool TryStepFood(PlayerSelectedConsumableState1458? previous,
        in PlayerSelectedConsumableFacts1458 facts, int selectedStack, bool localEffects, bool noItems,
        Func<int, int, int> nextInteger, Func<double> nextDouble,
        out PlayerSelectedConsumableTransition1458 transition)
    {
        transition = default;
        if (!VanillaSelectedConsumableCatalog1458.TryGet(facts.Item, out var item) ||
            FoodPriority(new BuffTypeId((short)item.BuffType)) == 0 ||
            !VanillaWorldItemAllocationCatalog1458.TryGet(facts.Item.Value, out var allocation) ||
            selectedStack < 0 || selectedStack > allocation.MaximumStack) return false;
        var useFacts = facts with { Cursed = noItems,
            LastUseSuccess = localEffects || facts.LastUseSuccess };
        if (!TryStepCore(previous, in useFacts, nextInteger, nextDouble, localEffects, out transition)) return false;
        if (localEffects)
            return CompleteOrdinaryConsumption(in facts, selectedStack, in transition, out transition);
        transition = transition with { RemainingStack = selectedStack };
        return true;
    }

    private static bool CompleteOrdinaryConsumption(in PlayerSelectedConsumableFacts1458 facts,
        int selectedStack, in PlayerSelectedConsumableTransition1458 planned,
        out PlayerSelectedConsumableTransition1458 transition)
    {
        transition = planned;
        // The source consumes after the effect and ApplyItemTime, rather than at the start gate.
        bool consumptionPoint = facts.AllowedToHoldItems && !facts.CrowdControlled &&
            transition.State.ItemTimeMax != 0 &&
            transition.State.ItemTime == transition.State.ItemTimeMax;
        bool consumed = consumptionPoint && selectedStack > 0;
        int remaining = selectedStack - (consumed ? 1 : 0);
        var state = transition.State;
        if (consumptionPoint && remaining == 0)
            state = state with { ItemTime = state.Animation };
        transition = transition with { State = state, Consumed = consumed, RemainingStack = remaining };
        return true;
    }

    public static byte GetPermanentUnlockFamilyBit(ItemTypeId item) => item.Value switch
    {
        5337 => PlayerPermanentUnlockState1458.AegisCrystalFlag,
        5338 => PlayerPermanentUnlockState1458.AegisFruitFlag,
        5339 => PlayerPermanentUnlockState1458.ArcaneCrystalFlag,
        5340 => PlayerPermanentUnlockState1458.GalaxyPearlFlag,
        5341 => PlayerPermanentUnlockState1458.GummyWormFlag,
        5342 => PlayerPermanentUnlockState1458.AmbrosiaFlag,
        5326 => PlayerPermanentUnlockState1458.ArtisanLoafFlag,
        3335 => PlayerPermanentUnlockState1458.DemonHeartFlag,
        _ => 0
    };

    /// <summary>Personal permanent ItemCheck. Only the explicit local branch invokes
    /// the original owner-only effects and consumes; flags never grant funding or publish.</summary>
    public static bool TryStepPermanentUnlock(PlayerSelectedConsumableState1458? previous,
        in PlayerSelectedConsumableFacts1458 facts, in PlayerPermanentUnlockState1458 unlocks,
        int selectedStack, bool localEffects, bool noItems, bool expert,
        Func<int, int, int> nextInteger, Func<double> nextDouble,
        out PlayerPermanentUnlockTransition1458 transition)
    {
        transition = default;
        byte family = GetPermanentUnlockFamilyBit(facts.Item);
        if (facts.Item == VanillaItemIds.None)
        {
            if (selectedStack != 0) return false;
        }
        else if (family == 0 || !VanillaWorldItemAllocationCatalog1458.TryGet(facts.Item.Value, out var allocation) ||
            selectedStack < 0 || selectedStack > allocation.MaximumStack)
            return false;

        bool heartAllowed = facts.Item.Value != 3335 || expert && !unlocks.DemonHeart;
        var useFacts = facts with
        {
            Cursed = noItems,
            LastUseSuccess = heartAllowed && (localEffects || facts.LastUseSuccess)
        };
        if (!TryStepCore(previous, in useFacts, nextInteger, nextDouble, localEffects: false, out var use))
            return false;

        bool applied = family != 0 && localEffects && heartAllowed && facts.AllowedToHoldItems &&
            use.State.Animation > 0 && use.State.ItemTime == 0 && (unlocks.Flags & family) == 0;
        var nextUnlocks = unlocks;
        if (applied)
        {
            nextUnlocks = new((byte)(unlocks.Flags | family));
            VanillaSelectedConsumableCatalog1458.TryGet(facts.Item, out var item);
            use = use with { State = use.State with { ItemTime = item.UseTime, ItemTimeMax = item.UseTime } };
        }
        bool consumptionPoint = localEffects && facts.AllowedToHoldItems && !facts.CrowdControlled &&
            use.State.ItemTimeMax != 0 && use.State.ItemTime == use.State.ItemTimeMax;
        bool consumed = consumptionPoint && selectedStack > 0;
        int remaining = selectedStack - (consumed ? 1 : 0);
        var state = use.State;
        if (consumptionPoint && remaining == 0)
            state = state with { ItemTime = state.Animation };
        use = use with { State = state, Consumed = consumed, RemainingStack = remaining };
        transition = new(use, nextUnlocks, applied, family)
        {
            SelectedItem = remaining == 0 && state.Animation == 0 ? VanillaItemIds.None : facts.Item
        };
        return true;
    }

    /// <summary>Terraria 1.4.5.8 ItemCheck_UseTorchGodsFavor followed by the
    /// ordinary consumable debit. The supplied local-effects decision must come
    /// from the caller's owned use; packet-4 flags alone do not provide it.</summary>
    public static bool TryStepTorchGodFavor(PlayerSelectedConsumableState1458? previous,
        in PlayerSelectedConsumableFacts1458 facts, int selectedStack,
        bool unlocked, bool usingBiomeTorches, bool localEffects, bool noItems,
        Func<int, int, int> nextInteger, Func<double> nextDouble,
        out PlayerTorchGodFavorTransition1458 transition)
    {
        transition = default;
        if (selectedStack is < 0 or > short.MaxValue ||
            (facts.Item.Value != 5043 && !facts.Item.IsNone)) return false;
        var useFacts = facts with
        {
            Cursed = noItems,
            LastUseSuccess = localEffects || facts.LastUseSuccess
        };
        if (!TryStepCore(previous, in useFacts, nextInteger, nextDouble, localEffects: false, out var use))
            return false;
        bool applied = facts.Item.Value == 5043 && localEffects && facts.AllowedToHoldItems &&
            use.State.Animation > 0 && use.State.ItemTime == 0 && !unlocked;
        if (applied)
        {
            unlocked = true;
            usingBiomeTorches = true;
            VanillaSelectedConsumableCatalog1458.TryGet(facts.Item, out var item);
            use = use with { State = use.State with { ItemTime = item.UseTime, ItemTimeMax = item.UseTime } };
        }
        bool consumptionPoint = localEffects && facts.AllowedToHoldItems && !facts.CrowdControlled &&
            use.State.ItemTimeMax != 0 && use.State.ItemTime == use.State.ItemTimeMax;
        bool consumed = consumptionPoint && selectedStack > 0;
        int remaining = selectedStack - (consumed ? 1 : 0);
        var state = use.State;
        if (consumptionPoint && remaining == 0)
            state = state with { ItemTime = state.Animation };
        use = use with { State = state, Consumed = consumed, RemainingStack = remaining };
        transition = new(use, unlocked, usingBiomeTorches, applied)
        {
            SelectedItem = remaining == 0 && state.Animation == 0 ? VanillaItemIds.None : facts.Item
        };
        return true;
    }

    /// <summary>Source UpdatePermanentBoosters, applied after equipment from the
    /// flags captured at phase entry. Calling this with newly adopted flags is a caller ordering error.</summary>
    public static bool TryApplyPermanentBoosters(in PlayerPermanentBoosterState1458 before,
        in PlayerPermanentUnlockState1458 unlocks, out PlayerPermanentBoosterState1458 after)
    {
        after = before;
        if (before.Defense > int.MaxValue - 4 || before.FishingSkill > int.MaxValue - 3 ||
            !float.IsFinite(before.PickSpeed) || !float.IsFinite(before.TileSpeed) || !float.IsFinite(before.WallSpeed))
            return false;
        int defense = before.Defense;
        int fishing = before.FishingSkill;
        float pick = before.PickSpeed, tile = before.TileSpeed, wall = before.WallSpeed;
        if (unlocks.AegisFruit) defense += 4;
        if (unlocks.GummyWorm) fishing += 3;
        if (unlocks.Ambrosia)
        {
            pick -= .05f;
            tile += .05f;
            wall += .05f;
        }
        after = new(defense, fishing, pick, tile, wall);
        return true;
    }

    /// <summary>Final speed inputs after source caps and reciprocals. This is not
    /// mining animation speed: pickSpeed belongs to local tool ItemTime consumers.</summary>
    public static bool TryFinalizePermanentSpeeds(in PlayerPermanentBoosterState1458 before,
        out PlayerPermanentSpeedState1458 after)
    {
        after = default;
        if (!float.IsFinite(before.PickSpeed) || !float.IsFinite(before.TileSpeed) ||
            !float.IsFinite(before.WallSpeed) || before.TileSpeed <= 0f || before.WallSpeed <= 0f)
            return false;
        float tile = before.TileSpeed > 3f ? 3f : before.TileSpeed;
        float wall = before.WallSpeed > 3f ? 3f : before.WallSpeed;
        after = new(before.PickSpeed < .3f ? .3f : before.PickSpeed, 1f / tile, 1f / wall);
        return true;
    }

    /// <summary>AdjTiles' inclusive raw Simple tile region. The crafting caller owns
    /// world clamping and station/liquid scanning; this does not change ordinary reach checks.</summary>
    public static bool TryGetPermanentCraftingRegion(float positionX, float positionY,
        int width, int height, int tileRangeX, int tileRangeY,
        in PlayerPermanentUnlockState1458 unlocks, out PlayerCraftingRegion1458 region)
    {
        region = default;
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY) ||
            positionX is < 0 or > 16_000_000 || positionY is < 0 or > 16_000_000 ||
            width is < 1 or > 1_000 || height is < 1 or > 1_000 ||
            tileRangeX is < 0 or > 1_000 || tileRangeY is < 0 or > 1_000)
            return false;
        int extension = unlocks.ArtisanLoaf ? 4 : 0;
        int reachX = Math.Min(tileRangeX, 20) + extension;
        int reachY = Math.Min(tileRangeY, 20) + extension;
        int left = (int)(positionX / 16f) - reachX;
        int top = (int)(positionY / 16f) - reachY;
        int right = (int)Math.Ceiling((positionX + width) / 16f) - 1 + reachX;
        int bottom = (int)Math.Ceiling((positionY + height) / 16f) - 1 + reachY;
        region = new(left, top, right, bottom);
        return true;
    }

    /// <summary>Configured ItemCheck for Life Crystal29, Mana Crystal109 and Life Fruit1291.
    /// Local ownership is explicit: remote ItemCheck advances clocks but never invokes these effects.
    /// The explicit noItems input owns the start gate; a raw cursed field alone is not that gate.
    /// Raw zero-stack source identity is retained here; authoritative custody belongs to the caller.</summary>
    public static bool TryStepPermanentConsumable(PlayerSelectedConsumableState1458? previous,
        in PlayerSelectedConsumableFacts1458 facts, int baseLifeMaximum, int baseManaMaximum,
        int selectedStack, bool localEffects, bool noItems, Func<int, int, int> nextInteger,
        Func<double> nextDouble, out PlayerPermanentConsumableTransition1458 transition)
    {
        transition = default;
        if (facts.Item.Value is not (29 or 109 or 1291) ||
            baseLifeMaximum is < 0 or > short.MaxValue || baseManaMaximum is < 0 or > 400 ||
            !VanillaWorldItemAllocationCatalog1458.TryGet(facts.Item.Value, out var allocation) ||
            selectedStack < 0 || selectedStack > allocation.MaximumStack)
        {
            return false;
        }

        var useFacts = facts with { Cursed = noItems,
            LastUseSuccess = localEffects || facts.LastUseSuccess };
        if (!TryStepCore(previous, in useFacts, nextInteger, nextDouble, localEffects: false, out var use))
        {
            return false;
        }

        int derivedLifeMaximum = facts.LifeMaximum;
        int derivedManaMaximum = facts.ManaMaximum;
        bool effectPoint = localEffects && facts.AllowedToHoldItems &&
            use.State.Animation > 0 && use.State.ItemTime == 0;
        int lifeIncrease = effectPoint ? facts.Item.Value switch
        {
            29 when baseLifeMaximum < 400 => 20,
            1291 when baseLifeMaximum is >= 400 and < 500 => 5,
            _ => 0
        } : 0;
        int manaIncrease = effectPoint && facts.Item.Value == 109 && baseManaMaximum < 200 ? 20 : 0;
        bool applied = lifeIncrease != 0 || manaIncrease != 0;
        if (applied)
        {
            baseLifeMaximum += lifeIncrease;
            derivedLifeMaximum += lifeIncrease;
            baseManaMaximum += manaIncrease;
            derivedManaMaximum += manaIncrease;
            use = use with
            {
                Life = use.Life + lifeIncrease,
                Mana = use.Mana + manaIncrease,
                State = use.State with { ItemTime = 30, ItemTimeMax = 30 }
            };
        }

        bool consumptionPoint = localEffects && facts.AllowedToHoldItems && !facts.CrowdControlled &&
            use.State.ItemTimeMax != 0 && use.State.ItemTime == use.State.ItemTimeMax;
        bool consumed = consumptionPoint && selectedStack > 0;
        int remaining = selectedStack - (consumed ? 1 : 0);
        var state = use.State;
        if (consumptionPoint && remaining == 0)
        {
            state = state with { ItemTime = state.Animation };
        }
        use = use with { State = state, Consumed = consumed, RemainingStack = remaining };
        transition = new(use, baseLifeMaximum, derivedLifeMaximum,
            baseManaMaximum, derivedManaMaximum, applied);
        return true;
    }

    /// <summary>Before ResetEffects, remote manaHeat visuals; returns the reset neutral heat.</summary>
    public static bool TryStepHeat(float? previousHeat, Func<double> nextDouble,
        Func<int, int, int> nextInteger, out float resetHeat)
    {
        resetHeat = 1f;
        if (previousHeat is not { } heat || !float.IsFinite(heat) || heat is < 0f or > 2f ||
            nextDouble is null || nextInteger is null) return false;
        if (heat != 1f)
        {
            for (int i = 0; i < 2; i++)
            {
                if ((float)nextDouble() * 6f < .3f)
                {
                    nextInteger(10, 26);
                    nextDouble();
                    nextDouble();
                }
            }
        }
        return true;
    }

    /// <summary>Detached admitted-family QuickBuff. Source input eligibility is explicit; this does not
    /// establish button intent or funded custody. All supplied images must be canonical, including inactive Void slots.
    /// Unsupported potentially mutating buff/food families refuse; immunity is an explicit detached input.</summary>
    public static bool TryPlanQuickBuff(ReadOnlySpan<WorldItemPickupSlot1458> inventory,
        ReadOnlySpan<WorldItemPickupSlot1458> voidItems, ReadOnlySpan<BuffTypeId> buffTypes,
        ReadOnlySpan<int> buffTimes, in QuickBuffEligibility1458 eligibility,
        ReadOnlySpan<bool> immunity, out QuickBuffPlan1458? plan)
    {
        plan = null;
        if (inventory.Length != 59 || voidItems.Length != 40 || buffTypes.Length != 44 ||
            buffTimes.Length != 44 || !immunity.IsEmpty && immunity.Length <= 117) return false;
        foreach (var item in inventory) if (!IsQuickBuffItem(item)) return false;
        foreach (var item in voidItems) if (!IsQuickBuffItem(item)) return false;
        for (int i = 0; i < buffTypes.Length; i++)
            if (!VanillaBuffIds.TryCreate(buffTypes[i].Value, out _) || buffTimes[i] < 0) return false;

        var nextInventory = inventory.ToArray();
        var nextVoid = voidItems.ToArray();
        var types = buffTypes.ToArray();
        var times = buffTimes.ToArray();
        var mutations = new List<QuickBuffMutation1458>(98);
        bool blocked = eligibility.PendingInventoryActions || eligibility.Cursed ||
            eligibility.CrowdControlled || eligibility.Dead || eligibility.Spectating || eligibility.NoItems;
        if (!blocked && CountQuickBuffPrefix(types) != types.Length)
        {
            bool voidEnabled = false;
            for (int i = 0; i < 58; i++)
                if (inventory[i].Type == VanillaNpcLootPredicateItemIds1458.OpenVoidBag.Value && inventory[i].Stack > 0) voidEnabled = true;
            // QuickBuff selects food before scanning ordinary buffs. Preflight both source images
            // before applying any mutation, so unsupported food/pet/mana behavior cannot be omitted.
            if (!HasSupportedQuickBuffItems(inventory[..58], immunity.Length) ||
                voidEnabled && !HasSupportedQuickBuffItems(voidItems, immunity.Length)) return false;
            ApplyBestQuickBuffFood(nextInventory, nextVoid, voidEnabled, types, times, immunity, mutations);
            for (int i = 0; i < 58 && CountQuickBuffPrefix(types) != types.Length; i++)
                ApplyQuickBuffItem(nextInventory, i, i, types, times, immunity, mutations);
            if (voidEnabled)
                for (int i = 0; i < 40 && CountQuickBuffPrefix(types) != types.Length; i++)
                    ApplyQuickBuffItem(nextVoid, i, 59 + i, types, times, immunity, mutations);
        }
        plan = new(nextInventory, nextVoid, types, times, mutations.ToArray());
        return true;
    }

    private static bool IsQuickBuffItem(in WorldItemPickupSlot1458 item)
    {
        if (item.Type == 0) return item == default; // gameplay-domain-literal-audit: allow raw-entity-type-decision - canonical empty item sentinel, not a Terraria content identity.
        return item.Type > 0 && item.Stack > 0 && // gameplay-domain-literal-audit: allow raw-entity-type-decision - positive item-domain validity, not a Terraria content identity.
            item.Prefix >= 0 && item.Prefix < VanillaItemPrefixCatalog.Count &&
            VanillaItemRetainedIdentity1458.TryResolve(new(item.Type), out var retained) &&
            retained.Value == item.Type &&
            VanillaWorldItemAllocationCatalog1458.TryGet(item.Type, out var allocation) &&
            (VanillaCoinFacts.TryGetValue(new(item.Type), out _)
                ? VanillaCoinFacts.IsValidRetainedStack(new(item.Type), item.Stack)
                : item.Stack <= allocation.MaximumStack);
    }

    private static bool HasSupportedQuickBuffItems(ReadOnlySpan<WorldItemPickupSlot1458> items, int immunityLength)
    {
        foreach (var item in items)
            if (VanillaSelectedConsumableCatalog1458.MayParticipateInQuickBuff(new(item.Type)) &&
                (!VanillaSelectedConsumableCatalog1458.TryGet(new(item.Type), out var definition) ||
                    definition.BuffType == 0 || immunityLength > 0 && definition.BuffType >= immunityLength)) return false;
        return true;
    }

    private static int CountQuickBuffPrefix(ReadOnlySpan<BuffTypeId> types)
    {
        // Player.CountBuffs advances its read index only on a positive type. Duration is irrelevant.
        int count = 0;
        for (int i = 0; i < types.Length; i++) if (types[count].Value > 0) count++;
        return count;
    }

    private static int FoodPriority(BuffTypeId type) => type == VanillaBuffIds.WellFed ? 1 :
        type == VanillaBuffIds.WellFed2 ? 2 : type == VanillaBuffIds.WellFed3 ? 3 : 0;

    private static void ApplyBestQuickBuffFood(WorldItemPickupSlot1458[] inventory,
        WorldItemPickupSlot1458[] voidItems, bool voidEnabled, BuffTypeId[] types, int[] times,
        ReadOnlySpan<bool> immunity, List<QuickBuffMutation1458> mutations)
    {
        int minimum = 0;
        for (int i = 0; i < types.Length; i++)
            if (times[i] >= 1)
            {
                int priority = FoodPriority(types[i]);
                if (minimum <= priority) minimum = priority + 1;
            }
        int selected = -1;
        int duration = 0;
        void Consider(WorldItemPickupSlot1458[] items, int slot, int sourceSlot)
        {
            var candidate = items[slot];
            if (candidate.Stack <= 0 || !VanillaSelectedConsumableCatalog1458.TryGet(new(candidate.Type), out var food)) return;
            int priority = FoodPriority(new BuffTypeId((short)food.BuffType));
            if (priority > 0 && priority >= minimum &&
                (selected < 0 || duration < food.BuffTime || priority > minimum))
            {
                selected = sourceSlot;
                duration = food.BuffTime;
                minimum = priority;
            }
        }
        for (int i = 0; i < 58; i++) Consider(inventory, i, i);
        if (voidEnabled)
            for (int i = 0; i < voidItems.Length; i++) Consider(voidItems, i, 59 + i);
        if (selected < 0) return;
        var chosenItems = selected < 59 ? inventory : voidItems;
        int chosenSlot = selected < 59 ? selected : selected - 59;
        var before = chosenItems[chosenSlot];
        VanillaSelectedConsumableCatalog1458.TryGet(new(before.Type), out var definition);
        var offer = new BuffTypeId((short)definition.BuffType);
        if (immunity.IsEmpty || !immunity[offer.Value])
        {
            // Source AddBuff scans current slots forward after each DelBuff compaction.
            // Adjacent configured fed-state duplicates therefore retain its source quirk.
            for (int i = 0; i < types.Length; i++)
                if (VanillaBuffDefinitionCatalog.TryGet(types[i], out var existing) && existing.IsFedState)
                    RemoveQuickBuffAt(types, times, i);
            AddQuickBuffOffer(types, times, offer, duration == 0 ? 3600 : duration);
        }
        var after = before.Stack == 1 ? default : before with { Stack = before.Stack - 1 };
        chosenItems[chosenSlot] = after;
        mutations.Add(new(selected, before, after, offer, duration == 0 ? 3600 : duration));
    }

    private static void RemoveQuickBuffAt(BuffTypeId[] types, int[] times, int slot)
    {
        types[slot] = VanillaBuffIds.None;
        times[slot] = 0;
        int destination = 0;
        for (int i = 0; i < types.Length - 1; i++)
            if (types[i] != VanillaBuffIds.None && times[i] != 0)
            {
                if (destination < i)
                {
                    types[destination] = types[i]; times[destination] = times[i];
                    types[i] = VanillaBuffIds.None; times[i] = 0;
                }
                destination++;
            }
    }

    private static void ApplyQuickBuffItem(WorldItemPickupSlot1458[] items, int slot, int sourceSlot,
        BuffTypeId[] types, int[] times, ReadOnlySpan<bool> immunity, List<QuickBuffMutation1458> mutations)
    {
        var before = items[slot];
        if (!VanillaSelectedConsumableCatalog1458.TryGet(new(before.Type), out var definition) ||
            definition.BuffType == 0) return;
        var offer = new BuffTypeId((short)definition.BuffType);
        if (FoodPriority(offer) != 0) return;
        foreach (var type in types) if (type == offer) return;
        if (immunity.IsEmpty || !immunity[offer.Value]) AddQuickBuffOffer(types, times, offer, definition.BuffTime);
        var after = before.Stack == 1 ? default : before with { Stack = before.Stack - 1 };
        items[slot] = after;
        mutations.Add(new(sourceSlot, before, after, offer, definition.BuffTime));
    }

    private static void AddQuickBuffOffer(BuffTypeId[] types, int[] times, BuffTypeId offer, int duration)
    {
        for (int attempt = 0; attempt < types.Length; attempt++)
        {
            int removable = 0;
            while (removable < types.Length && VanillaBuffDefinitionCatalog.IsDebuff(types[removable])) removable++;
            if (removable == types.Length) return;
            for (int i = removable; i < types.Length; i++)
                if (types[i] == VanillaBuffIds.None)
                {
                    types[i] = offer;
                    times[i] = duration;
                    return;
                }
            types[removable] = VanillaBuffIds.None;
            times[removable] = 0;
            int destination = 0;
            // Source DelBuff compacts 0..42 and leaves the special last slot in place.
            for (int i = 0; i < types.Length - 1; i++)
                if (types[i] != VanillaBuffIds.None && times[i] != 0)
                {
                    if (destination < i)
                    {
                        types[destination] = types[i];
                        times[destination] = times[i];
                        types[i] = VanillaBuffIds.None;
                        times[i] = 0;
                    }
                    destination++;
                }
        }
    }
}
