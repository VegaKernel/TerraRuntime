using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private GemBunnyDeathPhase? pendingGemBunnyDeath;
    private sealed record GemBunnyDeathPhase(VanillaUnifiedRandom1458 BeforeRandom,
        VanillaUnifiedRandom1458 AfterRandom, Func<bool> IsCurrent);

    // The common death planner owns loot, money, healing, credit and the source RNG. The caller supplies
    // the already staged AI/contact checkpoint, never an alternate random stream or a second NPC store.
    internal bool TryCommitGemBunnyDeath(in NpcSnapshot expected, in NpcStateUpdate dead,
        VanillaUnifiedRandom1458 beforeRandom, VanillaUnifiedRandom1458 afterRandom,
        Func<bool> phaseCurrent, int strikeDamage, float knockback, int direction, byte[]? homeFrame = null,
        NpcStateUpdate? finalState = null, IRuntimeGemBunnyStatusPhase1458? statusPhase = null,
        RuntimeNpcBuffAdditionPlan1458? addition = null, byte[]? additionFrame = null,
        Func<VanillaUnifiedRandom1458, bool>? afterLoot = null, bool strikeBeforeStatus = false)
    {
        if (!VanillaGroundCritterAi1458.IsSupported(expected.Type) || dead.Type != expected.Type ||
            dead.Simulation.Life != 0 || expected.Simulation.Life <= 0 ||
            pendingGemBunnyDeath is not null || pendingDeathPlan is not null || pendingProjectileStrike is not null ||
            pendingDebuffPrelude is not null || pendingTownStrike is not null ||
            !RuntimeNpcStore.IsValid(in dead) || !random.SourceRandom.HasSameState(beforeRandom) || !phaseCurrent() ||
            !npcs.TryGet(expected.Handle, out var live) || live != expected) return false;
        var projection = expected with { PositionX = dead.PositionX, PositionY = dead.PositionY,
            VelocityX = dead.VelocityX, VelocityY = dead.VelocityY, Target = dead.Target, Ai = dead.Ai,
            Simulation = dead.Simulation };
        var retained = finalState ?? dead;
        byte[]? expiredFrame = statusPhase?.ExpiredBuffFrame;
        byte[]? aiBuffFrame = statusPhase?.AiBuffFrame;
        byte[]? collisionBuffFrame = statusPhase?.CollisionBuffFrame;
        if (retained.Type != dead.Type || retained.NetId != dead.NetId || retained.Simulation.Life != 0 ||
            !RuntimeNpcStore.IsValid(in retained)) return false;
        var finalProjection = expected with { PositionX = retained.PositionX, PositionY = retained.PositionY,
            VelocityX = retained.VelocityX, VelocityY = retained.VelocityY, Target = retained.Target,
            Ai = retained.Ai, Simulation = retained.Simulation };
        pendingGemBunnyDeath = new(beforeRandom, afterRandom, phaseCurrent);
        try
        {
            if (!TryPrepareDeathPlan(in projection) || pendingDeathPlan is not { } drops || plannedPrelude is null ||
                afterLoot is not null && !afterLoot(drops.Random) ||
                !npcs.TryPrepareProjectedTerminalRemoval(in finalProjection, out var removal) ||
                !IsCurrentDeathOwner(expected.Handle, expected.Revision) || !phaseCurrent() ||
                !random.SourceRandom.HasSameState(beforeRandom) || !drops.CanAdoptUnpublished() ||
                deathPrelude.Revision != plannedPreludeRevision ||
                !npcs.TryGet(expected.Handle, out live) || live != expected) return false;
            if (!npcs.TryUpdateUnpublished(expected.Handle, in retained, out var committed) ||
                statusPhase is not null && !statusPhase.TryAdoptStatus(in committed, addition) ||
                !drops.TryAdoptUnpublished() ||
                !deathPrelude.TryAdoptUnpublished(plannedPrelude, plannedPreludeRevision, out var preludePublication) ||
                !removal!.TryAdoptUnpublished())
                throw new InvalidOperationException("A validated gem-bunny death lost its callback-free owners.");
            if (plannedDaily is not null) bossRecoveryDaily.CopyFrom(plannedDaily);
            if (worldClock is not null && plannedUnpublishedClock is not null)
                worldClock.AdoptDeathProgress(plannedUnpublishedClock);
            npcReplication?.RetirePreparedDeathBaseline(committed.Handle);
            var wire = new TerrariaNpcDamageState(committed.Handle.Slot,
                RuntimeNpcPacketProjection.ToProtocolGeneration(committed.Handle.Generation),
                (short)Math.Min(strikeDamage, short.MaxValue), knockback, checked((byte)(direction + 1)), 0);
            // Detach the accepted journal before an observer can start another death through this pipeline.
            pendingDeathPlan = null;
            pendingGemBunnyDeath = null;
            ExceptionDispatchInfo? failure = null;
            void Drain(Action action)
            {
                try { action(); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            if (!strikeBeforeStatus && removal.IsAcceptedCurrent && expiredFrame is not null)
                Drain(() => npcReplication?.PublishGemBunnyBuffs(expiredFrame));
            if (removal.IsAcceptedCurrent && homeFrame is not null) Drain(() => npcReplication?.PublishGemBunnyHome(homeFrame));
            if (removal.IsAcceptedCurrent && aiBuffFrame is not null) Drain(() => npcReplication?.PublishGemBunnyBuffs(aiBuffFrame));
            if (removal.IsAcceptedCurrent && additionFrame is not null) Drain(() => npcReplication?.PublishGemBunnyBuffs(additionFrame));
            if (removal.IsAcceptedCurrent) Drain(() => npcReplication?.TryPublishDamage(default, in wire));
            Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Prelude, lootDelivery.Adopt));
            Drain(() => preludePublication?.TryPublish());
            Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Imported, lootDelivery.Adopt));
            Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Recovery, lootDelivery.Adopt));
            Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Money, lootDelivery.Adopt));
            Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Healing, lootDelivery.Adopt));
            if (strikeBeforeStatus && removal.IsAcceptedCurrent && expiredFrame is not null)
                Drain(() => npcReplication?.PublishGemBunnyBuffs(expiredFrame));
            if (removal.IsAcceptedCurrent && collisionBuffFrame is not null)
                Drain(() => npcReplication?.PublishGemBunnyBuffs(collisionBuffFrame));
            Drain(() => removal.TryPublish());
            interactions.Forget(expected.Handle);
            drops.Dispose();
            failure?.Throw();
            return true;
        }
        finally
        {
            pendingGemBunnyDeath = null;
            CancelPendingDeathPlan();
        }
    }
}
