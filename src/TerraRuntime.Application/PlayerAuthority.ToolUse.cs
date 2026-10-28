using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

// Capturing an actor is not a tool-use authorization. The source clock/action
// planner and the owned world context must both admit the attempt before adoption.
internal sealed class RuntimePlayerToolActorCapture1458
{
    private readonly PlayerAuthority owner;
    internal RuntimePlayerProjectileUseCapture Actor { get; }
    internal RuntimePlayerInventoryStore.ShopJournalCapture Inventory { get; }
    internal PlayerBuffState BeforeBuffs { get; }
    internal RuntimePlayerInventoryItem Selected { get; }
    internal long CapturedUpdate { get; }
    internal RuntimePlayerUpdateWorld1458 World { get; }
    internal float PickSpeed => Phase.PermanentSpeeds?.PickSpeed ?? Phase.DerivedCombat!.Value.PickSpeed;
    internal float MeleeAnimationMultiplier => Phase.DerivedCombat!.Value.MeleeAnimationMultiplier;
    private readonly short[] quantities;
    private readonly short[] caps;
    private readonly long[] copper;
    private readonly VanillaUnifiedRandom1458 randomOwner;
    private readonly VanillaUnifiedRandom1458 beforeRandom;

    internal RuntimePlayerToolActorCapture1458(PlayerAuthority owner,
        RuntimePlayerProjectileUseCapture actor,
        RuntimePlayerInventoryStore.ShopJournalCapture inventory,
        PlayerBuffState beforeBuffs,
        RuntimePlayerInventoryItem selected,
        long capturedUpdate,
        VanillaUnifiedRandom1458 randomOwner, RuntimePlayerUpdateWorld1458 world)
    {
        this.owner = owner;
        Actor = actor;
        Inventory = inventory;
        BeforeBuffs = beforeBuffs;
        Selected = selected;
        CapturedUpdate = capturedUpdate;
        World = world;
        this.randomOwner = randomOwner;
        beforeRandom = randomOwner.Clone();
        quantities = inventory.Quantity;
        caps = inventory.Caps;
        copper = inventory.Copper.ToArray();
    }

    internal ConnectionHandle Connection => Actor.Connection;
    internal RuntimePlayerItemPhase1458 Phase => Actor.ItemPhase!.Value;
    internal ReadOnlySpan<RuntimePlayerInventoryItem> InventoryBefore => Inventory.Before;
    internal ReadOnlySpan<short> FundedQuantitiesBefore => quantities;
    internal ReadOnlySpan<short> QuantityCapsBefore => caps;
    internal ReadOnlySpan<long> FundedCopperBefore => copper;
    internal bool IsCurrent => owner.IsCurrentToolActor(this);
    internal bool HasCurrentSourceStage => owner.HasCurrentToolSourceStage(this);
    internal bool IsHeldToolUseAuthorized => owner.IsHeldToolUseAuthorized(this);
    internal VanillaUnifiedRandom1458 CreateRandomPreview() => beforeRandom.Clone();
    internal bool HasPreservedPlacementEndpoint(RuntimePlayerToolActorCapture1458 source, int movements)
        => owner.IsPreservedPlacementEndpoint(source, this, movements);
    internal bool IsRandomDistinctFrom(VanillaUnifiedRandom1458 other) => !ReferenceEquals(randomOwner, other);
    internal bool IsCurrentRandom(VanillaUnifiedRandom1458? current, bool owned) =>
        owned && ReferenceEquals(current, randomOwner) && randomOwner.HasSameState(beforeRandom);
    internal bool MatchesOwnedRandomBefore(VanillaUnifiedRandom1458 current,
        VanillaUnifiedRandom1458 expectedBefore) => ReferenceEquals(current, randomOwner) &&
        beforeRandom.HasSameState(expectedBefore);
}

// The last released frame can have two source-valid, wire-identical UI histories:
// an earlier buffered selection retains the old inactive AnimationMax; a later
// selection request clears it. Keep the complete bounded afterimage beside its
// existing release witness, without replacing canonical clocks or the live RNG.
internal sealed class RuntimePlayerReleasedToolClockAlternative1458
{
    private readonly RuntimePlayerToolUse1458 canonical;
    private readonly RuntimePlayerInventoryStore.ShopJournalCapture inventory;
    private readonly PlayerBuffState buffs;
    private readonly VanillaUnifiedRandom1458 endpoint;
    private readonly RuntimePlayerUpdateWorld1458 world;
    private readonly ConnectionHandle connection;
    private readonly long update;
    internal ToolClockState1458 Clocks { get; }
    internal ToolHitState1458 Hits => canonical.Hits;
    internal ToolHitState1458 ReplacementHits => canonical.ReplacementHits;
    internal int RemoteCrit { get; }
    internal ulong PossibleSelections { get; }

