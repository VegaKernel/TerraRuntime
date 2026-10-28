using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

// Called by the existing world-motion dispatcher after the Core plan's unpublished acceptance.
internal sealed class RuntimeGemBunnyLifecycle1458(
    WorldTileStore tiles, double surface, RuntimeNpcStore npcs,
    RuntimeNpcNetworkCombatPipeline combat, bool expertMode = false, RuntimeNpcReplicationRegistry? replication = null,
    RuntimeNpcBuffStatus1458? status = null)
{
    internal NpcSnapshot Complete(in NpcSnapshot before, in NpcSnapshot accepted, VanillaGemBunnyAccepted1458 ai,
        bool dayTime, bool eclipse, bool invasionActive)
    {
        if (!ai.TryGetPlan(in before, in accepted, out var planned) ||
            !RuntimeNpcReplicationRegistry.TryPrepareGemBunnyHome(in before, in planned, out var homeFrame) ||
            !ai.TryGetPlanRandom(in before, in accepted, out var checkpoint, out var phaseRandom) ||
            !ai.TryGetCapturedWorld(in before, in accepted, out var capturedWorld) ||
            capturedWorld is not IRuntimeGemBunnyStatusPhase1458 statusPhase ||
            !VanillaNpcWorldMotionAiStepper.TryApplyGravity(tiles, surface, in planned, out var accelerated, out var gravity) ||
            gravity is null) { ai.Cancel(); return default; }
        accelerated = accelerated with { Simulation = accelerated.Simulation with {
            HostileContactImmunity = Math.Max(0, accelerated.Simulation.HostileContactImmunity - 1) } };
        accelerated = RuntimeTownNpcCombat1458.PlanFriendlyRegeneration(in accelerated);
        // NPCID.Sets.TakesDamageFromHostilesWithoutBeingFriendly explicitly includes all
        // seven gem bunnies, so their update91 Friendly transition does not skip this phase.
        Span<NpcSnapshot> peers = stackalloc NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
        int count = npcs.CopyActive(peers);
        if (!RuntimeTownNpcCombat1458.TryPlanCritterContact(in before, in accelerated, peers[..count],
                new SystemVanillaNpcRandom(phaseRandom), expertMode, out var contacted, out var strike))
        { ai.Cancel(); return default; }
        if (!capturedWorld.SupportsCollision(in contacted)) { ai.Cancel(); return default; }
        var home = new VanillaNpcTownCollisionContext1458(contacted.Simulation.HomeTileY ?? -1,
            dayTime, eclipse, invasionActive);
        if (contacted.Simulation.Life == 0 && strike is { } lethal)
        {
            if (!VanillaNpcWorldMotionAiStepper.TryFinishCollision(tiles, in contacted, gravity.Value.Parameters.Gravity,
                    out var collidedDead, townContext: home) || !capturedWorld.SupportsCollision(in collidedDead))
            { ai.Cancel(); return default; }
            var deadFinal = VanillaGemBunnyAccepted1458.FinishPresentation(in collidedDead);
            if (!statusPhase.TryStageWetRemoval(in collidedDead)) { ai.Cancel(); return default; }
            var source = before; var staged = accepted;
            bool Current() => ai.IsAcceptedCurrent(in source, in staged);
            combat.TryCommitGemBunnyDeath(in accepted, in contacted, checkpoint, phaseRandom,
                Current, lethal.VariedDamage, lethal.KnockBack, lethal.HitDirection, homeFrame, deadFinal, statusPhase);
            ai.Cancel();
            // The existing death owner has already removed/published the generation. The executor
            // must not publish an intermediate life-zero AI update after that terminal transition.
            return default;
        }
        if (contacted.Simulation.HostileContactImmunity == 0 && !contacted.Simulation.DontTakeDamage &&
            contacted.Simulation.Immortal == false &&
            TerraRuntime.Gameplay.Npcs.VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out var definition) &&
            definition.TryResolveHitbox(contacted.Simulation, out var body) &&
            VanillaWorldCollision.LavaCollision(tiles, contacted.PositionX, contacted.PositionY, body.Width, body.Height))
        {
            var source = before; var staged = accepted;
            if (!statusPhase.TryPlanLavaAddition(in accepted, out var addition, out var additionFrame))
            { ai.Cancel(); return default; }
            bool Current() => ai.IsAcceptedCurrent(in source, in staged) && addition.IsCurrent;
            var burned = contacted with { Simulation = contacted.Simulation with {
                Life = 0, JustHit = true, HostileContactImmunity = 30 } };
            if (!VanillaNpcWorldMotionAiStepper.TryFinishCollision(tiles, in burned, gravity.Value.Parameters.Gravity,
                    out var collidedDead, townContext: home) || !capturedWorld.SupportsCollision(in collidedDead))
            { ai.Cancel(); return default; }
            var deadFinal = VanillaGemBunnyAccepted1458.FinishPresentation(in collidedDead);
            if (!statusPhase.TryStageWetRemoval(in collidedDead, addition)) { ai.Cancel(); return default; }
            combat.TryCommitGemBunnyDeath(in accepted, in burned, checkpoint, phaseRandom, Current, 50, 0f, 0,
                homeFrame, deadFinal, statusPhase, addition, additionFrame);
            ai.Cancel();
            return default;
        }
        if (!VanillaNpcWorldMotionAiStepper.TryFinishCollision(tiles, in contacted, gravity.Value.Parameters.Gravity,
                out var final, townContext: home) || !capturedWorld.SupportsCollision(in final))
        { ai.Cancel(); return default; }
        if (strike.HasValue) ai.ForceSync();
        if (!statusPhase.TryStageWetRemoval(in final)) { ai.Cancel(); return default; }
        var result = ai.Complete(in before, in accepted, in final);
        if (result.Handle.IsAssigned && homeFrame is not null && npcs.TryGet(result.Handle, out var homeOwner) && homeOwner == result)
            replication?.PublishGemBunnyHome(homeFrame);
        if (result.Handle.IsAssigned && npcs.TryGet(result.Handle, out var statusOwner) && statusOwner == result)
            statusPhase.PublishAiStatus();
        if (result.Handle.IsAssigned && strike is { } hit && npcs.TryGet(result.Handle, out var contactOwner) && contactOwner == result)
            RuntimeTownNpcCombat1458.PublishFriendlyContact(in result, in hit, replication);
        if (result.Handle.IsAssigned && npcs.TryGet(result.Handle, out var collisionOwner) && collisionOwner == result)
            statusPhase.PublishCollisionStatus();
        return result;
    }

    internal bool TryCommitLowerWorldDeath(in NpcSnapshot before, VanillaUnifiedRandom1458 random,
        Func<bool>? admissionCurrent = null)
    {
        if (admissionCurrent?.Invoke() == false) return false;
        if (!VanillaGemBunnyAi1458.IsSupported(before.Type) ||
            before.Simulation.Immortal != false || before.Simulation.DontTakeDamage ||
            !TerraRuntime.Gameplay.Npcs.VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(before.Simulation, out var body) ||
            before.PositionY <= tiles.Dimensions.HeightTiles * 16f - 640f + body.Height) return false;
        var damage = new NpcDamageRequest(before.Handle, DamageSource.Environment, 9999);
        if (!VanillaNpcDamageResolver.TryResolve(before.Simulation.DefenseOverride ?? definition.Defense,
                in damage, out _, out int resolved) || resolved < before.Simulation.Life) return false;
        if (status is null || !status.TryPlanGemBunnyLowerWorldPhase(in before, out var statusPhase)) return false;
        var checkpoint = random.Clone();
        var state = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
            before.VelocityX, before.VelocityY, before.Target, before.Ai,
            before.Simulation with { Life = 0, JustHit = true });
        var expected = before;
        bool Current() => admissionCurrent?.Invoke() != false &&
            npcs.TryGet(expected.Handle, out var current) && current == expected &&
            random.HasSameState(checkpoint) && statusPhase.IsCurrent;
        // UpdateNPC continues BuffSetFlags/expiry/VFX after this early strike's loot, then
        // reaches its life-zero network return. Preserve that post-loot shared RNG ordering.
        return combat.TryCommitGemBunnyDeath(in before, in state, checkpoint, checkpoint, Current, 9999, 0f, 0,
            statusPhase: statusPhase, afterLoot: statusPhase.StageAfterLoot, strikeBeforeStatus: true);
    }
}
