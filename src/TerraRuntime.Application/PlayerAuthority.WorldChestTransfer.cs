using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private RuntimeChestStore? ownedWorldChests;
    private WorldTileStore? ownedWorldChestTiles;
    private RuntimeChestReplicationRegistry? ownedWorldChestReplication;

    internal void BindOwnedWorldChests(RuntimeChestStore store, WorldTileStore tiles,
        RuntimeChestReplicationRegistry replication)
    {
        ownedWorldChests = store;
        ownedWorldChestTiles = tiles;
        ownedWorldChestReplication = replication;
    }

    internal bool ObserveOwnedWorldChestReport(ConnectionHandle connection, TerrariaChestItemState report)
    {
        if (ownedWorldChests is null || ownedWorldChestTiles is null ||
            !membership.TryGet(connection, out var member) || member.IsDead || member.ItemPhase is null ||
            shopJournalPublishing[connection.Player.Slot.Value] == connection) return false;
        var receipt = member.CursorMove;
        var journal = receipt?.Shop;
        if (journal is null)
        {
            if (receipt is not null || !ownedWorldChests.TryCaptureOwnedTransfer(connection, ownedWorldChestTiles, out var world) ||
                world is null || !inventory.TryCaptureShopJournal(connection, out var capture) || capture is null) return false;
            journal = new(capture, null, null) { WorldChest = world, WorldReports = [] };
            receipt = new(member.CaptureSnapshot(), member.ItemPhase, member.ProjectileUseInputRevision, null, Shop: journal);
            member.CursorMove = receipt;
        }
        if (journal.WorldChest is not { } source || journal.WorldReports is not { } reports || journal.IsSealed ||
            !source.IsCurrent || !journal.Capture.IsCurrent || !IsCurrentShopActor(member, receipt!) ||
            report.ChestId != source.ChestId || report.ItemSlot >= source.Before.Length || reports.Count >= 255)
            return false;
        reports.Add(report);
        // Original context3 may close with a restored tuple and emit no player5
        // or138. The detached explanation still has to prove unique private data.
        if (report.Stack == source.Before[report.ItemSlot].Stack &&
            report.ItemNetId == source.Before[report.ItemSlot].ItemType &&
            report.Prefix == source.Before[report.ItemSlot].Prefix)
            TrySealWorldChestTransfer(connection, member, receipt!, journal);
        return true;
    }

    private bool TrySealWorldChestTransfer(ConnectionHandle connection, RuntimePlayerMember member,
        PendingCursorMove receipt, PendingShopJournal journal)
    {
        if (journal.WorldChest is null) return false;
        if (journal.IsSealed) return true;
        journal.IsSealed = true;
        journal.PlanningAnimation = member.ItemAnimation;
        journal.PlanningItemTime = member.ItemPhase?.Selected.ItemTime ?? 0;
        journal.WorldResult = new();
        journal.Work = inventory.PrepareWorldChestTransfer(connection, journal.Capture,
            journal.WorldChest, journal.WorldReports!, journal.Reports, journal.PlanningAnimation,
            journal.PlanningItemTime, journal.WorldResult).GetEnumerator();
        return true;
    }

    private void CompleteWorldChestTransfer(ConnectionHandle connection, RuntimePlayerMember member,
        PendingCursorMove receipt, PendingShopJournal journal)
    {
        var result = journal.WorldResult!;
        var actor = result.Inventory;
        var world = journal.WorldChest!;
        if (result.Ambiguous || actor is null || result.Chest is null || !actor.IsCurrent || !world.IsCurrent ||
            !IsCurrentShopActor(member, receipt) || member.Revision == ulong.MaxValue)
        {
            DropBankBatchAlternative(member, journal);
            RejectedEquipmentUpdates++;
            return;
        }
        // Both owners are fully validated on the same world thread. No callback
        // occurs between these writes; the inventory preparation has no escrow.
        if (!world.TryAdoptUnpublished(result.Chest, result.ChestQuantity!, result.ChestCopper!) || !actor.TryAdoptUnpublished())
            throw new InvalidOperationException("Current two-store world transfer lost pure adoption.");
        member.CursorMove = null;
        member.TryAdvanceRevision();
        Exception? failure = null;
        shopJournalPublishing[connection.Player.Slot.Value] = connection;
        try
        {
            try { PublishAcceptedBankJournal(connection, member, journal, actor); }
            catch (Exception exception) { failure = exception; }
            shopJournalPublishing[connection.Player.Slot.Value] = connection;
            foreach (TerrariaChestItemState report in journal.WorldReports!)
            {
                if (!membership.TryGet(connection, out var current) || !ReferenceEquals(current, member) || !world.IsAcceptedImageCurrent) break;
                try { ownedWorldChestReplication!.PublishItem(connection, in report); }
                catch (Exception exception) { failure ??= exception; }
            }
        }
        finally { shopJournalPublishing[connection.Player.Slot.Value] = null; }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
