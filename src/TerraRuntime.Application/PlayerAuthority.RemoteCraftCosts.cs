using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // Removed trusted world stock, retained on the existing whole-operation
    // journal. It is a cost witness only; module12 carries no recipe/result.
    internal sealed class RemoteCraftCostCertificate(PlayerAuthority owner, RuntimePlayerMember member,
        PendingShopJournal journal, RuntimeChestStore.CraftingPreparation.FundedMaterialLeg[] materials)
    {
        private bool consumed;
        internal ReadOnlySpan<RuntimeChestStore.CraftingPreparation.FundedMaterialLeg> Materials => materials;
        internal RuntimePlayerInventoryStore.CraftExternalMaterial[] CaptureMaterials()
        {
            var result = new RuntimePlayerInventoryStore.CraftExternalMaterial[materials.Length];
            for (int i = 0; i < result.Length; i++)
            {
                var material = materials[i];
                result[i] = new(new(material.Before.ItemType, material.Amount, material.Before.Prefix, false),
                    material.FundedQuantity, material.FundedCopper);
            }
            return result;
        }
        internal bool IsCurrent => !consumed && member.CursorMove is { } receipt && ReferenceEquals(receipt.Shop, journal) &&
            owner.IsCurrentShopActor(member, receipt) &&
            ReferenceEquals(journal.RemoteCosts, this) && journal.Capture.IsCurrent &&
            owner.membership.TryGet(member.Connection, out var current) && ReferenceEquals(current, member);
        internal bool TryConsumeUnpublished(ConnectionHandle connection)
        {
            if (connection != member.Connection || !IsCurrent) return false;
            consumed = true;
            return true;
        }
    }

    internal RemoteCraftCostCapture? CaptureRemoteCraftCosts(ConnectionHandle connection)
    {
        if (!HasOwnedPickupContext || !membership.TryGet(connection, out var member) ||
            member.IsDead || member.ItemPhase is null || shopJournalPublishing[connection.Player.Slot.Value] == connection)
            return null;
        var receipt = member.CursorMove;
        var journal = receipt?.Shop;
        if (journal is null)
        {
            if (receipt is not null || !inventory.TryCaptureShopJournal(connection, out var capture) || capture is null) return null;
            journal = new(capture, null, null)
            {
                ObservedBatch = true, ObservedMember = member,
                ObservedActor = member.CaptureSnapshot(), ObservedInputRevision = member.ProjectileUseInputRevision
            };
            receipt = new(member.CaptureSnapshot(), member.ItemPhase, member.ProjectileUseInputRevision, null, Shop: journal);
            member.CursorMove = receipt;
        }
        if (journal.IsSealed || journal.RemoteCostWorkPending || journal.WorldChest is not null || !journal.Capture.IsCurrent ||
            !IsCurrentShopActor(member, receipt!) || journal.RemoteCosts is { IsCurrent: false }) return null;
        journal.RemoteCraftSegments = true;
        journal.RemoteCostWorkPending = true;
        return new(this, member, receipt!, journal, journal.RemoteCosts);
    }

    private bool TryRetainRemoteCraftBoundary(ConnectionHandle connection)
    {
        if (!membership.TryGet(connection, out var member) ||
            member.CursorMove is not { Shop: { RemoteCraftSegments: true, IsSealed: false } journal } receipt)
            return false;
        if (journal.Invalid || !journal.Capture.IsCurrent || !IsCurrentShopActor(member, receipt) ||
            journal.RemoteCosts is { IsCurrent: false })
        {
            DropBankBatchAlternative(member, journal);
            RejectedEquipmentUpdates++;
            return true;
        }
        bool resultObserved = false;
        for (int index = 0; index < journal.Reports.Length; index++)
        {
            var before = journal.Capture.Before[index];
            var after = journal.Reports[index];
            if (journal.Seen[index] && !after.IsEmpty && craftResultRoles[after.ItemType.Value] &&
                (before.ItemType != after.ItemType || before.Stack < after.Stack))
            { resultObserved = true; break; }
        }
        if (!journal.RemoteCostWorkPending && (journal.RemoteCosts is null || resultObserved)) return false;
        // Packet138 closes only this source synchronization segment while the
        // original client still has a pending request. Keep cumulative reports
        // and custody; restart only per-segment ordering/duplicate checks.
        journal.LastIndex = -1;
        Array.Clear(journal.RemoteEpochSeen);
        return true;
    }

    internal sealed class RemoteCraftCostCapture(PlayerAuthority owner, RuntimePlayerMember member,
        PendingCursorMove receipt, PendingShopJournal journal, RemoteCraftCostCertificate? prior)
    {
        internal bool IsCurrent => owner.membership.TryGet(member.Connection, out var current) &&
            ReferenceEquals(current, member) && ReferenceEquals(receipt.Shop, journal) &&
            member.CursorMove is { } currentReceipt && ReferenceEquals(currentReceipt.Shop, journal) &&
            !journal.IsSealed && journal.RemoteCostWorkPending && journal.Capture.IsCurrent && owner.IsCurrentShopActor(member, currentReceipt) &&
            ReferenceEquals(journal.RemoteCosts, prior) && (prior is null || prior.IsCurrent);

        internal void FinishUnpublished()
        {
            if (owner.membership.TryGet(member.Connection, out var current) && ReferenceEquals(current, member) &&
                ReferenceEquals(member.CursorMove?.Shop, journal)) journal.RemoteCostWorkPending = false;
        }

        internal bool TryPrepareCommit(RuntimeChestStore.CraftingPreparation preparation, out RemoteCraftCostCommit? commit)
        {
            commit = null;
            if (!IsCurrent || !preparation.TryCaptureFundedMaterialLegs(out var added)) return false;
            int oldCount = prior?.Materials.Length ?? 0;
            if (added.Length == 0 || oldCount + added.Length > 255) return false;
            var all = new RuntimeChestStore.CraftingPreparation.FundedMaterialLeg[oldCount + added.Length];
            if (prior is not null) prior.Materials.CopyTo(all);
            added.CopyTo(all, oldCount);
            commit = new(this, journal, new(owner, member, journal, all));
            return true;
        }
    }

    internal sealed class RemoteCraftCostCommit(RemoteCraftCostCapture capture, PendingShopJournal journal,
        RemoteCraftCostCertificate certificate)
    {
        internal bool IsCurrent => capture.IsCurrent;
        // Called only in the same validated callback-free commit span after the
        // store consumes its trusted stock. No refund/transport guarantee is made.
        internal void AdoptValidatedUnpublished() => journal.RemoteCosts = certificate;
    }
}
