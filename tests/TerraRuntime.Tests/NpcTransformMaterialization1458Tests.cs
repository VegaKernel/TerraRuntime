using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class NpcTransformMaterialization1458Tests
{
    [Theory]
    [InlineData(198)]
    [InlineData(230)]
    [InlineData(532)]
    [InlineData(534)]
    [InlineData(586)]
    [InlineData(593)]
    [InlineData(687)]
    [InlineData(683)]
    public void Selected_fresh_constructor_history_is_known_before_any_trusted_import(int type)
    {
        // Actual initialized NewNPC captures: source constructor zeros, not a Transform-time guess.
        var npcs = new RuntimeNpcStore();
        var input = new NpcStateUpdate(type, checked((short)type), 1000f, 1000f, 0f, 0f, 255,
            default, NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawn(0, in input, out var fresh));
        Assert.Equal(0f, fresh.Simulation.TeleportTime);
        Assert.Equal(0, fresh.Simulation.TeleportStyle);
        Assert.Equal(0, fresh.Simulation.BreathCounter);
        Assert.Equal(0, fresh.Simulation.WetCount);
        Assert.False(fresh.Simulation.LavaWet);
        Assert.False(fresh.Simulation.HoneyWet);
        Assert.False(fresh.Simulation.ShimmerWet);
    }

    [Theory]
    [InlineData(10, null, 0)]
    [InlineData(null, null, null)]
    [InlineData(10, 7, 7)]
    [InlineData(0, null, 0)]
    public void Changed_identity_resets_only_known_unsupplied_wet_cooldown(int? previousCount,
        int? suppliedCount, int? expectedCount)
    {
        var previous = State(Actor(198)) with { Simulation = Actor(198).Simulation with
            { WetCount = previousCount, HoneyWet = true, LavaWet = true,
                TeleportTime = 1f, TeleportStyle = 13 } };
        var update = new NpcStateUpdate(199, 199, 100f, 100f, 0f, 0f, 255, default,
            NpcSimulationState.Initial with { WetCount = suppliedCount });
        var changed = RuntimeNpcStateOwnershipPolicy.PreserveUnownedUpdateState(in update, in previous);
        Assert.Equal(expectedCount, changed.Simulation.WetCount);
        Assert.True(changed.Simulation.HoneyWet);
        Assert.True(changed.Simulation.LavaWet);
        Assert.Equal(1f, changed.Simulation.TeleportTime);
        Assert.Equal(13, changed.Simulation.TeleportStyle);
        var sameInput = update with { Type = 198, NetId = 198,
            Simulation = update.Simulation with { WetCount = null } };
        var same = RuntimeNpcStateOwnershipPolicy.PreserveUnownedUpdateState(in sameInput, in previous);
        Assert.Equal(previousCount, same.Simulation.WetCount);
    }

    [Fact]
    public void Lihzahrd_transform_resets_defaults_before_continuation_and_preserves_source_history()
    {
        var source = Actor(198) with
        {
            Ai = new(1f, 2f, 3f, 4f),
            Simulation = Actor(198).Simulation with
            {
                Life = 211, LifeMax = 400, MoneyValue = 0f, JustHit = true,
                LocalAi = new(5f, 6f, 7f, 8f), FrameCounter = 13d, FrameIndex = 5,
                CollideX = true, CollideY = true, Rotation = 1f, WetCount = 10,
                Wet = true, HoneyWet = true, LavaWet = true, ShimmerWet = false,
                TeleportTime = 1f, TeleportStyle = 13, StairFall = true,
                OldPositionX = 83f, OldPositionY = 97f, OldVelocityX = 2f, OldVelocityY = -3f
            }
        };
        var context = new VanillaNpcSpawnContext(1f, 1, false);
        var targeting = Targeting(new(0, true, false, false, 111.25f, 101f, 20, 42, 0, false, 0));

        Assert.True(RuntimeNpcStateOwnershipPolicy.TryMaterializeTransform(in source, new(199),
            in context, default, targeting, out var result));

        // NPC.Transform/SetTargetTrackingValues 1.4.5.8: bottom alignment, integer
        // target rectangle, AI003 bottom-edge facing, integer health ratio.
        Assert.Equal(199, result.Type);
        Assert.Equal(source.Handle, result.Handle);
        Assert.Equal(source.Revision, result.Revision);
        Assert.Equal(100.25f, result.PositionX);
        Assert.Equal(110.25f, result.PositionY);
        Assert.Equal(new NpcHitboxDimensions(42, 30), result.Simulation.HitboxOverride);
        Assert.Equal(211, result.Simulation.Life);
        Assert.Equal(400, result.Simulation.LifeMax);
        Assert.Equal(60, result.Simulation.DamageOverride);
        Assert.Equal(30, result.Simulation.DefenseOverride);
        Assert.Equal(-1, result.Simulation.DirectionX);
        Assert.Equal(1, result.Simulation.DirectionY);
        Assert.Equal(default, result.Ai);
        Assert.Equal(default, result.Simulation.LocalAi);
        Assert.Equal(0d, result.Simulation.FrameCounter);
        Assert.Equal(0, result.Simulation.FrameIndex);
        Assert.False(result.Simulation.JustHit);
        Assert.False(result.Simulation.CollideX);
        Assert.False(result.Simulation.CollideY);
        Assert.Equal(0f, result.Simulation.Rotation);
        Assert.Equal(0, result.Simulation.WetCount);
        Assert.True(result.Simulation.Wet);
        Assert.True(result.Simulation.HoneyWet);
        Assert.True(result.Simulation.LavaWet);
        Assert.False(result.Simulation.ShimmerWet);
        Assert.Equal(1f, result.Simulation.TeleportTime);
        Assert.Equal(13, result.Simulation.TeleportStyle);
        Assert.True(result.Simulation.StairFall);
        Assert.Equal(0f, result.Simulation.MoneyValue);
        Assert.Equal(83f, result.Simulation.OldPositionX);
        Assert.Equal(97f, result.Simulation.OldPositionY);
        Assert.Equal(2f, result.Simulation.OldVelocityX);
        Assert.Equal(-3f, result.Simulation.OldVelocityY);
        Assert.Equal(source.VelocityX, result.VelocityX);
        Assert.Equal(source.VelocityY, result.VelocityY);
    }

    [Fact]
    public void Fish_transform_uses_destination_catch_protection_and_integer_rectangle_facing()
    {
        var source = Actor(230) with { Simulation = Actor(230).Simulation with
            { Life = 1, LifeMax = 5, HitboxOverride = new(18, 20), Friendly = false } };
        var context = new VanillaNpcSpawnContext(1f, 1, false);
        var targeting = Targeting(new(0, true, false, false, 100.5f, 90.5f, 20, 42, 0, false, 0));
        Assert.True(RuntimeNpcStateOwnershipPolicy.TryMaterializeTransform(in source, new(55),
            in context, default, targeting, out var result));
        Assert.Equal(102.25f, result.PositionY);
        Assert.Equal(new NpcHitboxDimensions(20, 18), result.Simulation.HitboxOverride);
        Assert.Equal(1, result.Simulation.Life);
        Assert.Equal(90, result.Simulation.CatchableNpcTempImmunityCounter);
        Assert.Equal((short)255, result.Simulation.ReleaseOwner);
        Assert.True(result.Simulation.Friendly);
        Assert.Equal(-1, result.Simulation.DirectionX);
        Assert.Equal(-1, result.Simulation.DirectionY);
        Assert.Equal(750, result.Simulation.TimeLeft);
    }

    [Fact]
    public void Materializer_refuses_unknown_value_unowned_raw_geometry_and_unapproved_edges()
    {
        var source = Actor(198);
        var context = new VanillaNpcSpawnContext(1f, 1, false);
        var targeting = Targeting(VanillaNpcRawPlayer1458.Constructor(0));
        var unknown = source with { Simulation = source.Simulation with { MoneyValue = null } };
        Assert.False(RuntimeNpcStateOwnershipPolicy.TryMaterializeTransform(in unknown, new(199),
            in context, default, targeting, out _));
        Assert.False(RuntimeNpcStateOwnershipPolicy.TryMaterializeTransform(in source, new(3),
            in context, default, targeting, out _));
        var noRaw = new VanillaNpcBehaviorContext();
        noRaw.SetCandidates([new(0, 110f, 121f, 0, true, false, false, false)]);
        Assert.False(RuntimeNpcStateOwnershipPolicy.TryMaterializeTransform(in source, new(199),
            in context, default, noRaw, out _));
    }

    [Fact]
    public void Direct_transform_adoption_preserves_continuation_instead_of_normalizing_a_second_time()
    {
        var random = new VanillaUnifiedRandom1458(1458);
        var store = Store(random);
        var input = State(Actor(198));
        Assert.True(store.TrySpawn(0, in input, out var source));
        Assert.True(store.TryCreateAiSpawnPreview(in source, random, out var preview));
        var targeting = Targeting(VanillaNpcRawPlayer1458.Constructor(0));
        Assert.True(preview!.TryStageTransform(in source, new(199), default, targeting, out var checkpoint));
        var continuation = State(checkpoint) with { Ai = new(0f, 7f, 0f, 3f),
            Simulation = checkpoint.Simulation with { FrameIndex = 2, Rotation = .25f, Life = 123 } };
        _ = preview.Random.Next();
        var expectedRandom = preview.Random.Clone();
        Assert.True(preview.TryAdoptDirect(in continuation, static () => true, out var adopted));
        Assert.Equal(checkpoint.Handle, adopted.Handle);
        Assert.Equal(source.Revision.Value + 1, adopted.Revision.Value);
        Assert.Equal(continuation.Ai, adopted.Ai);
        Assert.Equal(2, adopted.Simulation.FrameIndex);
        Assert.Equal(.25f, adopted.Simulation.Rotation);
        Assert.Equal(123, adopted.Simulation.Life);
        Assert.True(random.HasSameState(expectedRandom));
        Assert.False(preview.TryAdoptDirect(in continuation, static () => true, out _));
    }

    [Fact]
    public void Powder_batch_stages_same_generations_atomically_without_new_births_or_random_draws()
    {
        var random = new VanillaUnifiedRandom1458(1458);
        var store = Store(random);
        var firstInput = State(Actor(534));
        var secondInput = State(Actor(687));
        Assert.True(store.TrySpawn(0, in firstInput, out var first));
        Assert.True(store.TrySpawn(1, in secondInput, out var second));
        var working = random.Clone();
        var beforeRandom = random.Clone();
        Assert.True(store.TryPrepareWorldSpawns(random, working, out var batch));
        var targeting = Targeting(VanillaNpcRawPlayer1458.Constructor(0));
        Assert.True(batch!.TryStageTransform(in first, new(441), default, targeting, out var one));
        Assert.True(batch.TryStageTransform(in second, new(683), default, targeting, out var two));
        Assert.True(batch.TryCaptureActive(0, out var staged));
        Assert.Equal(441, staged.Type);
        Assert.True(store.TryGet(first.Handle, out var untouched));
        Assert.Equal(first, untouched);
        Assert.Equal(first.Handle, one.Handle);
        Assert.Equal(second.Handle, two.Handle);
        Assert.Empty(batch.Births.ToArray());
        Assert.True(batch.ValidateContext());
        Assert.True(batch.TryAdoptUnpublished(out var publication));
        Assert.False(publication!.TryPublishNext());
        Assert.True(store.TryGet(first.Handle, out var finalFirst));
        Assert.True(store.TryGet(second.Handle, out var finalSecond));
        Assert.Equal(one, finalFirst);
        Assert.Equal(two, finalSecond);
        Assert.Equal(2, store.ActiveCount);
        Assert.True(random.HasSameState(beforeRandom));
        Assert.True(working.HasSameState(beforeRandom));
    }

    [Fact]
    public void Stale_powder_batch_cannot_adopt_an_earlier_transformation_after_later_slot_change()
    {
        var random = new VanillaUnifiedRandom1458(42);
        var store = Store(random);
        var firstInput = State(Actor(534));
        var secondInput = State(Actor(687));
        Assert.True(store.TrySpawn(0, in firstInput, out var first));
        Assert.True(store.TrySpawn(1, in secondInput, out var second));
        Assert.True(store.TryPrepareWorldSpawns(random, random.Clone(), out var batch));
        var targeting = Targeting(VanillaNpcRawPlayer1458.Constructor(0));
        Assert.True(batch!.TryStageTransform(in first, new(441), default, targeting, out _));
        var changed = State(second) with { PositionX = second.PositionX + 1f };
        Assert.True(store.TryUpdate(second.Handle, in changed, out var newer));
        Assert.False(batch.TryStageTransform(in second, new(683), default, targeting, out _));
        Assert.False(batch.TryAdoptUnpublished(out _));
        Assert.True(store.TryGet(first.Handle, out var untouched));
        Assert.Equal(first, untouched);
        Assert.True(store.TryGet(second.Handle, out var retained));
        Assert.Equal(newer, retained);
    }

    private static NpcSnapshot Actor(int type) => new(new(0, new(1)), new(1), type, (short)type,
        100.25f, 100.25f, .9f, -.25f, 255, default, NpcSimulationState.Initial with
        { Life = 400, LifeMax = 400, MoneyValue = 0f, Scale = 1f, HitboxOverride = new(18, 40),
            DirectionX = 1, DirectionY = 1, SpriteDirection = 1, KnockBackResist = .4f,
            TeleportTime = 0f, TeleportStyle = 0, WetCount = 0,
            HoneyWet = false, LavaWet = false, ShimmerWet = false });

    private static RuntimeNpcStore Store(VanillaUnifiedRandom1458 random)
    {
        var store = new RuntimeNpcStore(4);
        store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
        store.SetVanillaSpawnContextSource(static () => new(1f, 1, false));
        return store;
    }

    private static NpcStateUpdate State(in NpcSnapshot npc) => new(npc.Type, npc.NetId,
        npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);

    private static VanillaNpcBehaviorContext Targeting(VanillaNpcRawPlayer1458 facts)
    {
        var context = new VanillaNpcBehaviorContext();
        context.SetRawPlayerSlots(new Raw(facts));
        if (facts.Active) context.SetCandidates([facts.Candidate]);
        return context;
    }

    private sealed class Raw(VanillaNpcRawPlayer1458 facts) : INpcRawPlayerSlotLookup1458
    {
        public bool TryCapture(byte slot, out NpcRawPlayerSlotSnapshot1458 snapshot)
        {
            snapshot = new(facts, 1, 1, null);
            return slot == facts.Slot;
        }
        public bool IsCurrent(in NpcRawPlayerSlotSnapshot1458 snapshot) => snapshot.Facts == facts;
    }
}
