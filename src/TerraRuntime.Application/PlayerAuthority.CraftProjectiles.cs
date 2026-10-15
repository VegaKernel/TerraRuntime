using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private RuntimeProjectileStore.CraftRemovalPreparation? PrepareCraftProjectileRemovals(
        ConnectionHandle connection, PendingShopJournal journal)
    {
        short type = 0;
        for (int slot = 0; slot < journal.Reports.Length; slot++)
        {
            var before = journal.Capture.Before[slot];
            var after = journal.Reports[slot];
            if (!journal.Seen[slot] || after.IsEmpty ||
                (before.ItemType == after.ItemType && before.Prefix == after.Prefix && after.Stack <= before.Stack)) continue;
            short candidate = after.ItemType.Value switch { 6148 => 1093, 6149 => 1094, _ => 0 };
            if (candidate == 0) continue;
            if (type != 0 && type != candidate) return null;
            type = candidate;
        }
        // Capture before cooperative work; observed role selects only a detached
        // candidate. The actual source recipe and private proof must agree later.
        return type == 0 ? null : remotePlayerProjectiles?.PrepareCraftRemovals(connection.Player.Slot.Value, new(type));
    }

    private sealed class CraftRemovalWork(IEnumerator<int> inner, CraftResolution result) : IEnumerator<int>
    {
        private bool completed;
        private bool disposed;
        public int Current => inner.Current;
        object System.Collections.IEnumerator.Current => Current;
        public bool MoveNext()
        {
            if (disposed) return false;
            bool next = inner.MoveNext();
            completed = !next;
            return next;
        }
        public void Reset() => throw new NotSupportedException();
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { inner.Dispose(); }
            finally
            {
                if (!completed)
                {
                    result.Removals?.Dispose();
                    result.Containers?.Dispose();
                }
            }
        }
    }

    private bool TryAdoptCraftJournal(ConnectionHandle connection, RuntimePlayerMember member,
        PendingCursorMove receipt, PendingShopJournal journal,
        RuntimePlayerInventoryStore.ShopBankMovePreparation payment,
        RuntimeProjectileStore.CraftRemovalPreparation? removals, RemoteCraftCostCertificate? remoteCosts = null)
    {
        if (remoteCosts is not null)
        {
            if (!remoteCosts.IsCurrent || !payment.IsCurrent || !IsCurrentShopActor(member, receipt) ||
                (removals is not null && (!removals.IsCurrentOwned ||
                    (removals.Count > 0 && member.ItemPhase is { Channel: true } phase &&
                     (phase.OwnedChannelExpectedType is not (16 or 34 or 79) || phase.OwnedChannelExpectedType == removals.Type.Value)))) ||
                shopJournalPublishing[connection.Player.Slot.Value] == connection) return false;
            if (!remoteCosts.TryConsumeUnpublished(connection))
                throw new InvalidOperationException("Prepared remote material custody lost currentness before joint adoption.");
            member.CursorMove = null;
        }
        if (removals is null) return TryAdoptBankJournal(connection, member, receipt, journal, payment);
        if (!payment.IsCurrent || !removals.IsCurrentOwned || !IsCurrentShopActor(member, receipt) ||
            shopJournalPublishing[connection.Player.Slot.Value] == connection ||
            // Source Kill cancels only its exact ChannelShotCache match. That cache
            // expected type is certified only by admitted began-use/payment producers.
            // Raw unknown channels and any unresolved matching cache stay refused.
            (removals.Count > 0 && member.ItemPhase is { Channel: true } channelPhase &&
                (channelPhase.OwnedChannelExpectedType is not (16 or 34 or 79) ||
                 channelPhase.OwnedChannelExpectedType == removals.Type.Value))) return false;
        if (!payment.TryAdoptUnpublished() || !removals.TryAdoptUnpublished())
            throw new InvalidOperationException("Prepared craft owners lost currentness during callback-free adoption.");
        member.TryAdvanceRevision();
        MirrorAcceptedBank4Reports(connection, journal);
        Exception? failure = null;
        shopJournalPublishing[connection.Player.Slot.Value] = connection;
        try
        {
            while (true)
            {
                try { if (!removals.TryPublishNext()) break; }
                catch (Exception ex) { failure ??= ex; }
            }
            try { PublishAcceptedBankJournal(connection, member, journal, payment); }
            catch (Exception ex) { failure ??= ex; }
        }
        finally { shopJournalPublishing[connection.Player.Slot.Value] = null; }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return true;
    }
}