    internal RuntimePlayerReleasedToolClockAlternative1458(RuntimePlayerToolUse1458 canonical,
        ToolClockState1458 alternative, RuntimePlayerInventoryStore.ShopJournalCapture inventory,
        PlayerBuffState buffs, VanillaUnifiedRandom1458 endpoint, RuntimePlayerUpdateWorld1458 world,
        ConnectionHandle connection, long update, int remoteCrit, ulong possibleSelections)
    {
        this.canonical = canonical with { ReleaseWitness = null };
        Clocks = alternative;
        this.inventory = inventory;
        this.buffs = buffs.Clone();
        this.endpoint = endpoint.Clone();
        this.world = world;
        this.connection = connection;
        this.update = update;
        RemoteCrit = remoteCrit;
        PossibleSelections = possibleSelections;
    }

    internal VanillaUnifiedRandom1458 CreateRandomPreview() => endpoint.Clone();

    // A read-only compatible afterimage, qualified to a possible source target.
    // Producers still require their ordinary current stage and wire cohort;
    // no producer adopts these alternative clocks or consumes this method.
    internal bool TryCaptureSourceClocks(RuntimePlayerToolActorCapture1458 capture,
        int tileX, int tileY, out ToolClockState1458 clocks)
    {
        clocks = default;
        if (!capture.IsCurrent || !capture.HasCurrentSourceStage || capture.Connection != connection ||
            capture.CapturedUpdate != update || capture.World != world || !inventory.IsCurrent ||
            !buffs.HasSameSlots(capture.BeforeBuffs) ||
            capture.Phase.Selected.RevolverCritBonus != RemoteCrit ||
            capture.Phase.ToolUse is not { ReleaseWitness: { IsCurrent: true } release } current ||
            !ReferenceEquals(release.BufferedClockAlternative, this) || release.Update != update ||
            (current with { ReleaseWitness = null }) != canonical ||
            !release.Reach.Contains(release.Pose, tileX, tileY) ||
            !capture.CreateRandomPreview().HasSameState(endpoint)) return false;
        clocks = Clocks with { PoundRelease = true };
        return true;
    }
}

internal sealed partial class PlayerAuthority
{
    internal bool IsPreservedPlacementEndpoint(RuntimePlayerToolActorCapture1458 source,
        RuntimePlayerToolActorCapture1458 observed, int movements)
    {
        if (movements is < 1 or > RuntimePlayerToolInput1458.MaximumObservations ||
            !observed.IsCurrent || source.Connection != observed.Connection || source.World != observed.World ||
            source.CapturedUpdate != observed.CapturedUpdate || source.CapturedUpdate != currentCombatTick ||
            source.Phase != observed.Phase ||
            source.Selected != observed.Selected || !source.Inventory.IsCurrent ||
            source.Actor.Player.Revision.Value > ulong.MaxValue - (ulong)movements ||
            source.Actor.InputRevision > ulong.MaxValue - (ulong)movements ||
            observed.Actor.Player.Revision.Value != source.Actor.Player.Revision.Value + (ulong)movements ||
            observed.Actor.InputRevision != source.Actor.InputRevision + (ulong)movements ||
            (observed.Actor.Player.ControlFlags & 32) != 0 ||
            (source.Actor.Player.ControlFlags & ~32) != (observed.Actor.Player.ControlFlags & ~32) ||
            observed.Actor.Player != (source.Actor.Player with { Revision = observed.Actor.Player.Revision,
                ControlFlags = observed.Actor.Player.ControlFlags }) ||
            !source.CreateRandomPreview().HasSameState(observed.CreateRandomPreview()) ||
            playerUpdateRandom is null) return false;
        // Keep EVERY old private participant and rebind only the proven public
        // control/revision successor. This checks equipment/buffs/providers,
        // full219/private funding/caps, physics and world identity against live
        // ownership; equal visible inventory alone cannot preserve the stage.
        var reboundActor = source.Actor with { Player = observed.Actor.Player,
            InputRevision = observed.Actor.InputRevision };
        var rebound = new RuntimePlayerToolActorCapture1458(this, reboundActor, source.Inventory,
            source.BeforeBuffs, source.Selected, source.CapturedUpdate, playerUpdateRandom, source.World);
        return rebound.IsCurrent && observed.IsCurrent;
    }

