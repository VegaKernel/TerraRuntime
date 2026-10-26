using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private WorldFileRuntimeMetadata? anglerWorld;
    private RuntimeWorldProgressionMutations? anglerProgression;
    private VanillaUnifiedRandom1458? anglerRandom;
    private WorldAnglerQuestData1458? anglerState;
    private ulong anglerRevision;
    private ulong anglerDay = 1;
    private Action<ConnectionHandle, byte, bool, int>? anglerPublisher;

    internal void BindAnglerWorld(WorldFileRuntimeMetadata metadata, RuntimeWorldProgressionMutations progression,
        VanillaUnifiedRandom1458 random, Action<ConnectionHandle, byte, bool, int>? publisher = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(random);
        if (anglerWorld is not null) throw new InvalidOperationException("Angler world is already bound.");
        anglerWorld = metadata;
        anglerProgression = progression;
        anglerRandom = random;
        anglerState = metadata.AnglerQuest;
        anglerRevision = 1;
        anglerPublisher = publisher;
    }

    internal WorldAnglerQuestData1458? CaptureAnglerSaveState() => anglerState;

    internal bool TryCaptureAnglerState(ConnectionHandle connection, out byte quest, out bool completed, out int count)
    {
        quest = 0; completed = false; count = 0;
        if (anglerState is null || !membership.TryGet(connection, out var member) ||
            !transferProfiles.TryCapture(connection, out var appearance, out _, out _) || appearance is not { } profile)
            return false;
        quest = checked((byte)anglerState.Quest);
        completed = member.AnglerCompletedDay == anglerDay || anglerState.CompletedNames.Contains(profile.Name, StringComparer.Ordinal);
        count = member.AnglerQuestsFinished;
        return true;
    }

    internal void PublishAnglerState(ConnectionHandle connection)
    {
        if (TryCaptureAnglerState(connection, out byte quest, out bool completed, out int count))
            anglerPublisher?.Invoke(connection, quest, completed, count);
    }

    internal bool SwapAnglerQuestAtDawn()
    {
        if (anglerWorld is null || anglerRandom is null || anglerState is null ||
            anglerRevision == ulong.MaxValue || anglerDay == ulong.MaxValue)
            return false;
        var nextRandom = anglerRandom.Clone();
        var world = CaptureAnglerConditions();
        if (!VanillaAnglerQuest1458.TryChoose(new FishingRandom1458(nextRandom), world, out int quest)) return false;
        // The daily list is replaced with the new quest on the same authoritative owner,
        // before any packet74 observer may reenter the world.
        anglerState = new(quest, []);
        anglerRevision++;
        anglerDay++;
        anglerRandom.CopyStateFrom(nextRandom);
        var recipients = membership.Members.Select(static member => member.Connection).ToArray();
        foreach (var connection in recipients) PublishAnglerState(connection);
        return true;
    }

    internal bool TryCompleteAnglerQuest(ConnectionHandle connection)
    {
        if (anglerState is not { } before || anglerRevision == ulong.MaxValue ||
            !membership.TryGet(connection, out var member) || member.IsDead || !member.HasHealth ||
            member.AnglerQuestsFinished is < 0 or >= int.MaxValue - 50 ||
            member.AnglerCompletedDay == anglerDay ||
            !transferProfiles.TryCapture(connection, out var appearance, out var equipment, out _) ||
            appearance is not { } profile ||
            before.CompletedNames.Contains(profile.Name, StringComparer.Ordinal) ||
            before.CompletedNames.Count >= WorldAnglerQuestData1458.MaximumCompletedNames ||
            !membership.TryGetTalkNpc(connection.Player, out short talk) ||
            !membership.TryGetTownShopSession(connection.Player, out var session) || session is null ||
            session.NpcSlot != talk || session.NpcType.Value != 369 ||
            session.VendorStore is null || !session.VendorStore.TryGet(session.VendorHandle, out var vendor) ||
            vendor.Type != 369 || vendor.Simulation.Life <= 0 || !AnglerWithinReach(member, vendor) ||
            !float.IsFinite(session.PriceAdjustment) || session.PriceAdjustment is < .5f or > 2f ||
            (session.MissingFacts & RuntimeTownCommerceMissingFacts1458.LoveStruck) != 0)
            return false;

        int fish = VanillaAnglerQuest1458.ItemForQuest(before.Quest);
        short fishSlot = -1;
        var ownedItems = new List<int>();
        for (short slot = 0; slot < 59; slot++)
        {
            if (!inventory.TryGet(connection, slot, out var item)) return false;
            if (item.IsEmpty) continue;
            ownedItems.Add(item.ItemType.Value);
            // FindItem returns the first physical slot. An unfunded earlier fish
            // cannot be skipped in favor of a later funded stack.
            if (slot < 58 && item.ItemType.Value == fish && fishSlot < 0) fishSlot = slot;
        }
        if (fishSlot < 0 || !inventory.TryGetFundedQuantity(connection, fishSlot, out short funded) || funded <= 0)
            return false;
        var banks = new RuntimePlayerInventoryItem[160];
        var copper = new long[160];
        if (!inventory.TryCaptureShopBanks(connection, banks, copper)) return false;
        foreach (var item in banks) if (!item.IsEmpty) ownedItems.Add(item.ItemType.Value);
        foreach (var item in equipment) if (item.Stack > 0) ownedItems.Add(item.ItemNetId);

        int oldCount = member.AnglerQuestsFinished;
        ulong revision = anglerRevision;
        var conditions = CaptureAnglerConditions();
        var nextState = new WorldAnglerQuestData1458(before.Quest, before.CompletedNames.Append(profile.Name));
        var rewardContext = new AnglerRewardContext1458(oldCount + 1, fish, conditions.HardMode,
            expertMode, session.PriceAdjustment, ownedItems);
        bool Current() => anglerRevision == revision && ReferenceEquals(anglerState, before) &&
            member.AnglerQuestsFinished == oldCount && membership.TryGet(connection, out var current) &&
            ReferenceEquals(current, member) && !member.IsDead && CaptureAnglerConditions() == conditions &&
            membership.TryGetTalkNpc(connection.Player, out short currentTalk) && currentTalk == talk &&
            membership.TryGetTownShopSession(connection.Player, out var currentSession) && ReferenceEquals(currentSession, session) &&
            session.VendorStore.TryGet(session.VendorHandle, out var currentVendor) && currentVendor == vendor &&
            transferProfiles.TryCapture(connection, out var currentAppearance, out _, out _) && currentAppearance == appearance;
        bool adopted = false;
        try
        {
            return TryFishingRewards(connection, fishSlot, fish, debitAfterRewards: false, roll: rewards =>
            {
                foreach (var reward in VanillaAnglerReward1458.Roll(new FishingRandom1458(rewards.Random), rewardContext))
                    if (!rewards.Grant(reward.Type, reward.Stack, canGoIntoVoidVault: false)) return false;
                return true;
            }, guard: Current, adopt: () =>
            {
                member.AnglerQuestsFinished = oldCount + 1;
                member.AnglerCompletedDay = anglerDay;
                anglerState = nextState;
                anglerRevision++;
                adopted = true;
            });
        }
        finally
        {
            // Rewards may already be committed when a network observer throws.
            // Neither a retry nor the completion flag may pay again.
            if (adopted) PublishAnglerState(connection);
        }
    }

    private AnglerQuestWorld1458 CaptureAnglerConditions()
    {
        var world = anglerWorld!;
        bool Done(VanillaWorldProgressionId id, bool baseline) => baseline || anglerProgression?.IsCompleted(id) == true;
        bool hardMode = Done(VanillaWorldProgressionId.Hardmode, world.HardMode);
        bool earlyBoss = Done(VanillaWorldProgressionId.EyeOfCthulhu, world.DownedBoss1) ||
            Done(VanillaWorldProgressionId.EvilBoss, world.DownedBoss2) ||
            Done(VanillaWorldProgressionId.Skeletron, world.DownedBoss3) ||
            Done(VanillaWorldProgressionId.KingSlime, world.DownedSlimeKing) ||
            Done(VanillaWorldProgressionId.QueenBee, world.DownedQueenBee);
        return new(hardMode, world.Crimson, earlyBoss, world.WorldSurface > 50);
    }

    private static bool AnglerWithinReach(RuntimePlayerMember player, in NpcSnapshot vendor)
    {
        if (!VanillaTownNpcFacts1458.TryGetDefinition(vendor.TypeIdentity, out var definition)) return false;
        var body = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) : (Width: 20f, Height: 42f);
        // Player.Update uses TileReachCheckSettings.Simple for an open conversation.
        // The base 5x3 range is always legal; larger Journey/accessory reach remains
        // conservatively unadmitted until that mutable global range is owned here.
        int left = ((int)(player.PositionX / 16f) - 5) * 16;
        int top = ((int)(player.PositionY / 16f) - 3) * 16;
        int right = ((int)Math.Ceiling((player.PositionX + body.Width) / 16f) - 1 + 5) * 16 + 15;
        int bottom = ((int)Math.Ceiling((player.PositionY + body.Height) / 16f) - 1 + 3) * 16 + 15;
        return left < (int)vendor.PositionX + definition.BaseWidth && right > (int)vendor.PositionX &&
            top < (int)vendor.PositionY + definition.BaseHeight && bottom > (int)vendor.PositionY;
    }

}
