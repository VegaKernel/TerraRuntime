using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class SlimeDebuffDeath1458Tests
{
    public static IEnumerable<object[]> OriginalDeaths()
    {
        using var stream = typeof(SlimeDebuffDeath1458Tests).Assembly.GetManifestResourceStream("SlimeDebuffDeath1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalDeaths))]
    public void Actual_outer_debuff_death_matches_original_drops_strike_expiry_and_final_rng(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var fixture = new Fixture(row.GetProperty("seed").GetInt32(), row.GetProperty("held").GetInt32(),
            row.GetProperty("duration").GetInt32());
        Assert.Equal(1, fixture.Executor.Tick(fixture.Stepper).Applied);
        Assert.False(fixture.Npcs.TryGet(fixture.Parent.Handle, out _));
        Assert.False(row.GetProperty("active").GetBoolean());
        var drops = new WorldItemSnapshot[fixture.Items.Capacity];
        int count = fixture.Items.CopyActive(drops);
        Assert.Equal(row.GetProperty("drops").GetArrayLength(), count);
        int index = 0;
        foreach (var expected in row.GetProperty("drops").EnumerateArray())
        {
            var actual = drops[index++];
            Assert.Equal(expected.GetProperty("slot").GetInt32(), actual.Handle.Slot);
            Assert.Equal(expected.GetProperty("id").GetInt32(), actual.ItemNetId);
            Assert.Equal(expected.GetProperty("stack").GetInt32(), actual.Stack);
            Assert.Equal(expected.GetProperty("prefix").GetInt32(), actual.Prefix);
            Assert.Equal((expected.GetProperty("x").GetSingle(), expected.GetProperty("y").GetSingle(),
                expected.GetProperty("vx").GetSingle(), expected.GetProperty("vy").GetSingle()),
                (actual.PositionX, actual.PositionY, actual.VelocityX, actual.VelocityY));
        }
        // These source frames expose expiry-before-strike, including the genuine 9999 argument.
        Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(v => v.GetString()!)
            .Where(hex => Convert.FromHexString(hex)[2] is 54 or 28),
            fixture.Drain().Where(bytes => bytes[2] is 54 or 28).Select(Convert.ToHexString));
        Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Clone().Next());
    }

    [Fact]
    public void Held_allocation_refuses_whole_lethal_prepass_then_retry_commits_once()
    {
        var fixture = new Fixture(1458, 3347, 1);
        Assert.True(fixture.Items.TryReserveDropSlot(out var held));
        var random = fixture.Random.Clone();
        Assert.Equal(0, fixture.Executor.Tick(fixture.Stepper).Applied);
        Assert.True(fixture.Npcs.TryGet(fixture.Parent.Handle, out var retained));
        Assert.Equal(fixture.Parent, retained);
        Assert.True(random.HasSameState(fixture.Random));
        Assert.Empty(fixture.Drain());
        Span<TerrariaNpcBuffEntryState> buffs = stackalloc TerrariaNpcBuffEntryState[20];
        Assert.True(fixture.Status.TryCopyWireBuffs(retained.Handle, buffs, out int count));
        Assert.Equal(new TerrariaNpcBuffEntryState(24, 1), Assert.Single(buffs[..count].ToArray()));
        Assert.True(fixture.Items.TryReleaseDropReservation(in held));
        Assert.Equal(1, fixture.Executor.Tick(fixture.Stepper).Applied);
        Assert.Equal(0, fixture.Executor.Tick(fixture.Stepper).Applied);
        Assert.False(fixture.Npcs.TryGet(retained.Handle, out _));
        Assert.Single(fixture.Drain(), bytes => bytes[2] == 28);
    }

    private sealed class Fixture : IRuntimePlayerSlotSnapshotLookup
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeNpcBuffStatus1458 Status;
        internal readonly RuntimeNpcAiStateExecutor Executor;
        internal readonly INpcAiStateStepper Stepper;
        internal readonly NpcSnapshot Parent;
        private readonly TerrariaConnectionOutboundQueue outbound;

        internal Fixture(int seed, int held, int duration)
        {
            Random = new(seed);
            var registry = new RuntimeNpcReplicationRegistry();
            Npcs = new(commitSink: registry);
            var random = new SystemVanillaNpcRandom(Random);
            Npcs.SetVanillaSpawnRandomSource(random);
            Assert.True(Npcs.TrySpawn(0, new(1, 1, 800, 462, 0, 0, 0, new(0, held, 0, 0),
                NpcSimulationState.Initial with { Life = 1, LifeMax = 25, LifeRegenCounter = -119,
                    Immortal = false, MoneyValue = 0, ExtraMoneyValue = 0 }), out Parent));
            Status = new(Npcs, registry.PublishNpcBuffs);
            registry.BindNpcBuffStatus(Status);
            Assert.True(Status.TryApply(Parent.Handle, VanillaBuffIds.OnFire, duration));
            var pipeline = new RuntimeNpcNetworkCombatPipeline(Npcs, Items, this,
                new PlayerAuthority(null, null), () => 0, registry, new(Items), null,
                new RuntimeWorldClock(0, true, 0, 0, 1), new(), false, false,
                lootRandom: Random, seasonalItemContext: () => default);
            Stepper = new RuntimeNpcBuffAiStepper1458(new Rejecting(), Status, random,
                retainedSlimeStatuses: true, debuffDeath: pipeline.TryStrikeSlimeDebuffDeath);
            Executor = new(Npcs);
            outbound = new(new OutboundQueueOptions(64, 65536, 1024));
            var source = GameCommandSourceId.FromConnection(91458);
            Assert.True(registry.TryRegister(source, outbound));
            var connection = new ConnectionHandle(source, new(new(0), new(1)));
            registry.PlayerSpawned(connection, new(new(0), 20, 20, 0, 0, 0, 0, 0));
            _ = Drain();
        }

        internal byte[][] Drain()
        {
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
            var frames = new List<byte[]>();
            while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
            return frames.ToArray();
        }

        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        {
            player = default(PlayerStateSnapshot) with { Player = new(new(0), new(1)), Revision = new(1),
                PositionX = 820, PositionY = 438, HasHealth = true, Life = 400, MaxLife = 400,
                HasMana = true, Mana = 200, MaxMana = 200 };
            return slot.Value == 0;
        }

        private sealed class Rejecting : INpcAiStateStepper
        {
            public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
            { next = default; return false; }
        }
    }
}