    internal bool TryCaptureToolActor(ConnectionHandle connection,
        out RuntimePlayerToolActorCapture1458? capture, bool allowDead = false)
    {
        capture = null;
        if (playerUpdateWorld is not { IsValid: true } world ||
            !playerUpdateRandomOwned || playerUpdateRandom is not { } random ||
            !TryCaptureProjectileUse(connection, out var actor) || actor is null ||
            !actor.HasProfile || actor.Buffs is null || (!allowDead && actor.Player.IsDead) ||
            actor.ItemPhase is not { DerivedCombat: { } combat } ||
            !IsValidDerivedCombat(in combat) ||
            !inventory.TryCaptureShopJournal(connection, out var image) || image is null ||
            transferProfiles.CaptureBuffState(connection) is not { } beforeBuffs ||
            !inventory.TryGet(connection, actor.Player.SelectedItem, out var selected) ||
            selected.IsEmpty || !selected.IsCanonical)
            return false;
        var candidate = new RuntimePlayerToolActorCapture1458(this, actor, image,
            beforeBuffs, selected, currentCombatTick, random, world);
        if (!candidate.IsCurrent) return false;
        capture = candidate;
        return true;
    }

    internal bool IsHeldToolUseAuthorized(RuntimePlayerToolActorCapture1458 capture) =>
        capture.IsCurrent && membership.TryGet(capture.Connection, out var member) &&
        member.RemotePhaseSnapshot == capture.Actor.Player && member.RemotePhaseItem == capture.Actor.ItemPhase &&
        member.RemotePhaseInputRevision == capture.Actor.InputRevision &&
        capture.Phase.ToolUse is { HeldProjectile: not null } tools &&
        tools.HeldUseEpoch != 0 && tools.HeldUseEpoch == tools.UseEpoch &&
        tools.HeldItem == capture.Selected.ItemType && tools.HeldPrefix == capture.Selected.Prefix &&
        tools.HeldSlot == capture.Actor.Player.SelectedItem &&
        !tools.RequiresFreshUse && tools.DeferredSelectionSlot is null && tools.LocalClocksKnown;

    internal bool HasCurrentToolSourceStage(RuntimePlayerToolActorCapture1458 capture) =>
        capture.IsCurrent && membership.TryGet(capture.Connection, out var member) &&
        member.RemotePhaseSnapshot == capture.Actor.Player &&
        member.RemotePhaseItem == capture.Actor.ItemPhase &&
        member.RemotePhaseInputRevision == capture.Actor.InputRevision &&
        capture.Phase.ToolUse is { SourceStagePose: not null } tools &&
        tools.LocalClocksKnown && tools.DeferredSelectionSlot is null &&
        tools.SourceItem == capture.Selected.ItemType && tools.SourcePrefix == capture.Selected.Prefix &&
        tools.SourceSlot == capture.Actor.Player.SelectedItem &&
        tools.LastFrameUpdate == capture.CapturedUpdate &&
        tools.BuildingPermission is { IsCurrent: true };

    internal bool IsCurrentToolActor(RuntimePlayerToolActorCapture1458 capture) =>
        capture.IsCurrentRandom(playerUpdateRandom, playerUpdateRandomOwned) &&
        IsCurrentToolActorExceptRandom(capture);

    private bool IsCurrentToolActorExceptRandom(RuntimePlayerToolActorCapture1458 capture) =>
        playerUpdateWorld == capture.World &&
        capture.Inventory.IsCurrent && IsCurrentProjectileUse(capture.Actor) &&
        membership.TryGet(capture.Connection, out var member) &&
        member.PhysicsPhase == capture.Actor.PhysicsPhase &&
        capture.Phase.ToolUse?.BuildingPermission is not { IsCurrent: false } &&
        transferProfiles.IsCurrentBuffState(capture.Connection, capture.BeforeBuffs);
}

