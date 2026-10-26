using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

// A packet16 baseline already adopted by the established receiver. This is NOT
// a claimed tool-Hurt result. A subsequent canonical Hurt may make it stale.
internal sealed class ToolHealthPublication1458(PlayerAuthority owner, ConnectionHandle connection,
    PlayerHealthCommitRequest request, RuntimePlayerMember expectedMember)
{
    private bool published;
    internal bool TryPublish()
    {
        if (published) return false;
        published = true;
        return owner.TryPublishToolBaselineHealth(connection, in request, expectedMember);
    }
}

internal sealed partial class RuntimePlayerToolInput1458
{
    // Same bounded cohort as27/17. A newer already-adopted baseline supersedes
    // the old unpublished status; it grants no damage, use, RNG or hit credit.
    internal ToolHealthPublication1458? DeferredHealthPublication { get; private set; }

    internal bool TryRebaseAfterKnownHealth(RuntimePlayerToolActorCapture1458 expected,
        ulong expectedRevision, RuntimePlayerToolActorCapture1458 accepted,
        ToolHealthPublication1458 publication)
    {
        if (Invalid || !ReferenceEquals(actor, expected) || Revision != expectedRevision ||
            Revision == ulong.MaxValue || accepted.Connection != expected.Connection ||
            !ReferenceEquals(accepted.Phase.ToolUse?.Input, this) || !accepted.IsCurrent) return false;
        actor = accepted;
        Revision++;
        DeferredHealthPublication = publication;
        return true;
    }

    internal ToolHealthPublication1458? TakeHealthPublication()
    {
        var result = DeferredHealthPublication;
        DeferredHealthPublication = null;
        if (result is not null)
        {
            if (Revision == ulong.MaxValue) Invalid = true;
            else Revision++;
        }
        return result;
    }
}

internal sealed partial class PlayerAuthority
{
    private bool TryRetainToolHealth(PlayerHealthRuntimeCommand command,
        in PlayerHealthCommitRequest request, RuntimePlayerMember member)
    {
        if (!inventory.TryGet(command.Connection, member.SelectedItem, out var selected) ||
            !selected.IsCanonical || selected.IsEmpty ||
            !VanillaToolCatalog1458.TryGet(selected.ItemType, out _)) return false;
        var input = member.ItemPhase?.ToolUse?.Input;
        var before = input is { IsCurrent: true } ? input.Actor : null;
        if (before is null)
        {
            if (!TryCaptureToolActor(command.Connection, out before) || before is null ||
                !before.HasCurrentSourceStage) return false;
        }

        // Source SendPlayerStatus precedes Hurt. An unchanged status is an ACK,
        // never a revision/phase invalidation or an early callback before held27.
        if (member.HasHealth && member.NpcLifeCurrent == true && request.Life == member.Life &&
            member.NpcHealth?.Life == request.Life && member.MaxLife == request.MaxLife &&
            member.BaseLifeMax == request.MaxLife)
        {
            AppliedHealthUpdates++;
            return true;
        }
        if (input is null || !input.IsCurrent || !ReferenceEquals(input.Actor, before) ||
            input.Revision >= ulong.MaxValue - 1 || member.Revision >= ulong.MaxValue - 1 ||
            member.ProjectileUseInputRevision >= ulong.MaxValue - 1 || member.ItemPhase is not { } phase)
            return false;
        ulong revision = input.Revision;
        var oldPlayer = before.Actor.Player;
        var expectedPhase = phase with
        {
            ToolUse = phase.ToolUse! with { DeferredHealthPublication = null },
            Selected = request.Life <= 0 ? phase.Selected with { Animation = 0 } : phase.Selected
        };
        var expectedPlayer = oldPlayer with
        {
            Revision = new(oldPlayer.Revision.Value + 1), HasHealth = true,
            Life = request.Life, MaxLife = request.MaxLife, BaseLifeMax = request.MaxLife,
            NpcHealth = oldPlayer.NpcHealth is { } oldHealth ? oldHealth with { Life = request.Life }
                : new(request.Life, null, null),
            NpcLifeCurrent = true, IsDead = request.Life <= 0,
            ItemAnimation = request.Life <= 0 ? 0 : oldPlayer.ItemAnimation
        };
        // Full actor/inventory/buff/RNG currentness was captured above. This exact
        // existing receiver is callback-free here and changes ONLY these HP fields
        // plus its one public/input revision. No arbitrary resnapshot is allowed.
        if (!before.IsCurrent || !ReferenceEquals(member.ItemPhase?.ToolUse?.Input, input)) return false;
        var publication = ApplyPlayerHealthCore(command, allowToolRetention: false,
            deferPublication: true, preserveToolInput: true);
        if (publication is null) return true; // Existing receiver rejected; no authority fallback.
        var knownActor = before.Actor with
        {
            Player = expectedPlayer, InputRevision = before.Actor.InputRevision + 1,
            ItemPhase = expectedPhase
        };
        var accepted = new RuntimePlayerToolActorCapture1458(this, knownActor, before.Inventory,
            before.BeforeBuffs, before.Selected, before.CapturedUpdate, playerUpdateRandom!, before.World);
        if (member.CaptureSnapshot() != expectedPlayer || member.ItemPhase != expectedPhase ||
            !accepted.IsCurrent || !input.TryRebaseAfterKnownHealth(before, revision, accepted, publication))
            throw new InvalidOperationException("Validated health baseline lost its exact tool input afterimage.");
        // Source-stage currentness intentionally remains invalidated until the
        // normal next certified phase. HP receipt is not another Player.Update.
        return true;
    }

