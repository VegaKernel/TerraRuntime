using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectileNpcCombatPass
{
    private RuntimeNpcStore.OrdinaryArrowNpcWorkspace? ordinaryArrowWorkspace;

    internal readonly record struct OrdinaryArrowPublication(int HitIndex, int ProjectileIndex);

    internal readonly record struct OrdinaryArrowHit(
        int Subupdate, int NumUpdates, ProjectileSnapshot Projectile,
        NpcSnapshot Before, NpcSnapshot Accepted, NpcDamageRequest Request,
        NpcDamageResult Result, VanillaUnifiedRandom1458 AfterRandom);

    internal bool TryPrepareOrdinaryArrowContinuation(in ProjectileSnapshot initial,
        VanillaProjectileWorldStateStepper stepper, out OrdinaryArrowContinuationPreparation? preparation,
        out bool ownedActor)
    {
        preparation = null;
        ownedActor = false;
        if (initial.Type.Value is not (4 or 5) || !usesSourceRandom || status is null ||
            initial.Handle.Slot >= RuntimeProjectileStore.VanillaPhysicalSlotCount ||
            BitConverter.SingleToInt32Bits(initial.Ai.Ai2) is not (0 or 3) || initial.Ai.Ai1 != 0 ||
            !projectiles.TryGetCombatTrustedOwner(initial.Handle, out var trustedOwner) ||
            !players.TryCaptureProjectileUse(trustedOwner, out var owner) || owner is null ||
            owner.Player.Hostile || owner.Player.Luck != 0 || owner.Player.IsDead ||
            !players.TryCaptureProjectileCombatSnapshot(trustedOwner, out var ownerCombat) ||
            !projectiles.TryPrepareSimulation(in initial, out var actor) || actor is null ||
            actor.SourceNpc.IsAssigned || actor.Lifecycle.Reflected ||
            !stepper.TryCaptureOrdinaryArrowTerrain(in initial, out var terrain)) return false;
        if (owner.Buffs is null || !HasSupportedNpcHitBuffs(owner.Buffs)) return false;
        int censusCount = npcs.CopyActive(npcBuffer);
        int count = 0;
        for (int i = 0; i < censusCount; i++)
        {
            var candidate = npcBuffer[i];
            if (!IsEligibleTarget(in candidate, out var hitbox)) continue;
            if (!terrain.MayReach(candidate.PositionX, candidate.PositionY, hitbox.Width, hitbox.Height)) continue;
            // The private global mutation stamp also guards every unselected actor.
            // Only reachable targets need detached mutable damage slots.
            if (candidate.TypeIdentity.Value != 3 || candidate.NetIdentity.Value != 3) return false;
            npcBuffer[count++] = candidate;
        }
        var statusPlans = new RuntimeNpcBuffAdditionPlan1458[count];
        // Unsupported reachable effects cannot be discovered after offers have been sampled.
        for (int i = 0; i < count; i++)
        {
            var target = npcBuffer[i];
            if (target.TypeIdentity.Value != 3 || target.NetIdentity.Value != 3 ||
                !IsEligibleTarget(in target, out _) || target.Simulation.Immortal != false ||
                target.Simulation.ShimmerTransparency != 0 || target.Simulation.LifeRegenCounter is null)
                return false;
            if (!status.CaptureAddition(in target, out statusPlans[i])) return false;
            var checkpoint = statusPlans[i].Checkpoint;
            if (checkpoint.Flags != default || checkpoint.PreviousFlags != default ||
                checkpoint.Stinky || checkpoint.PreviousStinky || checkpoint.VisualOffer != default)
                return false;
            for (int slot = 0; slot < RuntimeNpcBuffStatus1458.Capacity; slot++)
                if (checkpoint.Slots[slot].Type != 0 || checkpoint.Slots[slot].Duration != 0) return false;
            if (!VanillaCombatFacts.TryResolvePveHit(initial.Type, initial.Damage, in ownerCombat,
                    1, 15, out var maximum)) return false;
            var worst = new NpcDamageRequest(target.Handle,
                DamageSource.FromPlayerProjectile(trustedOwner, initial.Handle), maximum.Damage,
                maximum.ArmorPenetration, true, initial.KnockBack, 1);
            // At most one successful strike per target in this tick (shared owner immunity).
            if (!TerraRuntime.Gameplay.Npcs.VanillaNpcDefinitionCatalog.TryGet(target.TypeIdentity,target.NetIdentity,out var npcDefinition) || !VanillaNpcDamageResolver.TryResolve(target.Simulation.DefenseOverride ?? npcDefinition.Defense, in worst, out _, out int bound) ||
                target.Simulation.Life <= bound) return false;
        }
        int steps = VanillaProjectileUpdateFacts.GetSubupdatesPerWorldTick(initial.Type, initial.Ai);
        if (!VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(initial.Type, out int initialPenetration))
            return false;
        int penetration = actor.Lifecycle.PenetrateOverride ?? initialPenetration;
        // Terminal initial contexts stay with the existing expiry/Kill owner.
        // Refusing them as owned every tick would prevent that owner making progress.
        if (penetration == 0 || penetration < -1 ||
            penetration > 0 && count >= penetration || actor.Lifecycle.TimeLeft <= steps)
            return false;
        ordinaryArrowWorkspace ??= npcs.CreateOrdinaryArrowNpcWorkspace();
        if (!ordinaryArrowWorkspace.TryPrepare(npcBuffer.AsSpan(0, count), out var preview) || preview is null)
            return false;
        ownedActor = true;
        bool retained = false;
        try
        {
            if (!TryResolveOwnerRow(trustedOwner, out int row)) return false;
            var immunityTicks = new long[count];
            var immunityGenerations = new NpcGeneration[count];
            var shadowHit = new bool[count];
            for (int i = 0; i < count; i++)
            {
                int index = row + npcBuffer[i].Handle.Slot;
                immunityTicks[i] = lastOwnerNpcHitTick[index];
                immunityGenerations[i] = lastOwnerNpcHitGeneration[index];
            }
            var beforeRandom = sourceRandom.Clone();
            long tick = tickProvider();
            if (!sourceRandom.HasSameState(beforeRandom) || !players.IsCurrentProjectileUse(owner) ||
                !preview.IsCurrent || !actor.IsCurrent || !terrain.IsCurrent) return false;
            var afterRandom = beforeRandom.Clone();
            var damage = new RuntimeNpcDamageExecutor(preview.Projected);
            var journal = new List<OrdinaryArrowHit>();
            var publications = new List<OrdinaryArrowPublication>();
            if (!TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(initial.Type, out var definition)) return false;
            for (int subupdate = 0; subupdate < steps && actor.IsActive; subupdate++)
            {
                int firstHit = journal.Count;
                var current = actor.Snapshot;
                var life = actor.Lifecycle;
                var context = new ProjectileSimulationStepContext(current, life, subupdate, steps);
                if (!stepper.TryStepState(in context, out var next) ||
                    next.TerminationReason != ProjectileSimulationTerminationReason.None ||
                    next.CollisionTileCutOffer is not null || next.PlayerBuff is not null ||
                    next.NpcHealing is not null || next.TimeLeft <= 0) return false;
                // Damage occurs after movement but before the ordinary lifetime decrement.
                var motionLife = life with { Liquid = next.Liquid ?? life.Liquid,
                    LocalAi = next.LocalAi ?? life.LocalAi,
                    OldVelocityX = current.VelocityX, OldVelocityY = current.VelocityY };
                if (!actor.TryStageMotion(next.State, motionLife) ||
                    !VanillaArrowSourceOffers1458.TryApply(initial.Type,
                        VanillaArrowSourcePhase1458.PostMovementBeforeDamage, afterRandom.Next)) return false;
                for (int i = 0; i < count; i++)
                {
                    if (!preview.Projected.TryGet(npcBuffer[i].Handle, out var target) ||
                        !IsEligibleTarget(in target, out var hitbox)) return false;
                    if (shadowHit[i] || !Intersects(actor.Snapshot, definition, target, hitbox) ||
                        immunityGenerations[i] == target.Handle.Generation &&
                        immunityTicks[i] != long.MinValue &&
                        tick - immunityTicks[i] < VanillaProjectileNpcCombatFacts.BaselineOwnerNpcHitCooldownTicks)
                        continue;
                    var hitProjectile = actor.Snapshot;
                    int crit = afterRandom.Next(1, 101), variation = afterRandom.Next(-15, 16);
                    if (!VanillaCombatFacts.TryResolvePveHit(hitProjectile.Type, hitProjectile.Damage,
                            in ownerCombat, crit, variation, out var hit)) return false;
                    _ = afterRandom.NextDouble(); _ = afterRandom.NextDouble();
                    if (!VanillaArrowPostHit1458.TryResolveStoredDamage(hitProjectile.Type,
                            hitProjectile.Damage, out int reduced)) return false;
                    if (!actor.TryStageOrdinaryArrowPostHit(ArrowUpdate(hitProjectile) with { Damage = checked((short)reduced) }))
                        return false;
                    int direction = hitProjectile.VelocityX > .01f ? 1 : hitProjectile.VelocityX < -.01f ? -1 : 0;
                    var request = new NpcDamageRequest(target.Handle,
                        DamageSource.FromPlayerProjectile(trustedOwner, initial.Handle), hit.Damage,
                        hit.ArmorPenetration, hit.Critical, hitProjectile.KnockBack, direction);
                    if (!damage.TryApplyUnpublished(in request, out var result, out var accepted,
                            out bool spawn, out _) || spawn || result.Lethal ||
                        !VanillaArrowPostHit1458.TryResolveAfterStrike(hitProjectile.Type,
                            actor.Snapshot.Ai.Ai2, null, true, out var afterStrike)) return false;
                    var post = actor.Snapshot;
                    if (!actor.TryStageOrdinaryArrowPostHit(ArrowUpdate(post) with
                            { Ai = post.Ai with { Ai2 = afterStrike.Ai2 } }) || !actor.TryStageNpcHit()) return false;
                    shadowHit[i] = true;
                    publications.Add(new(journal.Count, -1));
                    journal.Add(new(subupdate, context.VanillaNumUpdates, hitProjectile, target,
                        accepted, request, result, afterRandom.Clone()));
                    // Never publish a terminal state without owning its Kill offers.
                    if (!actor.IsActive) return false;
                }
                if (actor.IsActive)
                {
                    var finalLife = actor.Lifecycle with { TimeLeft = next.TimeLeft };
                    if (!actor.TryStageMotion(ArrowUpdate(actor.Snapshot), finalLife)) return false;
                    if (journal.Count != firstHit)
                    {
                        int index = actor.PublicationCount;
                        if (!actor.TryQueuePublication()) return false;
                        publications.Add(new(-1,index));
                    }
                }
            }
            if (!combat.TryPrepareOrdinaryArrowPublication(journal, trustedOwner, out var publication) ||
                publication is null) return false;
            preparation = new(this, actor, preview, owner, terrain, beforeRandom, afterRandom,
                row, tick, npcBuffer.AsSpan(0, count).ToArray(), immunityTicks, immunityGenerations,
                shadowHit, journal.ToArray(), publications.ToArray(), ownerGenerations[initial.Spawner],
                statusPlans, publication);
            retained = true;
            return true;
        }
        finally { if (!retained) preview.Dispose(); }
    }

    private static ProjectileStateUpdate ArrowUpdate(ProjectileSnapshot p) => new(p.Type,p.Spawner,p.PositionX,p.PositionY,p.VelocityX,p.VelocityY,p.Ai,p.BannerIdToRespondTo,p.Damage,p.KnockBack,p.OriginalDamage);

    internal sealed class OrdinaryArrowContinuationPreparation : IDisposable
    {
        private readonly RuntimeProjectileNpcCombatPass owner;
        private readonly RuntimeProjectileStore.SimulationPreparation actor;
        private readonly RuntimeNpcStore.OrdinaryArrowNpcPreview preview;
        private readonly RuntimePlayerProjectileUseCapture player;
        private readonly VanillaProjectileWorldStateStepper.OrdinaryArrowTerrainCapture terrain;
        private readonly VanillaUnifiedRandom1458 beforeRandom, afterRandom;
        private readonly int row;
        private readonly long tick;
        private readonly NpcSnapshot[] targets;
        private readonly long[] immunityTicks;
        private readonly NpcGeneration[] immunityGenerations;
        private readonly bool[] hit;
        private readonly PlayerSessionGeneration ownerGeneration;
        private readonly RuntimeNpcBuffAdditionPlan1458[] statusPlans;
        private readonly RuntimeNpcNetworkCombatPipeline.OrdinaryArrowPublicationPreparation publication;
        private bool adopted, disposed, publishing;
        private int published;
        internal OrdinaryArrowContinuationPreparation(RuntimeProjectileNpcCombatPass owner,
            RuntimeProjectileStore.SimulationPreparation actor, RuntimeNpcStore.OrdinaryArrowNpcPreview preview,
            RuntimePlayerProjectileUseCapture player, VanillaProjectileWorldStateStepper.OrdinaryArrowTerrainCapture terrain,
            VanillaUnifiedRandom1458 beforeRandom, VanillaUnifiedRandom1458 afterRandom, int row, long tick,
            NpcSnapshot[] targets, long[] immunityTicks, NpcGeneration[] immunityGenerations, bool[] hit,
            OrdinaryArrowHit[] hits, OrdinaryArrowPublication[] publications, PlayerSessionGeneration ownerGeneration,
            RuntimeNpcBuffAdditionPlan1458[] statusPlans,
            RuntimeNpcNetworkCombatPipeline.OrdinaryArrowPublicationPreparation publication)
        {
            this.owner = owner; this.actor = actor; this.preview = preview; this.player = player;
            this.terrain = terrain; this.beforeRandom = beforeRandom; this.afterRandom = afterRandom;
            this.row = row; this.tick = tick; this.targets = targets; this.immunityTicks = immunityTicks;
            this.immunityGenerations = immunityGenerations; this.hit = hit; Hits = hits; Publications = publications;
            this.ownerGeneration = ownerGeneration;
            this.statusPlans = statusPlans; this.publication = publication;
        }
        internal IReadOnlyList<OrdinaryArrowHit> Hits { get; }
        internal IReadOnlyList<OrdinaryArrowPublication> Publications { get; }
        internal bool TryGetProjectilePublication(int index,out ProjectileStateCommitKind kind,out ProjectileSnapshot snapshot) => actor.TryGetPublication(index,out kind,out snapshot);
        internal ProjectileSnapshot FinalProjectile => actor.Snapshot;
        internal ProjectileLifecycleState FinalLifecycle => actor.Lifecycle;
        internal bool CanAdopt
        {
            get
            {
                if (disposed || adopted || !owner.players.IsCurrentProjectileUse(player) ||
                    !actor.IsCurrent || !preview.CanAdopt || !terrain.IsCurrent ||
                    !owner.sourceRandom.HasSameState(beforeRandom) ||
                    owner.ownerGenerations[actor.Snapshot.Spawner] != ownerGeneration || !publication.CanAdopt) return false;
                for (int i = 0; i < targets.Length; i++)
                {
                    int index = row + targets[i].Handle.Slot;
                    if (owner.lastOwnerNpcHitTick[index] != immunityTicks[i] ||
                        owner.lastOwnerNpcHitGeneration[index] != immunityGenerations[i] ||
                        !statusPlans[i].IsCurrent) return false;
                    if (!preview.Projected.TryGet(targets[i].Handle, out var projected) ||
                        projected.Revision.Value != targets[i].Revision.Value + (hit[i] ? 1ul : 0ul)) return false;
                }
                return true;
            }
        }
        internal bool TryAdoptUnpublished()
        {
            if (!CanAdopt) return false;
            // Every operation below is a concrete single-writer adoption, with no callback/provider.
            if (!preview.TryAdoptUnpublished() || !actor.TryAdoptUnpublished(out _, out _))
                throw new InvalidOperationException("An admitted arrow continuation lost its owner.");
            owner.sourceRandom.CopyStateFrom(afterRandom);
            for (int i = 0; i < targets.Length; i++)
            {
                if (!preview.Projected.TryGet(targets[i].Handle, out var accepted) ||
                    !owner.status!.TryAdoptAddition(statusPlans[i], in accepted))
                    throw new InvalidOperationException("An admitted arrow lost its status owner.");
                if (hit[i]) owner.MarkOwnerNpcCooldown(row, targets[i].Handle, tick);
            }
            if (!publication.TryAdoptInteractions())
                throw new InvalidOperationException("An admitted arrow lost its interaction ledger.");
            owner.CommittedHits += Hits.Count;
            adopted = true;
            return true;
        }
        // The supplied concrete publication adapter must not recompute damage or sample random.
        internal bool TryPublish()
        {
            if (!adopted || disposed || publishing) return false;
            publishing = true;
            try
            {
                while (!disposed && published < Publications.Count)
                {
                    var entry = Publications[published++];
                    if (entry.HitIndex >= 0)
                    {
                        var hitEntry = Hits[entry.HitIndex];
                        if (preview.IsAcceptedTargetCurrent(hitEntry.Accepted.Handle)) publication.TryPublishHit(in hitEntry);
                    }
                    else actor.TryPublishNext();
                }
                return true;
            }
            finally
            {
                publishing = false;
                if (disposed) preview.Dispose();
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Reentrant disposal suppresses the rest of this journal but cannot release
            // the shared preview scratch while a publication callback is on the stack.
            if (!publishing) preview.Dispose();
        }
    }
}