// A single-use joint-adoption participant. Only source-derived clock/progress
// results may be staged by the tool planner; no observer is invoked here.
internal sealed partial class PlayerToolAttemptPreparation
{
    private readonly PlayerAuthority owner;
    private readonly RuntimePlayerToolActorCapture1458 capture;
    private RuntimePlayerToolUse1458? next;
    private bool adopted;
    private RuntimePlayerToolActorCapture1458? accepted;
    private readonly VanillaUnifiedRandom1458 plannedRandom;
    private readonly VanillaUnifiedRandom1458 beforeRandom;
    private VanillaUnifiedRandom1458? acceptedRandom;
    private RuntimePlayerToolRelease1458? qualifiedRelease;
    private readonly ulong? observedInputRevision;

    internal PlayerToolAttemptPreparation(PlayerAuthority owner, RuntimePlayerToolActorCapture1458 capture)
    {
        this.owner = owner;
        this.capture = capture;
        beforeRandom = capture.CreateRandomPreview();
        plannedRandom = beforeRandom.Clone();
        observedInputRevision = capture.Phase.ToolUse?.Input?.Revision;
    }

    // Local ItemCheck/HitTile draws remain on the existing player-update stream.
    // Dedicated server WorldGen/drop draws are a separate caller domain.
    internal int NextInteger(int minimum, int maximum)
    {
        if (adopted || next is not null) throw new InvalidOperationException("A staged tool result cannot draw again.");
        return plannedRandom.Next(minimum, maximum);
    }
    internal double NextDouble()
    {
        if (adopted || next is not null) throw new InvalidOperationException("A staged tool result cannot draw again.");
        return plannedRandom.NextDouble();
    }
    internal RuntimePlayerToolActorCapture1458 Capture => capture;
    internal RuntimePlayerToolUse1458 Before => capture.Phase.ToolUse!;
    internal bool IsCurrent => !adopted && capture.IsCurrent &&
        (observedInputRevision is not { } revision ||
            revision != ulong.MaxValue && capture.Phase.ToolUse?.Input is { } input &&
            owner.IsCurrentToolInput(input, revision));
    internal PlayerStateSnapshot? AcceptedPlayer => accepted?.Actor.Player;
    internal RuntimePlayerItemPhase1458? AcceptedPhase => accepted?.Phase;
    internal bool IsAcceptedCurrent => adopted && accepted?.IsCurrent == true;

    internal VanillaUnifiedRandom1458 CreateActionRandomPreview()
    {
        if (adopted || next is not null)
            throw new InvalidOperationException("A staged tool result cannot create another action stream.");
        return plannedRandom.Clone();
    }

    internal bool TryQualifyPoundRelease(int tileX, int tileY, out ToolClockState1458 clocks)
    {
        clocks = Before.Clocks with { PoundRelease = false };
        if (!IsCurrent || !capture.HasCurrentSourceStage || Before.RequiresFreshUse ||
            Before.ReleaseWitness is not { IsCurrent: true } release ||
            Before.SourceStagePose is not { } currentPose || Before.Reach is not { } currentReach ||
            Before.BuildingPermission is not { IsCurrent: true, NoBuilding: false } currentBuilding ||
            release.Connection != capture.Connection || release.Update > capture.CapturedUpdate ||
            Before.SourceDefinition is not { Hammer: > 0 } ||
            release.Slot >= capture.Inventory.Before.Length ||
            !capture.Inventory.Before[release.Slot].IsCanonical ||
            capture.Inventory.Before[release.Slot].ItemType != release.Tool.Item ||
            capture.Inventory.Before[release.Slot].Prefix != release.Tool.Prefix ||
            !ReferenceEquals(release.Building.Owner, currentBuilding.Owner) ||
            release.Building.Revision != currentBuilding.Revision ||
            !release.Reach.Contains(release.Pose, tileX, tileY) ||
            !currentReach.Contains(currentPose, tileX, tileY)) return false;
        qualifiedRelease = release;
        clocks = Before.Clocks with { PoundRelease = true };
        return true;
    }

    internal bool TryStageSourceResult(in ToolClockState1458 clocks,
        ToolHitState1458 hits, bool actionOccurred) =>
        TryStageSourceResult(in clocks, hits, actionOccurred, plannedRandom);

    internal bool TryStageSourceResult(in ToolClockState1458 clocks,
        ToolHitState1458 hits, bool actionOccurred, VanillaUnifiedRandom1458 actionRandom,
        bool consumePoundRelease = false, bool invalidatePoundRelease = false,
        ToolHitState1458? replacementHits = null) =>
        TryStageSourceResultCore(in clocks, hits, actionOccurred, actionRandom,
            consumePoundRelease, invalidatePoundRelease, replacementHits, null);

