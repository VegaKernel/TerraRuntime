using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NpcInitializationTests
{
    [Fact]
    public void Hands_publish_before_final_head_and_first_child_sees_final_defense()
    {
        var sink = new Sink();
        var store = new RuntimeNpcStore(commitSink: sink);
        var head = Spawn(store);
        sink.Events.Clear();
        var random = new CountedRandom();
        var ai = Ai(random);
        var tiles = new WorldTileStore(new WorldDimensions(400, 400));
        ai.SetProjectileEnvironment(new VanillaNpcProjectileWorldEnvironment(tiles));
        var motion = new VanillaNpcWorldMotionAiStepper(ai, tiles);
        var observer = new ObserveHands(motion, store);
        var projectiles = new RuntimeProjectileStore();
        Assert.Equal(3, new RuntimeNpcAiStateExecutor(store, projectiles).Tick(observer).Applied);
        Assert.Equal(2, observer.Hands);
        Assert.Equal(0, random.Calls);
        Assert.Equal(0, projectiles.ActiveCount);
        Assert.Equal(new[] { (NpcStateCommitKind.Spawn, 36), (NpcStateCommitKind.Spawn, 36), (NpcStateCommitKind.Update, 35) },
            sink.Events.Take(3).Select(e => (e.Kind, e.Npc.Type)).ToArray());
        Assert.True(store.TryGet(head.Handle, out var current));
        Assert.Equal(3UL, current.Revision.Value);
        Assert.Equal(960f + .07f, current.PositionX);
        Assert.Equal(898f - .03f, current.PositionY);
    }

    [Fact]
    public void Initialization_retargets_even_when_the_previous_target_is_still_valid()
    {
        var store = new RuntimeNpcStore();
        var head = Spawn(store, target: 1);
        var ai = Ai();
        ai.SetCandidates([new(0, 1010, 1021, 0, true, false, false, false), new(1, 1510, 1021, 0, true, false, false, false)]);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(store).Tick(new HeadOnly(ai)).Applied);
        Assert.True(store.TryGet(head.Handle, out var current));
        Assert.Equal(0, current.Target);
        for (byte slot = 11; slot <= 12; slot++)
        {
            Assert.True(store.TryGetActive(slot, out var hand));
            Assert.Equal(0, hand.Target);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Rejected_or_stale_first_proposal_cannot_initialize_or_spawn(int failure)
    {
        var store = new RuntimeNpcStore();
        var head = Spawn(store);
        var wrapped = new Intercept(Ai(), (int call, NpcSnapshot npc, NpcStateUpdate next) =>
        {
            if (failure == 0) return next with { VelocityX = float.NaN };
            if (failure == 1)
            {
                Assert.True(store.TryDespawn(npc.Handle));
                Assert.True(store.TrySpawn(npc.Handle.Slot, in next, out _));
            }
            else
            {
                var retained = next with { Ai = new NpcAiState(0, 0, 99, 0) };
                Assert.True(store.TryUpdate(npc.Handle, in retained, out _));
            }
            return next;
        });
        var result = new RuntimeNpcAiStateExecutor(store).Tick(wrapped);
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Rejected);
        Assert.Equal(1, store.ActiveCount);
        if (failure == 0)
        {
            Assert.True(store.TryGet(head.Handle, out var current));
            Assert.Equal(head, current);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Reentrant_child_publication_stops_initialization_on_head_change(bool replace)
    {
        var sink = new Sink();
        var store = new RuntimeNpcStore(commitSink: sink);
        var head = Spawn(store);
        sink.Callback = npc =>
        {
            if (npc.Type != 36) return;
            sink.Callback = null;
            var changed = new NpcStateUpdate(35, 35, 777, 888, 0, 0, 0, new(1, 0, 99, 0), NpcSimulationState.Initial);
            if (replace)
            {
                Assert.True(store.TryDespawn(head.Handle));
                Assert.True(store.TrySpawn(head.Handle.Slot, in changed, out _));
            }
            else Assert.True(store.TryUpdate(head.Handle, in changed, out _));
        };
        Assert.Equal(0, new RuntimeNpcAiStateExecutor(store).Tick(new HeadOnly(Ai())).Applied);
        Assert.Equal(2, store.ActiveCount);
        Assert.True(store.TryGetActive(10, out var current));
        Assert.Equal(99f, current.Ai.Ai2);
        Assert.Equal(777f, current.PositionX);
        Assert.False(store.TryGetActive(12, out _));
    }

    [Fact]
    public void Continuation_cannot_overwrite_an_intervening_revision()
    {
        var store = new RuntimeNpcStore();
        var head = Spawn(store);
        var wrapped = new Intercept(Ai(), (int call, NpcSnapshot npc, NpcStateUpdate next) =>
        {
            if (call == 2)
            {
                var changed = next with { Ai = new(1, 0, 99, 0) };
                Assert.True(store.TryUpdate(npc.Handle, in changed, out _));
            }
            return next;
        });
        var result = new RuntimeNpcAiStateExecutor(store).Tick(wrapped);
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Rejected);
        Assert.Equal(3, store.ActiveCount);
        Assert.True(store.TryGet(head.Handle, out var current));
        Assert.Equal(99f, current.Ai.Ai2);
    }

    private static NpcSnapshot Spawn(RuntimeNpcStore store, ushort target = 0)
    {
        store.SetVanillaSpawnContextSource(() => new(2, 1, false));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHead, 1000, 1000, 0, 0, target)
            { StartSlot = 10 }, out var head));
        return head;
    }

    private static VanillaNpcTargetingAiStepper Ai(IVanillaNpcRandom? random = null)
    {
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: random);
        ai.SetWorldConditions(false, false, expertMode: true);
        ai.SetCandidates([new(0, 1510, 1021, 0, true, false, false, false)]);
        return ai;
    }

    private sealed class CountedRandom : IVanillaNpcRandom
    {
        public int Calls { get; private set; }
        public int NextInt32(int inclusiveMin, int exclusiveMax) { Calls++; return inclusiveMin; }
    }

    private sealed class Sink : INpcStateCommitSink
    {
        public List<(NpcStateCommitKind Kind, NpcSnapshot Npc)> Events { get; } = [];
        public Action<NpcSnapshot>? Callback { get; set; }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc)
        {
            Events.Add((kind, npc));
            Callback?.Invoke(npc);
        }
    }

    private sealed class HeadOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return npc.Type == 35 && inner.TryStepState(in npc, out next);
        }
    }

    private sealed class Intercept(INpcAiStateStepper inner, Func<int, NpcSnapshot, NpcStateUpdate, NpcStateUpdate> intercept)
        : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        private int calls;
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            if (npc.Type != 35 || !inner.TryStepState(in npc, out next)) return false;
            next = intercept(++calls, npc, next);
            return true;
        }
    }

    private sealed class ObserveHands(INpcAiStateStepper inner, RuntimeNpcStore store) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public int Hands { get; private set; }
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (npc.Type == 36)
            {
                Hands++;
                Assert.True(store.TryGetActive(10, out var head));
                Assert.Equal(60, head.Simulation.DefenseOverride);
                Assert.Equal(1f, head.Ai.Ai2);
            }
            return inner.TryStepState(in npc, out next);
        }
    }
}
