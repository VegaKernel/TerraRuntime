using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed partial class SkeletronHeadEffectsTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Reentrant_summon_query_cannot_create_for_a_newer_head(bool replace)
    {
        var row = EffectRow(1, 200, true);
        var (npcs, projectiles, ai, head, random, taunts) = Setup(row, Worlds[0]);
        ai.SetSkeletronEnvironment(new ReentrantEnvironment(() => Supersede(npcs, head, replace)));
        new RuntimeNpcAiStateExecutor(npcs, projectiles, taunts: taunts).Tick(new HeadOnly(ai));
        Assert.Equal(1, npcs.ActiveCount); Assert.Empty(taunts.Variants);
        var expected = new CapturedRandom(row.GetProperty("randomBefore"));
        expected.NextInt32(-50, 51); expected.NextInt32(-50, 51); random.AssertSame(expected);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NewNPC_prefix_reentry_is_rejected_before_allocating_or_publishing_the_caster(bool replace)
    {
        var row = EffectRow(1, 200, true);
        var (npcs, projectiles, ai, head, random, taunts) = Setup(row, Worlds[0]);
        random.OnDraw = call => { if (call == 3) Supersede(npcs, head, replace); };
        new RuntimeNpcAiStateExecutor(npcs, projectiles, taunts: taunts).Tick(new HeadOnly(ai));
        Assert.Equal(3, random.Calls); Assert.Equal(1, npcs.ActiveCount);
        random.AssertState(row.GetProperty("randomAfter"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Taunt_RNG_reentry_cannot_announce_for_a_newer_head(bool replace)
    {
        var row = EffectRow(0, 799, false);
        var (npcs, projectiles, ai, head, random, taunts) = Setup(row, Worlds[0]);
        random.FirstDraw = () => Supersede(npcs, head, replace);
        new RuntimeNpcAiStateExecutor(npcs, projectiles, taunts: taunts).Tick(new HeadOnly(ai));
        Assert.Empty(taunts.Variants); Assert.Equal(1, random.Calls);
        random.AssertState(row.GetProperty("randomAfter"));
    }

    [Fact]
    public void Taunt_reaches_only_playing_recipients_through_the_application_registry()
    {
        var row = EffectRow(0, 799, false);
        var (npcs, projectiles, ai, _, random, _) = Setup(row, Worlds[0]);
        var replication = new RuntimeNpcReplicationRegistry();
        var playing = new TerrariaConnectionOutboundQueue(new(16, 16384, 1024));
        var joining = new TerrariaConnectionOutboundQueue(new(16, 16384, 1024));
        var source = GameCommandSourceId.FromConnection(1);
        Assert.True(replication.TryRegister(source, playing));
        Assert.True(replication.TryRegister(GameCommandSourceId.FromConnection(2), joining));
        var player = new ConnectionHandle(source, new(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        var spawn = new PlayerSpawnCommitRequest(player.Player.Slot, 100, 200, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(player, in spawn);
        new RuntimeNpcAiStateExecutor(npcs, projectiles, taunts: replication).Tick(new HeadOnly(ai));
        Assert.Equal(1, playing.QueuedFrames); Assert.Equal(0, joining.QueuedFrames);
        var reader = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(playing));
        Assert.True(reader.TryRead(out var frame));
        var expected = new CapturedRandom(row.GetProperty("randomBefore")); int variant = expected.NextInt32(2, 6);
        Assert.Equal(Packets.Single(p => p.GetProperty("taunt").GetInt32() == variant).GetProperty("hex").GetString(),
            Convert.ToHexString(frame.Bytes.Span));
        random.AssertState(row.GetProperty("randomAfter"));
    }

    [Theory]
    [InlineData(1)] [InlineData(6)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void Invalid_taunt_variants_do_not_publish(int variant)
    {
        var row = EffectRow(0, 799, false); var (npcs, projectiles, _, head, _, taunts) = Setup(row, Worlds[0]);
        var sink = (INpcAiCommittedNpcMutationSink)new RuntimeNpcAiStateExecutor(npcs, projectiles, taunts: taunts);
        Assert.False(sink.TryAnnounceSkeletronTaunt(in head, variant)); Assert.Empty(taunts.Variants);
        Assert.False(TerraRuntime.Protocol.Multiplicity.TerrariaSkeletronTauntCodec1458.TryEncode(variant, out var bytes));
        Assert.Empty(bytes);
    }

    [Fact]
    public void Stale_scoped_NPC_creation_rejects_before_context_or_RNG_callbacks()
    {
        var row = EffectRow(1, 200, true); var (npcs, _, _, head, random, _) = Setup(row, Worlds[0]);
        Supersede(npcs, head, false);
        var sink = (INpcAiCommittedNpcMutationSink)new RuntimeNpcAiStateExecutor(npcs);
        Assert.False(sink.TrySpawn(in head, new(VanillaNpcIds.DarkCaster, 1000, 1000, 0, 0, 255), out _));
        Assert.Equal(0, random.Calls); random.AssertState(row.GetProperty("randomBefore"));
    }

    private static JsonElement EffectRow(int phase, int timer, bool good) => Rows.First(row =>
        row.GetProperty("phase").GetInt32() == phase && row.GetProperty("timer").GetInt32() == timer &&
        row.GetProperty("good").GetBoolean() == good && row.GetProperty("hands").GetInt32() == 0 &&
        row.GetProperty("before").GetProperty("ai")[3].GetSingle() == 1);

    private static void Supersede(RuntimeNpcStore store, NpcSnapshot head, bool replace)
    {
        Assert.True(store.TryGetActive(head.Handle.Slot, out var current));
        var update = new NpcStateUpdate(current.Type, current.NetId, 777, current.PositionY, 0, 0, current.Target, current.Ai, current.Simulation);
        if (replace)
        {
            Assert.True(store.TryDespawn(current.Handle));
            Assert.True(store.TrySpawn(current.Handle.Slot, in update, out _));
        }
        else Assert.True(store.TryUpdate(current.Handle, in update, out _));
    }

    private sealed class ReentrantEnvironment(Action callback) : IVanillaSkeletronEnvironment
    {
        public bool TryFindCasterSpawn(float centerX, float centerY, IVanillaNpcRandom random, out int x, out int y)
        {
            random.NextInt32(-50, 51); random.NextInt32(-50, 51);
            callback(); x = 1000; y = 2000; return true;
        }
    }
}