    private bool TryStageSourceResultCore(in ToolClockState1458 clocks,
        ToolHitState1458 hits, bool actionOccurred, VanillaUnifiedRandom1458 actionRandom,
        bool consumePoundRelease, bool invalidatePoundRelease,
        ToolHitState1458? replacementHits, RuntimePlayerToolInput1458? placementStage)
    {
        if (next is not null || !IsCurrent ||
            !(capture.HasCurrentSourceStage || placementStage is { IsPlacement: true } &&
                placementStage.HasKnownPlacementStage(capture)) ||
            Before.ToolStagePending || actionOccurred && Before.RequiresFreshUse || !clocks.IsValid ||
            Before.Input?.DeferredHealthPublication is not null ||
            hits is null || actionRandom is null ||
            consumePoundRelease && (qualifiedRelease is null || clocks.PoundRelease ||
                !ReferenceEquals(qualifiedRelease, Before.ReleaseWitness)) ||
            actionOccurred && (Before.ActionSerial == ulong.MaxValue ||
                Before.LastActionUpdate == capture.CapturedUpdate)) return false;
        // One detached local caller stream interleaves HitTile and CLIENT
        // WorldGen/frame/dust. Only its validated final state is copied here.
        plannedRandom.CopyStateFrom(actionRandom);
        next = Before with
        {
            Clocks = clocks,
            Hits = hits,
            ReplacementHits = replacementHits ?? Before.ReplacementHits,
            ReleaseWitness = consumePoundRelease || invalidatePoundRelease ? null : Before.ReleaseWitness,
            ActionSerial = actionOccurred ? Before.ActionSerial + 1 : Before.ActionSerial,
            LastActionUpdate = actionOccurred ? capture.CapturedUpdate : Before.LastActionUpdate,
            // A matched source outcome also completes a no-effect/noBuilding cohort.
            Input = null
        };
        return true;
    }

    internal bool TryStagePlacementResult(in LocalPlacementTail1458 tail,
        in RuntimePlayerInventoryItem inventoryAfter, VanillaUnifiedRandom1458 actionRandom,
        ToolHitState1458? hits = null, ToolHitState1458? replacementHits = null)
    {
        if (Before.SourcePlacement is not { } placement || placement.Item != capture.Selected.ItemType ||
            !TryStageSourceResultCore(tail.State, hits ?? Before.Hits, true, actionRandom, false, false, replacementHits, Before.Input) || next is null) return false;
        next = next with
        {
            PlacementHistoryEpoch = Before.UseEpoch,
            PlacementAlternative = null,
            PlacementReady = new(tail.State, Before.LastUseSuccess, Before.UseEpoch),
            PlacementReceipt = new(capture.Connection, capture.Actor.Player.SelectedItem,
                capture.Selected, inventoryAfter, next.ActionSerial)
        };
        return true;
    }

    internal bool TryStageToolBirth(in ToolDefinition1458 tool, ProjectileHandle projectile)
    {
        if (next is not null || !IsCurrent || !capture.HasCurrentSourceStage ||
            Before.RequiresFreshUse || !Before.ShootEligible || !Before.ToolStagePending || !projectile.IsAssigned ||
            tool.Item != capture.Selected.ItemType || tool.Prefix != capture.Selected.Prefix ||
            tool.Shoot <= 0 || Before.UseEpoch == 0 ||
            !VanillaToolUse1458.TryApplyShootTime(in tool, Before.Clocks, out var clocks) ||
            !VanillaToolUse1458.TryEnterToolStage(in tool, in clocks, true, out clocks)) return false;
        next = Before with
        {
            Clocks = clocks,
            ShootEligible = false,
            ToolStagePending = false,
            HeldProjectile = projectile,
            HeldProjectileType = tool.Shoot,
            HeldUseEpoch = Before.UseEpoch,
            HeldItem = tool.Item,
            HeldPrefix = tool.Prefix,
            HeldSlot = capture.Actor.Player.SelectedItem,
            Input = Before.Input is { } observedWorld && (observedWorld.Count > 0 || observedWorld.DeferredHealthPublication is not null)
                ? observedWorld : null
        };
        return true;
    }

