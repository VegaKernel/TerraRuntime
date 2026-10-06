using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class InvasionDeathCredit1458Tests
{
    [Fact]
    public void Original_deaths_match_owned_loot_credit_and_rng_or_preserve_metadata_only_birth_fences()
    {
        using var stream = typeof(InvasionDeathCredit1458Tests).Assembly.GetManifestResourceStream("InvasionDeathOrder1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        int count = 0;
        int matched = 0;
        int fenced = 0;
        foreach (var row in document.RootElement.EnumerateArray())
        {
            count++;
            if (row.GetProperty("type").GetInt32() == 381)
            {
                AssertMetadataOnlyBirthRefusal(row.GetProperty("seed").GetInt32());
                Assert.Equal(4, row.GetProperty("group").GetInt32());
                Assert.Equal(11, row.GetProperty("after").GetProperty("size").GetInt32());
                fenced++;
                continue;
            }
            var fixture = new PipelineFixture(row.GetProperty("type").GetInt32(), row.GetProperty("group").GetInt32(),
                row.GetProperty("seed").GetInt32(), row.GetProperty("injured").GetBoolean());
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100000));
            var expected = row.GetProperty("frames").EnumerateArray().Select(value => Convert.FromHexString(value.GetString()!)).ToArray();
            var expectedDrops = expected.Where(frame => frame[2] == 21).ToArray();
            Assert.True(expectedDrops.Length == fixture.Drops.Count,
                $"runtimeValue={fixture.Npc.Simulation.MoneyValue} runtimeDamage={fixture.Npc.Simulation.DamageOverride} runtimeBody={fixture.Npc.Simulation.HitboxOverride} type={row.GetProperty("type")} seed={row.GetProperty("seed")} expected={string.Join(",", expectedDrops.Select(raw => $"{BitConverter.ToInt16(raw,25)}:{BitConverter.ToInt16(raw,21)}"))} actual={string.Join(",", fixture.Drops.Select(drop => $"{drop.ItemNetId}:{drop.Stack}"))}");
            for (int index = 0; index < expectedDrops.Length; index++)
            {
                var raw = expectedDrops[index]; var actual = fixture.Drops[index];
                Assert.Equal((BitConverter.ToInt16(raw, 25), BitConverter.ToInt16(raw, 21), raw[23]),
                    (actual.ItemNetId, actual.Stack, actual.Prefix));
                Assert.Equal((BitConverter.ToSingle(raw, 5), BitConverter.ToSingle(raw, 9),
                    BitConverter.ToSingle(raw, 13), BitConverter.ToSingle(raw, 17)),
                    (actual.PositionX, actual.PositionY, actual.VelocityX, actual.VelocityY));
            }
            Assert.Equal(expected[^1], fixture.ProgressFrame);
            Assert.Equal(78, fixture.Events[^2]); Assert.Equal(23, fixture.Events[^1]);
            Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Next());
            Assert.True(fixture.Owner.TryCapture(out var state));
            Assert.Equal(row.GetProperty("after").GetProperty("size").GetInt32(), state.State.Size);
            Assert.False(fixture.Npcs.TryGet(fixture.Npc.Handle, out _));
            matched++;
        }
        Assert.Equal(24, count);
        Assert.Equal(16, matched);
        Assert.Equal(8, fenced);
    }

    private static void AssertMetadataOnlyBirthRefusal(int seed)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(381), new NpcNetId(381), out var definition));
        Assert.True(definition.DefinitionOnly);
        var sink = new Sink();
        var npcs = new RuntimeNpcStore(10, sink);
        var items = new RuntimeWorldItemStore();
        var random = new VanillaUnifiedRandom1458(seed);
        var beforeRandom = random.Clone();
        npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
        int capturedContext = 0;
        npcs.SetVanillaSpawnContextSource(() =>
        {
            capturedContext++;
            return new(1, 1, true);
        });
        var invasion = new RuntimeWorldInvasion1458(State(4));
        Assert.True(invasion.TryCapture(out var beforeInvasion));
        var birth = new NpcStateUpdate(381, 381, 800, 440, 0, 0, 0, default, NpcSimulationState.Initial);

        Assert.False(npcs.TrySpawnVanilla(in birth, out _));

        Assert.Equal(0, capturedContext);
        Assert.Equal(0, npcs.ActiveCount);
        Assert.Equal(0, items.ActiveCount);
        Assert.True(random.HasSameState(beforeRandom));
        Assert.True(invasion.IsCurrent(in beforeInvasion));
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void Actual_lethal_admission_rejects_saturated_event_before_hp_loot_or_rng_changes()
    {
        var fixture = new PipelineFixture(27, 1, 1, true);
        typeof(RuntimeWorldInvasion1458).GetField("revision",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(fixture.Owner, long.MaxValue);
        var randomBefore = fixture.Random.Clone();

        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100000));

        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var unchanged));
        Assert.Equal(fixture.Npc, unchanged);
        Assert.True(fixture.Random.HasSameState(randomBefore));
        Assert.Empty(fixture.Drops);
        Assert.Empty(fixture.Events);
        Assert.Null(fixture.ProgressFrame);
        Assert.True(fixture.Owner.TryCapture(out var capture));
        Assert.Equal(12, capture.State.Size);
        Assert.Equal(long.MaxValue, capture.Revision);
    }

    [Fact]
    public void Actual_loot_callback_cannot_overwrite_newer_event_or_revived_npc()
    {
        foreach (bool reviveNpc in new[] { false, true })
        {
            var fixture = new PipelineFixture(27, 1, 1, true);
            bool invoked = false;
            fixture.OnDrop = () =>
            {
                if (invoked) return;
                invoked = true;
                if (reviveNpc)
                {
                    Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var current));
                    var revived = Update(current) with { Simulation = current.Simulation with { Life = 10 } };
                    Assert.True(fixture.Npcs.TryUpdate(current.Handle, in revived, out _));
                }
                else
                {
                    Assert.True(fixture.Owner.TryCapture(out var before));
                    var newer = new InvasionTransition1458(before.State with { Size = 8 }, default);
                    Assert.True(fixture.Owner.TryAdopt(in before, in newer, out _));
                }
            };
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100000));
            Assert.True(invoked); Assert.Null(fixture.ProgressFrame);
            Assert.True(fixture.Owner.TryCapture(out var state));
            Assert.Equal(reviveNpc ? 12 : 8, state.State.Size);
            Assert.Equal(reviveNpc, fixture.Npcs.TryGet(fixture.Npc.Handle, out var survivor));
            if (reviveNpc) Assert.Equal(10, survivor.Simulation.Life);
        }
    }

    [Fact]
    public void Actual_progress_callback_replacement_skips_the_old_terminal_packet()
    {
        var fixture = new PipelineFixture(27, 1, 1, true);
        NpcSnapshot replacement = default;
        fixture.OnProgress = () =>
        {
            var update = Update(fixture.Npc) with { Simulation = fixture.Npc.Simulation with { Life = 40 } };
            Assert.True(fixture.Npcs.TrySpawn(fixture.Npc.Handle.Slot, in update, out replacement));
        };
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
            fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100000));
        Assert.True(fixture.Npcs.TryGet(replacement.Handle, out var live));
        Assert.Equal(40, live.Simulation.Life);
        Assert.DoesNotContain(fixture.Terminals, handle => handle == fixture.Npc.Handle);
        Assert.True(fixture.Owner.TryCapture(out var state)); Assert.Equal(11, state.State.Size);
    }

    private static NpcStateUpdate Update(in NpcSnapshot npc) => new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY,
        npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);

    private sealed class PipelineFixture : IRuntimePlayerSlotSnapshotLookup, INpcStateCommitSink, IWorldItemStateCommitSink
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeNpcNetworkCombatPipeline Pipeline;
        internal readonly RuntimeWorldInvasion1458 Owner;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly NpcSnapshot Npc;
        internal readonly List<int> Events = [];
        internal readonly List<WorldItemSnapshot> Drops = [];
        internal readonly List<NpcHandle> Terminals = [];
        internal byte[]? ProgressFrame;
        internal Action? OnDrop;
        internal Action? OnProgress;
        private readonly PlayerStateSnapshot player;

        internal PipelineFixture(int type, int group, int seed, bool injured)
        {
            Owner = new(State(group)); Random = new(seed);
            Npcs = new(10, this); var items = new RuntimeWorldItemStore(this);
            player = default(PlayerStateSnapshot) with
            {
                Player = new(new PlayerSlotId(0), new PlayerSessionGeneration(1)), Revision = new(1),
                Zones = new(0, 0, 0, 0, 0, 0),
                HasHealth = true, Life = injured ? (short)100 : (short)400, MaxLife = 400, DerivedLifeMax = 400,
                HasMana = true, Mana = injured ? (short)20 : (short)200, MaxMana = 200, NpcLifeCurrent = true
            };
            var update = new NpcStateUpdate(type, checked((short)type), 800, 440, 0, 0, 0, default,
                NpcSimulationState.Initial);
            Assert.True(Npcs.TrySpawnVanilla(in update, out Npc), $"Source actor {type} must have owned vanilla creation.");
            Pipeline = new(Npcs, items, this, new PlayerAuthority(null, null), () => 0,
                null, new(items), null, new(0, true, 0, 0, 0), new(), false, false,
                lootRandom: Random, lootRemixWorld: false,
                globalLootWorld: new(100, 80, 1, false, false, false, false, 35, 40, false, false),
                invasion: Owner, invasionProgressPublisher: accepted =>
                {
                    Assert.False(Npcs.TryGet(Npc.Handle, out _));
                    var progress = new TerrariaInvasionProgressState(accepted.State.Progress, accepted.State.ProgressMax,
                        (sbyte)accepted.State.ProgressIcon, (sbyte)accepted.State.ProgressWave);
                    Assert.True(TerrariaInvasionProgressCodec.TryEncode(in progress, out var frame));
                    ProgressFrame = frame; Events.Add(78); OnProgress?.Invoke();
                });
            Events.Clear();
        }

        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        { snapshot = player; return slot.Value == 0; }

        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc)
        {
            Events.Add(23);
            if (kind == NpcStateCommitKind.Despawn) Terminals.Add(npc.Handle);
        }

        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item)
        {
            if (kind != WorldItemStateCommitKind.Drop) return;
            Drops.Add(item); Events.Add(21); OnDrop?.Invoke();
        }
    }
}
