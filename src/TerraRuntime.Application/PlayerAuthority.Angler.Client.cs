using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal sealed class PendingAnglerClientQuest(WorldAnglerQuestData1458 quest, ulong revision,
        AnglerQuestCountEvidence1458 counts)
    {
        internal WorldAnglerQuestData1458 Quest => quest;
        internal ulong Revision => revision;
        internal AnglerQuestCountEvidence1458 Counts => counts;
        internal bool Intent;
    }

    private bool CanBeginAnglerClientQuest(ConnectionHandle connection, RuntimePlayerMember member)
    {
        if (anglerState is not { } quest || anglerRevision == ulong.MaxValue || member.IsDead || !member.HasHealth ||
            member.AnglerCompletedDay == anglerDay ||
            !transferProfiles.TryCapture(connection, out var appearance, out _, out _) || appearance is not { } profile ||
            quest.CompletedNames.Contains(profile.Name, StringComparer.Ordinal) ||
            !membership.TryGetTalkNpc(connection.Player, out short talk) ||
            !membership.TryGetTownShopSession(connection.Player, out var session) || session is null ||
            session.NpcSlot != talk || session.NpcType.Value != 369) return false;
        int type = VanillaAnglerQuest1458.ItemForQuest(quest.Quest);
        var capture = member.CursorMove?.Shop?.Capture;
        for (short slot = 0; slot < 58; slot++)
        {
            RuntimePlayerInventoryItem item;
            if (capture is not null) item = capture.Before[slot];
            else if (!inventory.TryGet(connection, slot, out item)) return false;
            if (item.ItemType.Value == type)
                return inventory.CanSpendFishingStock(connection, slot, type, capture);
        }
        return false;
    }

    private bool TryBeginAnglerClientQuest(ConnectionHandle connection)
    {
        if (!membership.TryGet(connection, out var member) || !CanBeginAnglerClientQuest(connection, member) ||
            !TryEnsureFishingReportJournal(connection, member, out var journal) || journal is null) return false;
        if (journal.AnglerClient is null)
            journal.AnglerClient = new(anglerState!, anglerRevision, member.AnglerQuestCounts);
        if (!ReferenceEquals(journal.AnglerClient.Quest, anglerState) || journal.AnglerClient.Revision != anglerRevision)
            return false;
        // Main.NPCChatText_DoAnglerQuest sends75 after local payment/rewards.
        // The intent opens no budget and cannot advance daily progression.
        journal.AnglerClient.Intent = true;
        return true;
    }

    private bool TrySealAnglerClientReports(ConnectionHandle connection)
    {
        if (!membership.TryGet(connection, out var member) ||
            member.CursorMove is not { Shop: { AnglerClient: { } pending } journal } receipt) return false;
        bool accepted = pending.Intent && ReferenceEquals(pending.Quest, anglerState) &&
            pending.Revision == anglerRevision && ReferenceEquals(pending.Counts, member.AnglerQuestCounts) &&
            TryCompleteAnglerQuest(connection, journal);
        if (!accepted) RefuseFishingCrateReports(connection, member, receipt, journal);
        return true;
    }
}
