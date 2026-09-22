using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaChaosElementalAiTests
{
    [Fact]
    public void Committed_stuck_clock_teleports_through_the_dedicated_source_query()
    {
        var environment = new TeleportEnvironment();
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new ZeroRandom());
        stepper.SetChaosElementalEnvironment(environment);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 512f, 320f, 0, true, false, false, false)]);

        var source = new NpcSnapshot(
            new NpcHandle(1, new NpcGeneration(1)),
            new NpcRevision(1),
            VanillaNpcIds.ChaosElemental.Value,
            checked((short)VanillaNpcIds.ChaosElemental.Value),
            100f,
            100f,
            1f,
            -2f,
            7,
            new NpcAiState(0f, 0f, 0f, 179f),
            NpcSimulationState.Initial);
        var committed = source with { Revision = new NpcRevision(2), Ai = source.Ai with { Ai3 = 180f } };
        var mutations = new CapturingMutationSink();

        Assert.True(stepper.DefersStatePublication(in source, new NpcStateUpdate(
            source.Type, source.NetId, source.PositionX, source.PositionY, source.VelocityX, source.VelocityY,
            source.Target, committed.Ai, source.Simulation)));

        NpcSnapshot completed = stepper.CompleteCommittedState(in source, in committed, mutations);

        Assert.True(environment.Called);
        Assert.Equal(32, environment.TargetTileX);
        Assert.Equal(20, environment.TargetTileY);
        Assert.True(mutations.StateUpdated);
        Assert.Equal(503f, completed.PositionX);
        Assert.Equal(280f, completed.PositionY);
        Assert.Equal(-120f, completed.Ai.Ai3);
        Assert.Equal(1f, completed.VelocityX);
        Assert.Equal(-2f, completed.VelocityY);
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class ZeroRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin;
    }

    private sealed class TeleportEnvironment : IVanillaChaosElementalEnvironment
    {
        public bool Called { get; private set; }
        public int TargetTileX { get; private set; }
        public int TargetTileY { get; private set; }

        public bool TryFindTeleportSpot(float npcPositionX, float npcPositionY, int targetTileX, int targetTileY,
            IVanillaNpcRandom random, out int tileX, out int tileY)
        {
            Called = true;
            TargetTileX = targetTileX;
            TargetTileY = targetTileY;
            tileX = 32;
            tileY = 20;
            return true;
        }
    }

    private sealed class CapturingMutationSink : INpcAiCommittedNpcMutationSink
    {
        public bool StateUpdated { get; private set; }

        public bool TryUpdateAi(in NpcSnapshot expected, NpcAiState ai, out NpcSnapshot committed)
        {
            committed = expected with { Ai = ai };
            return true;
        }

        public bool TryUpdateState(in NpcSnapshot expected, in NpcStateUpdate update, out NpcSnapshot committed)
        {
            StateUpdated = true;
            committed = expected with
            {
                Type = update.Type,
                NetId = update.NetId,
                PositionX = update.PositionX,
                PositionY = update.PositionY,
                VelocityX = update.VelocityX,
                VelocityY = update.VelocityY,
                Target = update.Target,
                Ai = update.Ai,
                Simulation = update.Simulation
            };
            return true;
        }

        public int TryHeal(NpcHandle npc, int maximumAmount) => 0;
        public bool TrySpawn(in NpcAiSpawnIntent intent, out NpcSnapshot spawned) { spawned = default; return false; }
        public bool TrySpawn(in NpcSnapshot source, in NpcAiSpawnIntent intent, out NpcSnapshot spawned) { spawned = default; return false; }
        public bool TrySpawnProjectile(in NpcSnapshot source, in NpcAiProjectileIntent intent, out ProjectileSnapshot spawned) { spawned = default; return false; }
        public bool TryAnnounceSkeletronTaunt(in NpcSnapshot source, int variant) => false;
        public bool TryUpdateVelocity(NpcHandle npc, float velocityX, float velocityY, out NpcSnapshot committed) { committed = default; return false; }
        public bool TryGetActive(byte slot, out NpcSnapshot npc) { npc = default; return false; }
        public bool TryDespawn(NpcHandle npc) => false;
        public bool TryTranslate(NpcHandle npc, float deltaX, float deltaY, out NpcSnapshot committed) { committed = default; return false; }
        public bool TryLinkFollower(NpcHandle npc, byte followerSlot) => false;
    }
}
