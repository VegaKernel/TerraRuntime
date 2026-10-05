using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class TorchSlimeStatus1458Tests
{
    public static IEnumerable<object[]> WholeSequences()
    {
        using var stream = typeof(TorchSlimeStatus1458Tests).Assembly.GetManifestResourceStream("TorchSlimeStatus1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(WholeSequences))]
    public void Complete_original_updates_preserve_status_before_ai_refresh_motion_and_rng(string json)
    {
        using var document = JsonDocument.Parse(json);
        var input = document.RootElement.GetProperty("inputs");
        using var fixture = new Fixture(input);
        Span<TerrariaNpcBuffEntryState> actual = stackalloc TerrariaNpcBuffEntryState[20];
        foreach (var row in document.RootElement.GetProperty("sequence").EnumerateArray())
        {
            fixture.Frames.Clear();
            fixture.Status.BeginWorldTick();
            Assert.Equal(1, fixture.Executor.Tick(fixture.Stepper).Applied);
            Assert.True(fixture.Store.TryGet(fixture.Parent.Handle, out var npc));
            Assert.Equal(row.GetProperty("life").GetInt32(), npc.Simulation.Life);
            Assert.Equal(row.GetProperty("count").GetInt32(), npc.Simulation.LifeRegenCounter);
            Assert.Equal((row.GetProperty("x").GetSingle(), row.GetProperty("y").GetSingle(),
                row.GetProperty("vx").GetSingle(), row.GetProperty("vy").GetSingle()),
                (npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY));
            Assert.Equal(row.GetProperty("ai").EnumerateArray().Select(v => v.GetSingle()),
                new[] { npc.Ai.Ai0, npc.Ai.Ai1, npc.Ai.Ai2, npc.Ai.Ai3 });
            Assert.True(fixture.Status.TryGetDebuffs(npc.Handle, out var flags));
            Assert.Equal(row.GetProperty("fire").GetBoolean(), flags.OnFire);
            Assert.Equal(row.GetProperty("poison").GetBoolean(), flags.Poisoned);
            Assert.True(fixture.Status.TryCopyWireBuffs(npc.Handle, actual, out int count));
            var types = row.GetProperty("types").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var times = row.GetProperty("times").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var expected = Enumerable.Range(0, 20).Where(i => types[i] > 0 && times[i] > 0)
                .Select(i => new TerrariaNpcBuffEntryState((ushort)types[i], (ushort)times[i]));
            Assert.Equal(expected, actual[..count].ToArray());
            BuffTypeId[] immunities = [VanillaBuffIds.CursedInferno, VanillaBuffIds.Frostburn,
                VanillaBuffIds.OnFire3, VanillaBuffIds.Frostburn2];
            for (int i = 0; i < immunities.Length; i++)
            {
                Assert.True(fixture.Status.TryGetTorchImmunity(npc.Handle, immunities[i], out bool immune));
                Assert.Equal(row.GetProperty("immunity")[i].GetBoolean(), immune);
            }
            Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(v => v.GetString()!)
                .Where(hex => Convert.FromHexString(hex)[2] == 54), fixture.Frames);
            Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.SourceRandom.Clone().Next());
        }
    }

    private sealed class Fixture : IDisposable, IRuntimePlayerSlotSnapshotLookup
    {
        internal readonly RuntimeNpcStore Store = new();
        internal readonly SystemVanillaNpcRandom Random = new(1458);
        internal readonly RuntimeNpcBuffStatus1458 Status;
        internal readonly RuntimeNpcAiStateExecutor Executor;
        internal readonly INpcAiStateStepper Stepper;
        internal readonly NpcSnapshot Parent;
        internal readonly List<string> Frames = [];
        internal Action? OnCapture;

        internal Fixture(JsonElement input)
        {
            var tiles = new WorldTileStore(new(100, 80));
            Assert.True(tiles.TryAttachWorldSurface(40d));
            for (int x = 0; x < 100; x++) tiles.Set(x, 30, new() { Flags = WorldTileFlags.Active, Type = 1 });
            bool wet = input.GetProperty("wet").GetBoolean();
            bool lavaWet = input.GetProperty("lavaWet").GetBoolean();
            if (wet)
                for (int x = 49; x <= 52; x++)
                    for (int y = 28; y <= 29; y++) tiles.Set(x, y, new() { LiquidAmount = 255 });
            Status = new(Store, handle =>
            {
                Span<TerrariaNpcBuffEntryState> buffs = stackalloc TerrariaNpcBuffEntryState[20];
                Assert.True(Status!.TryCopyWireBuffs(handle, buffs, out int count));
                Assert.True(TerrariaNpcBuffCodec.TryEncodeCurrent(handle.Slot, buffs[..count], out var frame));
                Frames.Add(Convert.ToHexString(frame));
            });
            int height = input.GetProperty("height").GetInt32();
            int type = input.GetProperty("type").GetInt32();
            bool good = input.GetProperty("good").GetBoolean();
            var state = new NpcStateUpdate(type, (short)type, 800, 480 - height, 0, 0, 0,
                new(0, input.GetProperty("held").GetInt32(), 0, 0), NpcSimulationState.Initial with
                {
                    Life = input.GetProperty("life").GetInt32(), LifeMax = 1000,
                    LifeRegenCounter = input.GetProperty("counter").GetInt32(), Wet = wet,
                    LiquidContact = lavaWet ? NpcLiquidContactKind.Lava : wet ? NpcLiquidContactKind.Water : NpcLiquidContactKind.None,
                    HitboxOverride = new(input.GetProperty("width").GetInt32(), height),
                    Scale = input.GetProperty("scale").GetSingle(),
                    DamageOverride = input.GetProperty("damage").GetInt32(),
                    DefenseOverride = input.GetProperty("defense").GetInt32(),
                    MoneyValue = input.GetProperty("value").GetSingle(),
                    Alpha = input.GetProperty("alpha").GetInt32(),
                    KnockBackResist = input.GetProperty("kb").GetSingle(), DirectionX = 1, DirectionY = 1
                });
            Assert.True(Store.TrySpawn(0, in state, out Parent));
            Store.SetVanillaSpawnRandomSource(Random);
            var targeting = new VanillaNpcTargetingAiStepper(new Rejecting(), random: Random);
            targeting.EnableBlueSlimeMotion(40);
            targeting.SetCandidates([new(0, 830, 459, 0, true, false, false, false)]);
            targeting.SetPlayerSnapshotLookup(this);
            targeting.SetSlimeContainedOwner(Store, () =>
            {
                OnCapture?.Invoke();
                return new(40, 35, good, false, false, false,
                    false, false, false, false, false, false, false, false, false, (int)VanillaMoonPhase.Full);
            },
                new VanillaSlimeContainedWorld1458(tiles));
            targeting.SetSlimeStatusOwner(Status);
            Stepper = new RuntimeNpcBuffAiStepper1458(new VanillaNpcWorldMotionAiStepper(targeting, tiles),
                Status, Random, good, retainedSlimeStatuses: true);
            Executor = new(Store);
            int buff = input.GetProperty("buff").GetInt32();
            int duration = input.GetProperty("duration").GetInt32();
            foreach (int identity in buff == 2024 ? new[] { 20, 24 } : new[] { buff })
                if (identity > 0) Assert.True(Status.TryApply(Parent.Handle, new BuffTypeId(identity), duration));
        }

        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        {
            player = default(PlayerStateSnapshot) with
            {
                Player = new(new(0), new(1)), Revision = new(1), PositionX = 820, PositionY = 438,
                ItemAnimation = 0, Stealth = 1, BaseLifeMax = 100, DerivedLifeMax = 100
            };
            return slot.Value == 0;
        }

        public void Dispose() { }
    }

    [Theory]
    [InlineData("status")]
    [InlineData("npc")]
    [InlineData("random")]
    [InlineData("late-status")]
    public void Reentrant_fact_capture_rejects_stale_phase_without_aging_or_adopting_random(string mutation)
    {
        using var document = JsonDocument.Parse((string)WholeSequences().First()[0]);
        using var fixture = new Fixture(document.RootElement.GetProperty("inputs"));
        var before = fixture.Parent;
        int expectedNext = fixture.Random.SourceRandom.Clone().Next();
        int captures = 0;
        fixture.OnCapture = () =>
        {
            if (mutation == "late-status" && ++captures == 1) return;
            fixture.OnCapture = null;
            if (mutation is "status" or "late-status")
                Assert.True(fixture.Status.TryApply(before.Handle, VanillaBuffIds.OnFire, 5));
            else if (mutation == "npc")
            {
                var same = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
                    before.VelocityX, before.VelocityY, before.Target, before.Ai, before.Simulation);
                Assert.True(fixture.Store.TryUpdate(before.Handle, in same, out _));
            }
            else
            {
                fixture.Random.SourceRandom.Next();
                expectedNext = fixture.Random.SourceRandom.Clone().Next();
            }
        };
        fixture.Frames.Clear();
        Assert.Equal(0, fixture.Executor.Tick(fixture.Stepper).Applied);
        Assert.Equal(expectedNext, fixture.Random.SourceRandom.Clone().Next());
        Assert.True(fixture.Store.TryGet(before.Handle, out var after));
        Assert.Equal(before.Ai, after.Ai);
        Assert.Equal(before.Simulation, after.Simulation);
        Span<TerrariaNpcBuffEntryState> buffs = stackalloc TerrariaNpcBuffEntryState[20];
        Assert.True(fixture.Status.TryCopyWireBuffs(before.Handle, buffs, out int count));
        Assert.Equal(mutation is "status" or "late-status" ? 1 : 0, count);
        if (count == 1) Assert.Equal(new TerrariaNpcBuffEntryState(24, 5), buffs[0]);
        Assert.Empty(fixture.Frames);
    }

    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
}