    // Collect AFTER callback-free held births, BEFORE world action preparation.
    // Root publishes birth tokens first; a canonical later Hurt suppresses an
    // obsolete baseline status through the token's current-health predicate.
    internal void CollectToolBaselineHealthPublications(List<ToolHealthPublication1458> publications)
    {
        if (publications is null || publications.Count > MaxPlayerSlots) throw new ArgumentOutOfRangeException(nameof(publications));
        foreach (var member in membership.Members.ToArray())
        {
            if (member.ItemPhase is not { ToolUse: { } tools } phase) continue;
            var input = tools.Input;
            if (tools.DeferredHealthPublication is null && input?.DeferredHealthPublication is null) continue;
            if (publications.Count == MaxPlayerSlots) throw new InvalidOperationException("Tool health status batch is bounded by players.");
            var priorActor = input is { IsCurrent: true } ? input.Actor : null;
            ulong priorRevision = input?.Revision ?? 0;
            var publication = input?.TakeHealthPublication() ?? tools.DeferredHealthPublication;
            if (publication is null) continue;
            publications.Add(publication);
            // There is ONE latest status owner. A prior baseline is superseded
            // only by an actual later legacy baseline, never a raw declaration.
            bool removeInput = input is { Count: 0, SquareReport: null, FurnitureSquare: null } &&
                (input.ShootReport is null || input.ShootReport is { } report &&
                    tools.HeldProjectile is { } born && toolProjectiles is not null &&
                    toolProjectiles.HasAdoptedToolBirth(member.Connection, in report, born));
            var after = phase with { ToolUse = tools with
            { DeferredHealthPublication = null, Input = removeInput ? null : input } };
            if (after == phase) continue;
            bool known = member.RemotePhaseSnapshot == member.CaptureSnapshot() && member.RemotePhaseItem == phase &&
                member.RemotePhaseInputRevision == member.ProjectileUseInputRevision;
            member.ItemPhase = after;
            if (known) member.MarkRemotePhaseSnapshot();
            if (!removeInput && input is not null && priorActor is not null)
            {
                var nextActor = priorActor.Actor with { ItemPhase = after };
                var accepted = new RuntimePlayerToolActorCapture1458(this, nextActor, priorActor.Inventory,
                    priorActor.BeforeBuffs, priorActor.Selected, priorActor.CapturedUpdate, playerUpdateRandom!, priorActor.World);
                // TakeHealthPublication incremented the receipt revision itself.
                ulong actualRevision = input.Revision;
                if (actualRevision < priorRevision || !accepted.IsCurrent ||
                    !input.TryRebaseAfterOwnedPhase(priorActor, actualRevision, accepted))
                    throw new InvalidOperationException("Tool health publication lost its exact metadata afterimage.");
            }
        }
    }

    internal void PublishCollectedToolBaselineHealth()
    {
        var publications = new List<ToolHealthPublication1458>();
        CollectToolBaselineHealthPublications(publications);
        PublishToolBaselineHealthBatch(publications);
    }

    internal bool TryPublishToolBaselineHealth(ConnectionHandle connection, in PlayerHealthCommitRequest request,
        RuntimePlayerMember expectedMember)
    {
        if (!membership.TryGet(connection, out var member) || !ReferenceEquals(member, expectedMember) || !member.HasHealth ||
            member.Life != request.Life || member.NpcHealth?.Life != request.Life ||
            member.MaxLife != request.MaxLife || member.BaseLifeMax != request.MaxLife) return false;
        events?.PlayerHealthUpdated(connection, in request);
        return true;
    }

    private static void PublishToolBaselineHealthBatch(List<ToolHealthPublication1458> publications)
    {
        Exception? first = null;
        foreach (var publication in publications)
        {
            try { publication.TryPublish(); }
            catch (Exception error) { first ??= error; }
        }
        if (first is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
    }

    private static void RetireToolHealthBeforeLegacyApply(RuntimePlayerMember member,
        in PlayerHealthCommitRequest request)
    {
        if (member.ItemPhase is not { ToolUse: { } tools } phase) return;
        member.ItemPhase = phase with { ToolUse = tools with { Input = null, DeferredHealthPublication = null } };
    }
}