    internal bool TryFinalizePlacementInventory(in RuntimePlayerInventoryItem inventoryAfter,
        int consumed)
    {
        if (!adopted || accepted is null || next?.PlacementReceipt is null ||
            !owner.TryCaptureAcceptedPlacementInventory(capture, accepted, inventoryAfter, consumed,
                out var refreshed) || refreshed is null) return false;
        accepted = refreshed;
        return true;
    }

    internal bool TryAdoptUnpublished()
    {
        if (next is null || !IsCurrent || !owner.TryAdoptToolUseUnpublished(capture, next, plannedRandom, out accepted))
            return false;
        acceptedRandom = plannedRandom.Clone();
        adopted = true;
        return true;
    }

    // This proof deliberately does not require our actor pose/vitals to remain
    // current: the same joint transaction may apply source Hurt or displacement.
    // Other actors retain every non-RNG before-image unchanged.
    internal bool TryRebaseOtherToolInputs() => adopted && acceptedRandom is { } after &&
        owner.TryForwardToolRandomLineage(capture.Connection, beforeRandom, after);
}

internal sealed partial class PlayerAuthority
{
    // Endpoint/state proof only. An invisible draw-and-restoration history is not
    // claimed; observable actor/inventory/world writes have their own captures.
    internal bool TryForwardToolRandomLineage(ConnectionHandle completedConnection,
        VanillaUnifiedRandom1458 before, VanillaUnifiedRandom1458 after)
    {
        if (!playerUpdateRandomOwned || playerUpdateRandom is not { } random ||
            !random.HasSameState(after)) return false;
        foreach (var member in membership.Members)
        {
            if (member.Connection == completedConnection ||
                member.ItemPhase?.ToolUse?.Input is not { } input ||
                input.Actor is not { } captured || input.Revision == ulong.MaxValue ||
                !captured.MatchesOwnedRandomBefore(random, before) ||
                !IsCurrentToolActorExceptRandom(captured)) continue;
            var forwarded = new RuntimePlayerToolActorCapture1458(this, captured.Actor,
                captured.Inventory, captured.BeforeBuffs, captured.Selected,
                captured.CapturedUpdate, random, captured.World);
            if (!input.TryRebaseAfterOwnedPhase(captured, input.Revision, forwarded)) return false;
        }
        return true;
    }

    internal bool TryPrepareToolAttempt(ConnectionHandle connection,
        out PlayerToolAttemptPreparation? preparation)
    {
        preparation = null;
        RuntimePlayerToolActorCapture1458? capture;
        if (membership.TryGet(connection, out var member) &&
            member.ItemPhase?.ToolUse?.Input is { } input)
        {
            if (!input.IsCurrent || input.Actor is not { } observed) return false;
            capture = observed;
        }
        else if (!TryCaptureToolActor(connection, out capture)) return false;
        if (capture is null || capture.Phase.ToolUse is not { } tools || !tools.Clocks.IsValid)
            return false;
        preparation = new(this, capture);
        return true;
    }

    internal bool TryAdoptToolUseUnpublished(RuntimePlayerToolActorCapture1458 capture,
        RuntimePlayerToolUse1458 next, VanillaUnifiedRandom1458 plannedRandom,
        out RuntimePlayerToolActorCapture1458? accepted)
    {
        accepted = null;
        if (!capture.IsCurrent || !membership.TryGet(capture.Connection, out var member) ||
            member.ItemPhase is not { } phase || !member.TryAdvanceRevision())
            return false;
        member.ItemPhase = phase with { ToolUse = next };
        playerUpdateRandom!.CopyStateFrom(plannedRandom);
        member.MarkRemotePhaseSnapshot();
        var actorAfter = capture.Actor with
        {
            Player = member.CaptureSnapshot(),
            InputRevision = member.ProjectileUseInputRevision,
            ItemPhase = member.ItemPhase
        };
        accepted = new(this, actorAfter, capture.Inventory, capture.BeforeBuffs,
            capture.Selected, capture.CapturedUpdate, playerUpdateRandom, capture.World);
        if (next.Input is { } input && ReferenceEquals(capture.Phase.ToolUse?.Input, input))
        {
            // The only retained input here follows a source-validated shoot. Its
            // world observations have not been consumed by that earlier stage.
            if (!input.TryRebaseAfterOwnedPhase(capture, input.Revision, accepted))
                throw new InvalidOperationException("Validated tool shoot lost its retained world receipt.");
        }
        return true;
    }
}
