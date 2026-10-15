using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private static readonly bool[] craftResultRoles = BuildCraftRoles(false);
    private static readonly bool[] craftIngredientRoles = BuildCraftRoles(true);
    private static bool[] BuildCraftRoles(bool ingredients)
    {
        var roles = new bool[short.MaxValue + 1];
        for (int index = 0; index < VanillaRecipeCatalog1458.Count; index++)
        {
            if (!VanillaRecipeCatalog1458.TryGet(index, out var recipe)) continue;
            if (!ingredients)
            {
                int type = recipe.ResultType.Value == 5437 ? 5358 : recipe.ResultType.Value;
                if ((uint)type < roles.Length) roles[type] = true;
                continue;
            }
            for (int part = 0; part < VanillaRecipeCatalog1458.GetIngredientCount(index); part++)
            {
                if (!VanillaRecipeCatalog1458.TryGetIngredient(index, part, out var item)) continue;
                if (!item.IsGroup)
                {
                    if ((uint)item.ItemIdOrGroup < roles.Length) roles[item.ItemIdOrGroup] = true;
                }
                else
                    foreach (int type in VanillaRecipeCatalog1458.GetGroupMembers(item.ItemIdOrGroup - 1_000_000))
                        if ((uint)type < roles.Length) roles[type] = true;
            }
        }
        return roles;
    }
    // A material/report role retains an explanation only. It is neither an input
    // command to craft nor permission to renew a historical consumed quantity.
    private bool TryStageLocalCraftCredit(PlayerEquipmentRuntimeCommand command)
    {
        var request = command.Request;
        // A recipe role must not consume an independently current cursor
        // image. Keep the existing pair/return owner on its immediate route.
        if (membership.TryGet(command.Connection, out var primaryMember) &&
            CanPreservePrimaryCraftReport(command, primaryMember)) return false;
        // A normal craft's final mouse image is not the cursor producer's
        // ingredient identity. Retain it before that primary owner can retire
        // the independent recipe explanation. A viable ordinary pair still owns
        // its established immediate path.
        if (request.SlotId is >= 0 and < 59 &&
            membership.TryGet(command.Connection, out var candidateMember) &&
            candidateMember.CursorMove is { Shop: { ObservedBatch: true, IsSealed: false } active } activeReceipt &&
            active.Quote is null && !active.Invalid && active.Capture.IsCurrent &&
            IsCurrentShopActor(candidateMember, activeReceipt) && TryShopReport(in request, out var craftReport) &&
            !craftReport.IsEmpty && craftResultRoles[craftReport.ItemType.Value] &&
            HasRecordedCraftDebit(active) &&
            (active.Capture.Before[request.SlotId].ItemType != craftReport.ItemType ||
                active.Capture.Before[request.SlotId].Stack < craftReport.Stack) &&
            !CanPrepareOrdinaryCursorAlternative(command.Connection, candidateMember, request.SlotId, craftReport))
        {
            active.CraftCandidate = true;
            active.StagedQuantityImage = true;
            if (!active.Observe(request.SlotId, in craftReport))
            { DropBankBatchAlternative(candidateMember, active); RejectedEquipmentUpdates++; }
            return true;
        }
        if (inventory.CanAcceptOwnedQuantityReport(command.Connection, in request) ||
            request.SlotId is < 0 or >= 59 || !request.TryGetCanonicalItemType(out var type) ||
            type.IsNone || !craftResultRoles[type.Value] ||
            !membership.TryGet(command.Connection, out var member) || !HasOwnedPickupContext ||
            member.ItemPhase is null || !TryShopReport(in request, out var reported)) return false;
        var journal = member.CursorMove?.Shop;
        if (journal is null)
        {
            // Keep the existing quote owner until a combined trade/craft matcher
            // compares it. A material role must not steal a live shop alternative.
            if (membership.TryGetTownShopSession(command.Connection.Player, out var shop) && shop?.Quote is not null) return false;
            if (!inventory.TryCaptureShopJournal(command.Connection, out var capture) || capture is null) return false;
            journal = new(capture, null, null)
            {
                ObservedBatch = true,
                ObservedMember = member,
                ObservedActor = member.CaptureSnapshot(),
                ObservedInputRevision = member.ProjectileUseInputRevision,
                CraftCandidate = true,
                StagedQuantityImage = true
            };
            member.CursorMove = member.CursorMove is { } primary
                ? primary with { Shop = journal }
                : new(member.CaptureSnapshot(), member.ItemPhase, member.ProjectileUseInputRevision, null, Shop: journal);
        }
        if (!journal.ObservedBatch || !journal.Capture.IsCurrent || !IsCurrentShopActor(member, member.CursorMove!) ||
            !journal.Observe(request.SlotId, in reported)) return false;
        journal.CraftCandidate = true;
        journal.StagedQuantityImage = true;
        return true;
    }
    private bool CanPreservePrimaryCraftReport(PlayerEquipmentRuntimeCommand command, RuntimePlayerMember member)
    {
        var request = command.Request;
        if (request.PlayerSlot != command.Connection.Player.Slot || !TryShopReport(in request, out var reported)) return false;
        if (member.CursorMove is { } receipt)
        {
            if (request.SlotId == 58 && receipt.Inventory is { } pair && pair.IsCurrent &&
                IsCurrentCursorActor(member, receipt) && pair.TrySelectCursorImage(reported)) return true;
            if (receipt.Return is { } returning && returning.Preparation.IsCurrentForReceipt(receipt))
            {
                var whole = returning.Preparation;
                var slots = whole.ChangedSlots;
                for (int index = 0; index <= returning.NextImage && index < slots.Length; index++)
                    if (request.SlotId == slots[index] &&
                        request == whole.Items.After[request.SlotId].ToCommitRequest(command.Connection.Player.Slot, request.SlotId)) return true;
            }
        }
        return CanPrepareOrdinaryCursorAlternative(command.Connection, member, request.SlotId, reported);
    }

    internal sealed class CraftResolution
    {
        internal CraftEnvironmentCapture? Environment;
        internal RuntimeChestContainerContext1458? ContainerContext;
        internal RuntimePlayerInventoryStore.CraftWitnessResolution Witness = new();
        internal TerraRuntime.Core.Projectiles.RuntimeProjectileStore.CraftRemovalPreparation? Removals;
        internal TerraRuntime.Core.Projectiles.RuntimeProjectileStore.CraftRemovalPreparation? Containers;
    }

    private bool TrySealLocalCraft(ConnectionHandle connection, RuntimePlayerMember member,
        PendingCursorMove receipt, PendingShopJournal journal)
    {
        if (!journal.ObservedBatch || journal.Quote is not null || craftingWorldFacts is null ||
            !HasPotentialLocalCraft(journal)) return false;
        if (journal.Invalid || !journal.Capture.IsCurrent || !IsCurrentShopActor(member, receipt))
        { DropBankBatchAlternative(member, journal); RejectedEquipmentUpdates++; return true; }
        journal.CraftCandidate = true;
        journal.IsSealed = true;
        journal.PlanningAnimation = member.ItemAnimation;
        journal.PlanningItemTime = member.ItemPhase?.Selected.ItemTime ?? 0;
        journal.CraftResult = new()
        {
            Removals = PrepareCraftProjectileRemovals(connection, journal),
            Containers = journal.Capture.Before.Skip(59).Any(static item => !item.IsEmpty)
                ? remotePlayerProjectiles?.PrepareCraftRemovals(connection.Player.Slot.Value, new(0)) : null
        };
        // Capture the last observed scalar UI role at seal, after any source33
        // in this batch. It does not certify an unobserved closed UI.
        if (chestCommands?.TryCaptureCraftContainerContext(connection, out var context) == true)
            journal.CraftResult.ContainerContext = context;
        var work = ResolveLocalCraftJournal(connection, journal, journal.CraftResult).GetEnumerator();
        journal.Work = new CraftRemovalWork(work, journal.CraftResult);
        return true;
    }

    private IEnumerable<int> ResolveLocalCraftJournal(ConnectionHandle connection, PendingShopJournal journal, CraftResolution result)
    {
        var environment = new CraftEnvironmentResolution();
        using (var work = PrepareCraftEnvironment(connection, journal, result.Containers, environment).GetEnumerator())
            while (work.MoveNext()) yield return 0;
        if (environment.Capture is not { } captured || !captured.IsCurrent) yield break;
        result.Environment = captured;
        bool voidBag = journal.Capture.Before.Take(58).Any(static item => item.ItemType.Value == 4131 && item.Stack > 0);
        var orders = new List<byte[]>();
        var currentCandidates = captured.NearbyBanks.Select(static bank => (int)bank).Prepend(-1).ToList();
        if (result.ContainerContext is { Observed: true, ChestId: >= -5 and <= -2 } container)
        {
            int observedBank = -container.ChestId - 2;
            if (!currentCandidates.Contains(observedBank)) currentCandidates.Add(observedBank);
        }
        foreach (int current in currentCandidates)
            for (int setting = 0; setting < 2; setting++)
            {
                var order = new List<byte>(4);
                if (current >= 0) order.Add((byte)current);
                if (voidBag && !order.Contains(3)) order.Add(3);
                if (setting != 0)
                    foreach (byte bank in captured.NearbyBanks) if (!order.Contains(bank)) order.Add(bank);
                byte[] candidate = order.ToArray();
                if (!orders.Any(existing => existing.AsSpan().SequenceEqual(candidate))) orders.Add(candidate);
            }
        using var witness = inventory.PrepareCraftWitnessWork(connection, journal.Capture, journal.Reports,
            orders.ToArray(), captured.Stations, captured.Environment, receivePrefixWorld, result.Witness,
            journal.RemoteCosts?.CaptureMaterials()).GetEnumerator();
        while (witness.MoveNext()) yield return 0;
        if (result.Witness.Preparation is null || result.Witness.KillOwnedProjectileType == 0) yield break;
        if (result.Removals is null || result.Removals.Type.Value != result.Witness.KillOwnedProjectileType)
        { result.Witness.Preparation = null; yield break; }
        bool prepared = false;
        try
        {
            while (result.Removals.MoveNext()) yield return 0;
            prepared = true;
        }
        finally
        {
            if (!prepared) result.Removals.Dispose();
        }

    }

    private void CompleteLocalCraftJournal(ConnectionHandle connection, RuntimePlayerMember member,
        PendingCursorMove receipt, PendingShopJournal journal, CraftResolution result)
    {
        try
        {
        var accepted = result.Witness.Preparation;
        // The existing proof covers full before/after funding, or conservative
        // currency relocation with a unique final private allocation. Partial
        // material histories require transformed alternative comparison first.
        if (result.Witness.Ambiguous || result.Witness.HasUnconfirmedLottery || accepted is null ||
            (!accepted.CanElideConservativeBankAlternatives() && !result.Witness.UniqueReducedAllocation) || result.Environment?.IsCurrent != true ||
            (result.ContainerContext is { } context && chestCommands?.IsCurrentCraftContainerContext(in context) != true) ||
            !journal.Capture.IsCurrent || !IsCurrentShopActor(member, receipt) ||
            member.Revision == ulong.MaxValue || member.ProjectileUseInputRevision == ulong.MaxValue ||
            (journal.RemoteCosts is { } remote && (!remote.IsCurrent || !result.Witness.ConsumesAllRemoteCosts)))
        { DropBankBatchAlternative(member, journal); RejectedEquipmentUpdates++; return; }
        if (journal.RemoteCosts is null) member.CursorMove = null;
        if (TryAdoptCraftJournal(connection, member, receipt, journal, accepted, result.Removals, journal.RemoteCosts)) return;
        member.CursorMove = receipt;
        DropBankBatchAlternative(member, journal);
        RejectedEquipmentUpdates++;
        }
        finally
        {
            result.Removals?.Dispose();
            result.Containers?.Dispose();
        }
    }

    private static bool HasRecordedCraftDebit(PendingShopJournal journal)
    {
        if (journal.RemoteCosts is { } costs && costs.Materials.Length > 0) return true;
        // A completed ordinary pair relocates its ingredient identity to the
        // cursor. Its old donor debit alone is not a remaining craft cost.
        var remaining = new Dictionary<(int Type, int Prefix), int>();
        for (int slot = 0; slot < 219; slot++)
        {
            var before = journal.Capture.Before[slot];
            var after = journal.Reports[slot];
            if (!before.IsEmpty && craftIngredientRoles[before.ItemType.Value])
            {
                var key = (before.ItemType.Value, before.Prefix.Value);
                remaining[key] = remaining.GetValueOrDefault(key) + before.Stack;
            }
            if (!after.IsEmpty && craftIngredientRoles[after.ItemType.Value])
            {
                var key = (after.ItemType.Value, after.Prefix.Value);
                remaining[key] = remaining.GetValueOrDefault(key) - after.Stack;
            }
        }
        return remaining.Values.Any(static amount => amount > 0);
    }

    private static bool HasPotentialLocalCraft(PendingShopJournal journal)
    {
        var totals = new Dictionary<(int Type, int Prefix), int>();
        bool debit = journal.RemoteCosts is { } costs && costs.Materials.Length > 0;
        for (int slot = 0; slot < 219; slot++)
        {
            var before = journal.Capture.Before[slot];
            var after = journal.Reports[slot];
            if (!before.IsEmpty)
            {
                var key = (before.ItemType.Value, before.Prefix.Value);
                totals[key] = totals.GetValueOrDefault(key) - before.Stack;
            }
            if (!after.IsEmpty)
            {
                var key = (after.ItemType.Value, after.Prefix.Value);
                totals[key] = totals.GetValueOrDefault(key) + after.Stack;
            }
            if (journal.Seen[slot] && !before.IsEmpty && craftIngredientRoles[before.ItemType.Value] &&
                (after.IsEmpty || after.ItemType != before.ItemType || after.Prefix != before.Prefix || after.Stack < before.Stack)) debit = true;
        }
        return debit && totals.Any(pair => pair.Value > 0 && craftResultRoles[pair.Key.Type]);
    }
}
