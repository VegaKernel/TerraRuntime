using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed record FishingPlayerCapture1458(RuntimePlayerProjectileUseCapture Actor,
    FishingConditions1458 Conditions, float Luck, bool CratePotion, bool SonarPotion);

internal sealed partial class PlayerAuthority
{
    private RuntimeWorldClock? fishingClock;
    internal void BindFishingWorldClock(RuntimeWorldClock? clock) => fishingClock = clock;

    internal void AdoptConsumedFishingBait(RuntimePlayerMember member, FishingItemAction1458 action)
    {
        if (!action.ConsumedBait || action.Pull?.Result.BaitItemType is not (4361 or 4362)) return;
        // ConsumeBait calls LadyBugKilled at the local fisher's exact center, so
        // the source distance multiplier is1. Other players are unaffected.
        fishingClock?.ConsumeLadyBugBait();
        var factors = member.LuckComponents ?? default;
        int penalty = action.Pull.Result.BaitItemType == 4362 ? -21_600 : -10_800;
        int beforeSourceUse = VanillaPlayerLuckFacts1458.AdvanceRemoteFactors(factors, 1).LadyBugLuckTimeLeft;
        if (beforeSourceUse > penalty)
        {
            member.LuckComponents = factors with { LadyBugLuckTimeLeft = penalty };
            member.FishingLadyBugAppliedTick = currentCombatTick;
            member.FishingLadyBugPriorTimer = factors.LadyBugLuckTimeLeft;
        }
        member.OwnsFishingLadyBugLuck = true;
    }

    internal bool TryCaptureFishingPlayer(RuntimePlayerMember member, FishingPowerContext1458 weather,
        out FishingPlayerCapture1458? capture, bool ending = false)
    {
        capture = null;
        if (!TryCaptureProjectileUse(member.Connection, out var actor) || actor is null ||
            !ReferenceEquals(actor.Member, member) || member.IsDead || member.HasMount ||
            transferProfiles.CaptureBuffState(member.Connection) is not { } buffs ||
            !ending && (member.ItemPhase is null || buffs.IsItemUseCrowdControlled || buffs.HasNoItemsEffect) ||
            !inventory.TryGet(member.Connection, member.SelectedItem, out var selected) ||
            !ending && (!FishingRules1458.TryGetPole(selected.ItemType.Value, out _) || selected.Prefix.Value != 0))
            return false;
        Span<FishingInventorySlot1458> image = stackalloc FishingInventorySlot1458[59];
        for (int i = 0; i < image.Length; i++)
        {
            if (!inventory.TryGet(member.Connection, i, out var item)) return false;
            image[i] = new(item.ItemType.Value, item.Stack);
        }
        byte effects = buffs.CaptureFishingEffects(currentCombatTick);
        int permanentSkill = member.ItemPhase?.PermanentBoosters?.FishingSkill ??
            (((actor.Appearance?.ConsumableUnlockFlags ?? 0) & PlayerPermanentUnlockState1458.GummyWormFlag) != 0 ? 3 : 0);
        weather = weather with { FishingSkill = permanentSkill +
            ((effects & 1) != 0 ? 15 : 0) };
        var conditions = FishingRules1458.GetConditions(image, member.SelectedItem, weather);
        capture = new(actor, conditions, member.Luck,
            (effects & 4) != 0, (effects & 2) != 0);
        return true;
    }

    internal int CaptureFishingQuestFish(ConnectionHandle connection)
    {
        if (!TryCaptureAnglerState(connection, out byte quest, out bool complete, out _) || complete) return 0;
        int type = VanillaAnglerQuest1458.ItemForQuest(quest);
        for (int i = 0; i < 58; i++)
            if (inventory.TryGet(connection, i, out var item) && item.Stack > 0 && item.ItemType.Value == type) return 0;
        return type;
    }
}
