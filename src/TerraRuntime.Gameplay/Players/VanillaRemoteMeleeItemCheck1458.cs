using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Gameplay.Players;

public readonly record struct PlayerRemoteMeleeItemFacts1458(ItemTypeId Item,
    bool ControlUseItem, bool LastUseSuccess, bool Cursed, bool CrowdControlled,
    bool SelectionBuffered, PrefixId Prefix = default, bool WindowsItemPrefixArithmetic = false);

public readonly record struct PlayerRemoteMeleeItemTransition1458(
    PlayerSelectedConsumableState1458 State, bool PendingItemReuse,
    bool BeganActualUse, bool IsMiningTool);

/// <summary>Dedicated remote style1 clocks. Picking and melee hits belong to the absent local-owner branch.</summary>
public static class VanillaRemoteMeleeItemCheck1458
{
    public static bool IsSupported(ItemTypeId item, PrefixId prefix = default,
        bool windowsItemPrefixArithmetic = false) =>
        TryGet(item, prefix, windowsItemPrefixArithmetic, out _, out _, out _);

    public static bool TryStep(PlayerSelectedConsumableState1458? previous,
        in PlayerRemoteMeleeItemFacts1458 facts, Func<int, int, int> nextInteger,
        out PlayerRemoteMeleeItemTransition1458 transition)
    {
        transition = default;
        if (previous is not { } state ||
            !TryGet(facts.Item, facts.Prefix, facts.WindowsItemPrefixArithmetic,
                out int useAnimation, out bool autoReuse, out bool miningTool) ||
            state.ItemTime is < 0 or > short.MaxValue || state.ItemTimeMax is < 0 or > short.MaxValue ||
            state.Animation is < 0 or > short.MaxValue || state.AnimationMax is < 0 or > short.MaxValue ||
            state.PotionDelay is < 0 or > 3_600 || state.RevolverCritBonus < int.MinValue + 2 ||
            nextInteger is null)
            return false;
        if (facts.CrowdControlled)
        {
            transition = new(state with { Animation = 0, AnimationMax = 0 }, false, false, miningTool);
            return true;
        }

        int animation = state.Animation;
        int animationMax = state.AnimationMax;
        bool release = state.ReleaseUseItem;
        if (autoReuse && !facts.Cursed && !facts.SelectionBuffered)
        {
            release = true;
            // Style1 does not use the remote style5 ApplyItemAnimation renewal. It clears
            // the last frame and can run genuine StartActualUse in this same ItemCheck.
            if (animation == 1)
                animation = 0;
        }
        if (animation == 0)
            animationMax = 0;
        bool beganActualUse = facts.ControlUseItem && release && animation == 0 &&
            facts.LastUseSuccess && !facts.Cursed && !facts.SelectionBuffered;

        int crit = state.RevolverCritBonus;
        if (nextInteger(0, 3) == 0)
            crit -= 2;
        if (beganActualUse)
            animation = animationMax = useAnimation;
        bool pending = false;
        if (animation > 0)
        {
            animation--;
            pending = animation == 0 && facts.ControlUseItem && release;
        }
        transition = new(new(Math.Max(0, state.ItemTime - 1), state.ItemTimeMax,
            animation, animationMax, !facts.ControlUseItem, state.PotionDelay, crit),
            pending, beganActualUse, miningTool);
        return true;
    }

    /// <summary>Common Update order: after the outside return and before dead/ghost or ItemCheck.</summary>
    public static bool TryStepAttackCooldown(int? previous, int incomingAnimation, out int next)
    {
        next = default;
        if (previous is not { } counter || counter < 0 || incomingAnimation is < 0 or > short.MaxValue)
            return false;
        next = counter > 0 ? counter - 1 : counter;
        if (incomingAnimation == 0)
            next = 0;
        return true;
    }

    private static bool TryGet(ItemTypeId item, PrefixId prefix, bool windowsItemPrefixArithmetic,
        out int useAnimation, out bool autoReuse, out bool miningTool)
    {
        useAnimation = 0;
        autoReuse = false;
        miningTool = false;
        if (!VanillaRemoteMeleeItemCatalog1458.TryGet(item, out var definition) ||
            !VanillaItemPrefixTable1458.TryResolveSpeedMultiplier(item, prefix,
                windowsItemPrefixArithmetic, out float speed))
            return false;
        useAnimation = windowsItemPrefixArithmetic
            ? (int)Math.Round(definition.UseAnimation * (double)speed)
            : (int)Math.Round(definition.UseAnimation * speed);
        autoReuse = definition.AutoReuse;
        miningTool = definition.IsMiningTool;
        return true;
    }
}

public readonly record struct PlayerRemoteMeleeItemDefinition1458(
    int UseAnimation, bool AutoReuse, bool IsMiningTool);

/// <summary>The 80 invariant mining tools and 21 wood/metal broadswords with independently captured remote clocks.</summary>
public static class VanillaRemoteMeleeItemCatalog1458
{
    public const int Count = 101;

    private static ReadOnlySpan<ushort> MiningTools =>
    [
        1, 7, 10, 45, 103, 104, 122, 196, 204, 217, 367, 654, 657, 660, 776, 777,
        778, 787, 797, 798, 799, 882, 922, 990, 991, 992, 993, 1188, 1195, 1202,
        1222, 1223, 1224, 1230, 1233, 1234, 1294, 1305, 1320, 1506, 1507, 1917,
        2176, 2320, 2341, 2516, 2746, 2776, 2781, 2786, 3466, 3481, 3482, 3485,
        3487, 3488, 3491, 3493, 3494, 3497, 3499, 3500, 3503, 3505, 3506, 3509,
        3511, 3512, 3515, 3517, 3518, 3521, 3522, 3523, 3524, 3525, 4059, 4317,
        5095, 5295
    ];

    private static ReadOnlySpan<ushort> Broadswords =>
    [
        4, 24, 482, 483, 484, 653, 656, 659, 921, 1185, 1192, 1199, 2517, 2745,
        3484, 3490, 3496, 3502, 3508, 3514, 3520
    ];

    private static ReadOnlySpan<ushort> AutoReuseBroadswords => [482, 483, 484, 659, 1185, 1192, 1199];

    public static bool TryGet(ItemTypeId item, out PlayerRemoteMeleeItemDefinition1458 definition)
    {
        definition = default;
        ushort id = unchecked((ushort)item.Value);
        bool mining = MiningTools.Contains(id);
        if ((!mining && !Broadswords.Contains(id)) ||
            !VanillaItemCombatCatalog.TryGetDirectMelee(item, out var combat))
            return false;
        // Reuse existing verified SetDefaults animation facts, without granting melee combat,
        // item variants, owner-local picking, or additional item identities through this query.
        definition = new(combat.AnimationTicks, mining || AutoReuseBroadswords.Contains(id), mining);
        return true;
    }
}
