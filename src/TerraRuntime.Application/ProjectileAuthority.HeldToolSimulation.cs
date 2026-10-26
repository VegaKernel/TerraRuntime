using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Players;
using System.Runtime.ExceptionServices;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    private bool activeToolAwareTick;

    // Exact birth lineage, not a raw player-slot lookup. A disconnected or
    // transferred player's retained member must never attach to its replacement.
    private readonly ToolActorOwner1458?[] toolActorOwners =
        new ToolActorOwner1458?[RuntimeProjectileStore.MaximumProtocolAddressableCapacity];

    private WorldTileAuthority? toolActorWorld;

    internal void BindToolActorWorld(WorldTileAuthority world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!ReferenceEquals(world.ToolPlayerOwner, players) ||
            !ReferenceEquals(world.ToolProjectileOwner, projectiles) ||
            toolActorWorld is not null && !ReferenceEquals(toolActorWorld, world))
            throw new InvalidOperationException("A tool actor cannot cross world owners.");
        toolActorWorld = world;
    }

    private ProjectileActorSimulationResult TrySimulateHeldToolActor(ProjectileSnapshot initial)
    {
        bool heldFamily = VanillaHeldToolProjectile1458.IsSupported(initial.Type);
        if (!heldFamily && initial.Type.Value != VanillaThrownAxeProjectile1458.ProjectileType)
            return ProjectileActorSimulationResult.NotApplicable;
        if (initial.Handle.Slot >= toolActorOwners.Length ||
            toolActorOwners[initial.Handle.Slot] is not { } binding || binding.Projectile != initial.Handle ||
            initial.Spawner != binding.Player.Slot.Value ||
            worldTiles is null || stepper is not VanillaProjectileWorldStateStepper concrete ||
            players.MagicItemWindowsArithmetic is not { } windows ||
            !projectiles.TryGet(initial.Handle, out var observed) || observed != initial ||
            !projectiles.TryGetLifecycle(initial.Handle, out var lifecycle) || lifecycle.TimeLeft <= 0 || lifecycle.Reflected ||
            !projectiles.TryGetCombatTrustedOwner(initial.Handle, out var trusted) || trusted != binding.Player ||
            !projectiles.TryGetSourceDirection(initial.Handle.Slot, out int retainedDirection) ||
            !VanillaToolProjectileDefinition1458.TryGet(initial.Type, out var definition) ||
            heldFamily && lifecycle.HeldTool is null || !heldFamily && lifecycle.ThrownAxe is null)
            return ProjectileActorSimulationResult.Rejected;

        bool bounds = heldFamily && (initial.PositionX <= 0f || initial.PositionY <= 0f ||
            initial.PositionX + definition.Width >= worldTiles.Dimensions.WidthTiles * 16f ||
            initial.PositionY + definition.Height >= worldTiles.Dimensions.HeightTiles * 16f);
        if (bounds)
        {
            var ending = lifecycle with { TimeLeft = 0 };
            var unchanged = ToolUpdate(initial, initial.PositionX, initial.PositionY,
                initial.VelocityX, initial.VelocityY, initial.Ai);
            if (!projectiles.TryPrepareSimulation(in initial, out var outside) || outside is null ||
                !outside.TryStageMotion(in unchanged, in ending) || !outside.TryQueueToolDespawn() ||
                !outside.IsCurrent || !outside.TryAdoptUnpublished(out _, out bool expired) || !expired)
                return ProjectileActorSimulationResult.Rejected;
            if (ReferenceEquals(toolActorOwners[initial.Handle.Slot], binding))
            {
                toolActorOwners[initial.Handle.Slot] = null;
                toolControlledInputs[initial.Handle.Slot] = null;
                toolStopInputs[initial.Handle.Slot] = null;
            }
            if (replication is not null) replication.TryPublishToolActor(outside, false, true);
            else while (outside.TryPublishNext()) { }
            return ProjectileActorSimulationResult.Applied;
        }
        if (!players.TryPrepareHeldToolLifecycle(binding.Member, binding.Player,
                binding.LastSelectedPick, out var owner) || owner is null || owner.Before.HasMount)
            return ProjectileActorSimulationResult.Rejected;
        // The admitted body is the same unrotated20x42 pose used by qualified
        // birth. No raw mouse coordinate is reconstructed from13,17 or27.
        float mountedX = owner.Before.PositionX + 10f;
        float mountedY = owner.Before.PositionY + 21f;
        bool eligibleChannel = owner.HasCurrentPhase && owner.Phase?.ToolUse is { } tools &&
            tools.HeldProjectile == initial.Handle && tools.HeldProjectileType == initial.Type.Value &&
            !tools.RequiresFreshUse && tools.LocalClocksKnown && tools.DeferredSelectionSlot is null &&
            tools.HeldUseEpoch == binding.UseEpoch && tools.HeldItem == binding.Item &&
            tools.HeldPrefix == binding.Prefix && tools.HeldSlot == binding.SelectedSlot &&
            tools.SourceItem == binding.Item && tools.SourcePrefix == binding.Prefix &&
            tools.SourceSlot == binding.SelectedSlot && tools.UseEpoch == binding.UseEpoch &&
            owner.Before.SelectedItem == binding.SelectedSlot && owner.Selected.ItemType == binding.Item &&
            owner.Selected.Prefix == binding.Prefix && tools.Clocks.Channel;
        bool stopRequested = ReferenceEquals(toolStopInputs[initial.Handle.Slot], binding);
        eligibleChannel &= !stopRequested;
        var randomBefore = projectileRandom.Clone();
        var random = randomBefore.Clone();
        var nextLifecycle = lifecycle with { OldVelocityX = initial.VelocityX, OldVelocityY = initial.VelocityY };
        bool earlyKill = false, sourceNetUpdate = false;
        ToolControlledInput1458? retainedInput = toolControlledInputs[initial.Handle.Slot];
        ProjectileKillOrigin? killOrigin = null;
        float x, y, vx, vy;
        ProjectileAiState ai = initial.Ai;
        HeldToolProjectileState1458 held = default;
        ThrownAxeState1458 axe = default;
        float meleeSpeed = owner.Phase?.DerivedCombat?.MeleeAnimationMultiplier ?? binding.LastMeleeSpeed;
        if (heldFamily)
        {
            held = lifecycle.HeldTool!.Value with
            {
                X = initial.PositionX, Y = initial.PositionY,
                VelocityX = initial.VelocityX, VelocityY = initial.VelocityY,
                Direction = retainedDirection, SoundDelay = Math.Max(0, lifecycle.HeldTool.Value.SoundDelay - 1),
                NetUpdate = false
            };
            var sourceOwner = new HeldToolOwnerFacts1458(mountedX, mountedY, false, true,
                owner.NoItems, owner.CrowdControlled, owner.SelectedPick, 0f, 0f, 0f,
                false, 0, 0, 0, 0, 0, windows);
            // Input never overwrites the trusted tuple. Only a proved local-AI
            // preimage enables the owned source branch; RNG remains our clone.
            // AI75 reach is supplied by the owned player reach capture below.
            HeldToolOwnerFacts1458 provedAim = default;
            bool controlled = eligibleChannel && retainedInput is not null &&
                TryCaptureToolControlledReach(binding, owner, initial.Type, out var reach) &&
                TryPrepareToolControlledAim(binding, owner, in initial, in held, windows,
                    reach.TileRangeX, reach.TileRangeY, reach.TileBoost, out provedAim, out _);
            if (controlled) sourceOwner = provedAim;
            if (initial.Ai != default ||
                !VanillaHeldToolProjectile1458.TryStepAi(initial.Type, definition.Width, definition.Height,
                    in held, in sourceOwner, random.Next,
                    out var transition) ||
                !owner.TryStageDummyOwner(transition.OwnerDirection, transition.OwnerItemRotation))
                return ProjectileActorSimulationResult.Rejected;
            held = transition.State;
            sourceNetUpdate = controlled && held.NetUpdate;
            bool localCancel = !eligibleChannel || initial.Type.Value == 445 && (owner.NoItems || owner.CrowdControlled);
            if (localCancel)
            {
                earlyKill = true;
                killOrigin = new(initial.PositionX, initial.PositionY);
                held = held with { Active = false, TimeLeft = initial.Type.Value == 445 ? 2 : 0 };
            }
            // Generic owner-inactive Kill follows AI, including its Dummy2 writer.
            if (owner.Inactive) { earlyKill = true; held = held with { Active = false, TimeLeft = 0 }; }
            x = held.X; y = held.Y; vx = held.VelocityX; vy = held.VelocityY;
        }
        else
        {
            if (!owner.Inactive && owner.Phase?.DerivedCombat is null)
                return ProjectileActorSimulationResult.Rejected;
            axe = lifecycle.ThrownAxe!.Value with
            {
                X = initial.PositionX, Y = initial.PositionY,
                VelocityX = initial.VelocityX, VelocityY = initial.VelocityY,
                Direction = retainedDirection, SoundDelay = Math.Max(0, lifecycle.ThrownAxe.Value.SoundDelay - 1),
                NetUpdate = false
            };
            if (initial.Ai != new ProjectileAiState(axe.Ai0, axe.Ai1, 0f) ||
                !VanillaThrownAxeProjectile1458.TryStepAi(in axe,
                    new(owner.Before.PositionX, owner.Before.PositionY, 20, 42, true,
                        !owner.Inactive, meleeSpeed, windows), out var transition))
                return ProjectileActorSimulationResult.Rejected;
            axe = transition.State;
            earlyKill = transition.SourceKill || owner.Inactive;
            if (transition.SourceKill) killOrigin = new(initial.PositionX, initial.PositionY);
            if (owner.Inactive) axe = axe with { Active = false, TimeLeft = 0 };
            sourceNetUpdate = axe.NetUpdate;
            x = axe.X; y = axe.Y; vx = axe.VelocityX; vy = axe.VelocityY;
            ai = new(axe.Ai0, axe.Ai1, 0f);
        }
        WorldTileAuthority.ToolProjectileCutsPreparation1458? cuts = null;
        if (!earlyKill && (toolActorWorld is null ||
                !toolActorWorld.TryBeginToolProjectileCuts(owner, in initial, projectileRandom,
                    randomBefore, random, windows, out cuts) || cuts is null ||
                !cuts.TryRecordTerrainReads(initial.Type, x, y, vx, vy)))
            return ProjectileActorSimulationResult.Rejected;
        using (cuts)
        {
        bool sourceTileCollide = !heldFamily && axe.TileCollide;
        if (!concrete.TryCaptureToolTerrain(initial.Type, x, y, vx, vy,
                sourceTileCollide, lifecycle.Liquid, lifecycle.SourceWetCount, out var terrain))
            return ProjectileActorSimulationResult.Rejected;
        // Update captures wetVelocity before AI. Only HandleMovement's collision
        // branch replaces it. Noncolliding held/return bodies therefore retain
        // the pre-AI vector, including AI20's independent velocity jitter.
        if (terrain.Liquid.Wet && !sourceTileCollide)
            terrain = terrain with { WetVelocityX = initial.VelocityX, WetVelocityY = initial.VelocityY };
        // Dedicated Dust allocation is suppressed, but splash colour arguments
        // still draw Main before HandleMovement/WorldGen's collision branches.
        for (int draw = 0; draw < terrain.ShimmerColourDraws; draw++) random.Next(6);
        vx = terrain.ClampedX; vy = terrain.ClampedY;
        if (terrain.HasImpact)
        {
            if (heldFamily) return ProjectileActorSimulationResult.Rejected;
            var collisionBody = ToolUpdate(initial, terrain.X, terrain.Y,
                terrain.ClampedX, terrain.ClampedY, ai);
            // Source484 cuts precede this draw; selected cracked-brick cuts
            // follow it on the same Main.rand preview before rebound.
            if (cuts is not null)
            {
                var collisionLife = nextLifecycle with
                {
                    TimeLeft = axe.TimeLeft, Liquid = terrain.Liquid, SourceWetCount = terrain.WetCount,
                    ThrownAxe = axe with { X = terrain.X, Y = terrain.Y,
                        VelocityX = terrain.ClampedX, VelocityY = terrain.ClampedY }
                };
                if (!projectiles.TryPrepareSimulation(in initial, out var phaseActor) || phaseActor is null ||
                    !phaseActor.TryStageMotion(in collisionBody, in collisionLife) ||
                    !phaseActor.TryStageSourceDirection(axe.Direction) ||
                    !cuts.TrySetSourceActorView(phaseActor) || !cuts.TryStageCollisionCuts(in collisionBody))
                    return ProjectileActorSimulationResult.Rejected;
            }
            else _ = random.Next(3);
            if (!VanillaThrownAxeProjectile1458.TryResolveTileImpact(axe,
                    terrain.IncomingX, terrain.IncomingY, out axe))
                return ProjectileActorSimulationResult.Rejected;
            vx = axe.VelocityX; vy = axe.VelocityY;
            ai = new(axe.Ai0, axe.Ai1, 0f);
            sourceNetUpdate = true;
        }
        (x, y) = VanillaProjectileWorldMotionResolver.CompleteToolPosition(in terrain, vx, vy);
        int direction = retainedDirection;
        if (VanillaProjectileSourceDirection1458.TryResolveAfterMotion(initial.Type, in ai, vx, out int changedDirection))
            direction = changedDirection;
        var afterMotion = ToolUpdate(initial, x, y, vx, vy, ai);
        int timeLeft = earlyKill ? 0 : (heldFamily ? held.TimeLeft : axe.TimeLeft) - 1;
        bool terminal = earlyKill || timeLeft <= 0;
        if (terminal && !earlyKill) killOrigin = new(x, y);
        if (heldFamily)
        {
            held = held with { X = x, Y = y, VelocityX = vx, VelocityY = vy, Direction = direction,
                TimeLeft = Math.Max(0, timeLeft), Active = !terminal, NetUpdate = false };
            nextLifecycle = nextLifecycle with { HeldTool = held,
                LocalAi = lifecycle.LocalAi with { Ai0 = held.LocalAi0, Ai1 = held.LocalAi1 } };
        }
        else
        {
            axe = axe with { X = x, Y = y, VelocityX = vx, VelocityY = vy, Direction = direction,
                TimeLeft = Math.Max(0, timeLeft), Active = !terminal, NetUpdate = false };
            nextLifecycle = nextLifecycle with { ThrownAxe = axe };
        }
        nextLifecycle = nextLifecycle with { TimeLeft = Math.Max(0, timeLeft), Liquid = terrain.Liquid,
            SourceWetCount = terrain.WetCount };
        // Damage/CutTiles is between movement and lifetime expiry. A time1 axe
        // still reaches this source phase; an AI/inactive-owner Kill does not.
        if (cuts is not null)
        {
            // NewProjectile's full-pool oldest selection observes this exact
            // source phase: refreshed AI lifetime BEFORE the common time--.
            // A final afterimage bound only after allocation cannot recover it.
            var sourceDamageLife = nextLifecycle with { TimeLeft = timeLeft + 1 };
            if (heldFamily) sourceDamageLife = sourceDamageLife with
                { HeldTool = held with { TimeLeft = timeLeft + 1, Active = true } };
            else sourceDamageLife = sourceDamageLife with
                { ThrownAxe = axe with { TimeLeft = timeLeft + 1, Active = true } };
            if (!projectiles.TryPrepareSimulation(in initial, out var phaseActor) || phaseActor is null ||
                !phaseActor.TryStageMotion(in afterMotion, in sourceDamageLife) ||
                !phaseActor.TryStageSourceDirection(direction) || !cuts.TrySetSourceActorView(phaseActor) ||
                !cuts.TryStageDamageCuts(in afterMotion) || !cuts.TrySeal())
                return ProjectileActorSimulationResult.Rejected;
        }
            if (!projectiles.TryPrepareSimulation(in initial, out var publication) || publication is null ||
                !publication.TryStageMotion(in afterMotion, in nextLifecycle) ||
                !publication.TryStageSourceDirection(direction) ||
                !(terminal && !owner.Inactive ? publication.TryQueueToolDespawn(killOrigin) : publication.TryQueuePublication()) ||
                cuts is not null && (!cuts.TryBindSourceActor(publication) || !cuts.IsCurrent) || !owner.IsCurrent || !publication.IsCurrent ||
                !ReferenceEquals(toolActorOwners[initial.Handle.Slot], binding) ||
                !projectileRandom.HasSameState(randomBefore))
                return ProjectileActorSimulationResult.Rejected;
            // All following adoption writes are callback-free. No observer may
            // see only one of dummy clocks, projectile, world effects or RNG.
            if (heldFamily && !owner.TryAdoptUnpublished()) return ProjectileActorSimulationResult.Rejected;
            if (!publication.TryAdoptUnpublished(out _, out bool expired) || expired != terminal ||
                cuts is not null && !cuts.TryAdoptUnpublished())
                throw new InvalidOperationException("A prepared tool actor lost joint adoption ownership.");
            projectileRandom.CopyStateFrom(random);
            if (ReferenceEquals(toolControlledInputs[initial.Handle.Slot], retainedInput))
                toolControlledInputs[initial.Handle.Slot] = null;
            if (ReferenceEquals(toolStopInputs[initial.Handle.Slot], binding))
                toolStopInputs[initial.Handle.Slot] = null;
            toolActorOwners[initial.Handle.Slot] = terminal ? null : binding with
                { LastSelectedPick = owner.SelectedPick, LastMeleeSpeed = meleeSpeed };
            ExceptionDispatchInfo? failure = null;
            if (cuts is not null)
                while (true)
                {
                    try { if (!cuts.TryPublishNext()) break; }
                    catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
                }
            try
            {
                if (replication is not null) replication.TryPublishToolActor(publication, !terminal && sourceNetUpdate);
                else while (publication.TryPublishNext()) { }
            }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
            failure?.Throw();
            return ProjectileActorSimulationResult.Applied;
        }
    }

    private static ProjectileStateUpdate ToolUpdate(in ProjectileSnapshot before, float x, float y,
        float vx, float vy, ProjectileAiState ai) =>
        new(before.Type, before.Spawner, x, y, vx, vy, ai, before.BannerIdToRespondTo,
            before.Damage, before.KnockBack, before.OriginalDamage);

    internal bool TryPrepareToolActorBirth(ToolBirthPreparation1458 birth,
        in ProjectileSnapshot preview, out ToolActorBirthPreparation1458? preparation)
    {
        preparation = null;
        if (!birth.IsCurrent || birth.Player.Capture.Actor.Member is not { } member ||
            birth.Player.Capture.Phase.DerivedCombat is not { } combat ||
            !float.IsFinite(combat.MeleeAnimationMultiplier) || combat.MeleeAnimationMultiplier <= 0f ||
            preview.Handle.Slot >= toolActorOwners.Length || preview.Type != birth.State.Type ||
            preview.Spawner != birth.Player.Capture.Connection.Player.Slot.Value ||
            preview.Type.Value != birth.Tool.Shoot ||
            !VanillaHeldToolProjectile1458.IsSupported(preview.Type) &&
                preview.Type.Value != VanillaThrownAxeProjectile1458.ProjectileType)
            return false;
        var owner = new ToolActorOwner1458(preview.Handle, birth.Player.Capture.Connection.Player,
            member, birth.Tool.Item, birth.Tool.Prefix, birth.Player.Before.SourceSlot,
            birth.Player.Before.UseEpoch, birth.Tool.Pick, combat.MeleeAnimationMultiplier);
        preparation = new(this, birth, preview, owner, toolActorOwners[preview.Handle.Slot]);
        return preparation.IsCurrent;
    }

    internal sealed class ToolActorBirthPreparation1458
    {
        private readonly ProjectileAuthority owner;
        private readonly ToolBirthPreparation1458 birth;
        private readonly ProjectileSnapshot preview;
        private readonly ToolActorOwner1458 next;
        private readonly ToolActorOwner1458? before;
        private bool sealedForAdoption;
        private bool adopted;

        internal ToolActorBirthPreparation1458(ProjectileAuthority owner,
            ToolBirthPreparation1458 birth, ProjectileSnapshot preview,
            ToolActorOwner1458 next, ToolActorOwner1458? before)
        {
            this.owner = owner;
            this.birth = birth;
            this.preview = preview;
            this.next = next;
            this.before = before;
        }

        internal bool IsCurrent => !adopted && birth.IsCurrent &&
            ReferenceEquals(owner.toolActorOwners[preview.Handle.Slot], before);

        // Called in the complete birth preflight, before either store is adopted.
        internal bool TrySealForAdoption()
        {
            if (sealedForAdoption || !IsCurrent) return false;
            sealedForAdoption = true;
            return true;
        }

        // The player preparation has deliberately advanced its clock revision
        // here. Its old capture is no longer current; no callback may intervene
        // between the seal and this concrete callback-free joint-adoption tail.
        internal bool TryAdoptUnpublished(in ProjectileSnapshot born)
        {
            if (adopted || !sealedForAdoption || born != preview ||
                !ReferenceEquals(owner.toolActorOwners[born.Handle.Slot], before) ||
                !owner.players.TryGet(next.Player, out var member) || !ReferenceEquals(member, next.Member) ||
                !owner.projectiles.TryGet(born.Handle, out var current) || current != born ||
                !owner.projectiles.TryGetCombatTrustedOwner(born.Handle, out var trusted) || trusted != next.Player)
                return false;
            owner.toolActorOwners[born.Handle.Slot] = next;
            owner.toolControlledInputs[born.Handle.Slot] = null;
            owner.toolStopInputs[born.Handle.Slot] = null;
            adopted = true;
            return true;
        }
    }

    internal sealed record ToolActorOwner1458(ProjectileHandle Projectile, PlayerHandle Player,
        RuntimePlayerMember Member, ItemTypeId Item, PrefixId Prefix, byte SelectedSlot,
        ulong UseEpoch, int LastSelectedPick, float LastMeleeSpeed);
}
