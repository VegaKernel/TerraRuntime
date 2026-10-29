using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

// This exercises the real child allocation/publication transaction, independently
// of public NPC lifecycle admission and the still separately qualified App phase.
public sealed class ScutlixBirthPreview1458Tests
{
    [Fact]
    public void Unpublished_mount_adoption_defers_observers_until_the_parent_child_and_random_stream_are_owned()
    {
        var sink = new BirthSink();
        var store = new RuntimeNpcStore(4, sink);
        var random = new VanillaUnifiedRandom1458(1458);
        Prepare(store, random, out var before, out var preview, out var birth, out var placeholder, out var final);
        Assert.True(preview.TryAdoptUnpublished(in placeholder, in final, () => true, out var completed));
        Assert.Equal(0, sink.Retained);
        Assert.True(store.TryGet(birth.Handle, out var actualBirth));
        Assert.Equal(birth, actualBirth);
        Assert.True(random.HasSameState(preview.Random));
        sink.OnBirth = observed =>
        {
            Assert.Equal(birth, observed);
            Assert.True(store.TryGet(before.Handle, out var parent));
            Assert.Equal(completed, parent);
            Assert.True(random.HasSameState(preview.Random));
        };
        preview.PublishBirths();
        Assert.Equal(1, sink.Retained);
        preview.PublishBirths();
        Assert.Equal(1, sink.Retained);
    }

    [Fact]
    public void Reentrant_mount_observer_cannot_duplicate_birth_or_rewind_entropy_consumed_by_the_observer()
    {
        var sink = new BirthSink();
        var store = new RuntimeNpcStore(4, sink);
        var random = new VanillaUnifiedRandom1458(42);
        Prepare(store, random, out _, out var preview, out _, out var placeholder, out var final);
        Assert.True(preview.TryAdoptUnpublished(in placeholder, in final, () => true, out var completed));
        var expected = random.Clone();
        expected.Next();
        sink.OnBirth = _ =>
        {
            preview.PublishBirths();
            random.Next();
            var changed = final with { VelocityX = 3f };
            Assert.True(store.TryUpdateUnpublished(completed.Handle, in changed, out _));
        };
        preview.PublishBirths();
        Assert.Equal(1, sink.Retained);
        Assert.True(random.HasSameState(expected));
        Assert.True(store.TryGet(completed.Handle, out var current));
        Assert.Equal(3f, current.VelocityX);
        preview.PublishBirths();
        Assert.Equal(1, sink.Retained);
        Assert.True(random.HasSameState(expected));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_later_parent_revision_or_replaced_child_generation_suppresses_the_stale_birth(bool replaceChild)
    {
        var sink = new BirthSink();
        var store = new RuntimeNpcStore(4, sink);
        var random = new VanillaUnifiedRandom1458(8675309);
        Prepare(store, random, out _, out var preview, out var birth, out var placeholder, out var final);
        Assert.True(preview.TryAdoptUnpublished(in placeholder, in final, () => true, out var completed));
        if (replaceChild)
        {
            Assert.True(store.TryDespawn(birth.Handle));
            var replacement = State(in birth);
            Assert.True(store.TrySpawn(birth.Handle.Slot, in replacement, out var nextGeneration));
            Assert.NotEqual(birth.Handle, nextGeneration.Handle);
        }
        else
        {
            var changed = final with { VelocityX = 2f };
            Assert.True(store.TryUpdateUnpublished(completed.Handle, in changed, out _));
        }
        preview.PublishBirths();
        Assert.Equal(0, sink.Retained);
    }

    private static void Prepare(RuntimeNpcStore store, VanillaUnifiedRandom1458 random,
        out NpcSnapshot before, out RuntimeNpcStore.AiSpawnPreview preview, out NpcSnapshot birth,
        out NpcSnapshot placeholder, out NpcStateUpdate final)
    {
        store.SetVanillaSpawnContextSource(() => new(1f, 1, false));
        store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
        var source = new NpcStateUpdate(VanillaNpcIds.ScutlixRider.Value,
            (short)VanillaNpcIds.ScutlixRider.Value, 1023f, 1560f, 0f, 0f, 255, default,
            NpcSimulationState.Initial with { TimeLeft = 750 });
        Assert.True(store.TrySpawn(0, in source, out before));
        Assert.True(store.TryCreateAiSpawnPreview(in before, random, out var prepared));
        preview = prepared!;
        var intent = new NpcAiSpawnIntent(VanillaNpcIds.Scutlix, 1032, 1580, 0f, 0f, 255) { StartSlot = 0 };
        Assert.True(preview.TryStageScutlixMount(in intent, out birth));
        var unchanged = State(in before);
        Assert.True(store.TryUpdateUnpublished(before.Handle, in unchanged, out placeholder));
        final = unchanged with { Ai = unchanged.Ai with { Ai0 = birth.Handle.Slot },
            Simulation = unchanged.Simulation with { LocalAi = unchanged.Simulation.LocalAi with { Ai0 = 1f } } };
    }

    private static NpcStateUpdate State(in NpcSnapshot source) => new(source.Type, source.NetId,
        source.PositionX, source.PositionY, source.VelocityX, source.VelocityY, source.Target, source.Ai, source.Simulation);

    private sealed class BirthSink : INpcStateCommitSink, INpcBirthRetentionSink
    {
        internal int Retained;
        internal Action<NpcSnapshot>? OnBirth;
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) { }
        public void NpcBirthRetained(in NpcSnapshot snapshot) { Retained++; OnBirth?.Invoke(snapshot); }
    }
}
