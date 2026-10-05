using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RawFloatingEye1458Tests
{
    public static IEnumerable<object[]> Cases() => RawFlightFixture1458.Cases("RawEyeFirst1458");

    [Theory]
    [MemberData(nameof(Cases))]
    public void Original_NewNPC_raw_target_private_phase_and_full_UpdateNPC_are_preserved(int index, string capturedJson)
    {
        _ = index;
        using var document = JsonDocument.Parse(capturedJson);
        var row = document.RootElement;
        var fixture = new RawFlightFixture1458(row, bee: false);
        var before = fixture.Before;
        if (row.GetProperty("full").GetBoolean())
        {
            fixture.Executor.Tick(fixture.Motion);
            var actual = fixture.Commits.Events[^1].State;
            RawFlightFixture1458.AssertState(row.GetProperty("after"), in actual);
            Assert.Equal(row.GetProperty("after").GetProperty("timeLeft").GetInt32(), actual.Simulation.TimeLeft);
            Assert.Equal(row.GetProperty("rotation").GetSingle(), actual.Simulation.Rotation);
            Assert.Equal(row.GetProperty("afterSprite").GetInt32(), actual.Simulation.SpriteDirection);
            bool active = row.GetProperty("after").GetProperty("active").GetBoolean();
            Assert.Equal(active, fixture.Store.TryGet(before.Handle, out _));
            Assert.Equal(active ? 1 : 2, fixture.Commits.Events.Count);
            Assert.Equal(!row.GetProperty("noSpawnCycle").GetBoolean(), fixture.Cycle.TryBeginCycle());
            Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.SourceRandom.Next());
        }
        else
        {
            Assert.True(fixture.Targeting.TryStepState(in before, out var placeholder));
            Assert.True(fixture.Store.TryUpdateUnpublished(before.Handle, in placeholder, out var accepted));
            Assert.True(fixture.Targeting.TryGetFlyingEyeRetainedPlan(in before, in accepted, fixture.Executor, out var planned));
            var actual = RawFlightFixture1458.Snapshot(in accepted, in planned);
            RawFlightFixture1458.AssertState(row.GetProperty("after"), in actual);
            Assert.Equal(row.GetProperty("rotation").GetSingle(), actual.Simulation.Rotation);
        }
    }
}

internal sealed class RawFlightFixture1458
{
    internal readonly RuntimeNpcStore Store;
    internal readonly SystemVanillaNpcRandom Random;
    internal readonly VanillaNpcTargetingAiStepper Targeting;
    internal readonly RuntimeNpcAiStateExecutor Executor;
    internal readonly VanillaNpcWorldMotionAiStepper Motion;
    internal readonly RuntimeNpcSpawnCycle1458 Cycle = new();
    internal readonly CommitRecorder Commits = new();
    internal readonly RuntimeNpcRawPlayerSlots1458 Raw;
    internal readonly Players Lookup;
    internal NpcSnapshot Before;

    internal RawFlightFixture1458(JsonElement row, bool bee)
    {
        bool good = row.GetProperty("good").GetBoolean();
        Random = new(row.TryGetProperty("seed", out var seed) ? seed.GetInt32() : 1458);
        Store = new(200, Commits);
        Store.SetVanillaSpawnRandomSource(Random);
        Store.SetVanillaSpawnContextSource(() => new(good ? 2f : 1f, 1, good));
        var captured = row.GetProperty("birth");
        var state = ReadState(captured, row.GetProperty("scale").GetSingle(),
            row.GetProperty("difficulty").GetSingle(), row.GetProperty("sprite").GetInt32());
        if (bee)
            state = state with { Simulation = state.Simulation with
            {
                OldVelocityX = row.GetProperty("oldVelocityX").GetSingle(),
                OldVelocityY = row.GetProperty("oldVelocityY").GetSingle()
            } };
        // Genuine NewNPC owns the GoodWorld creation offer before AI; its stream checkpoint
        // is asserted independently. Captured live instance facts are the oracle input.
        Assert.True(Store.TrySpawnVanillaPendingAtBottomCenter(in state, 1000, 1280, out var born));
        Assert.Equal(row.GetProperty("afterBirthNext").GetInt32(), Random.SourceRandom.Clone().Next());
        Assert.True(Store.TryUpdateUnpublished(born.Handle, in state, out Before));
        if (row.TryGetProperty("peer", out var peer))
        {
            var peerState = ReadState(peer.GetProperty("state"), peer.GetProperty("scale").GetSingle(),
                peer.GetProperty("difficulty").GetSingle(), -1);
            Assert.True(Store.TrySpawn(1, in peerState, out _));
        }
        Lookup = new(row.GetProperty("layout").GetInt32(), bee);
        Raw = new(Lookup);
        if (Lookup.Exists)
            Assert.True(Raw.TryAttach(Lookup.Player.Player));
        Targeting = new(new Rejecting(), random: Random);
        Targeting.SetWorldBounds(600, 140, 200, 500);
        Targeting.SetWorldConditions(bee || row.GetProperty("day").GetBoolean(), false, good, false, false);
        Targeting.SetPlayerSnapshotLookup(Lookup);
        Targeting.SetRawPlayerSlots(Raw);
        Targeting.SetCandidates(Lookup.Exists ? [Lookup.Candidate] : []);
        var tiles = new WorldTileStore(new(600, 500));
        Assert.True(tiles.TryAttachWorldSurface(140));
        for (int x = 0; x < 600; x++)
            tiles.Set(x, 80, new() { Flags = WorldTileFlags.Active, Type = 1 });
        var world = new VanillaFlyingEyeWorldEnvironment(tiles);
        Targeting.SetFlyingEyeEnvironment(world);
        if (bee)
            Targeting.SetBeeOwner(Store, world);
        Executor = new(Store, sourceSpawnCycle: Cycle);
        Motion = new(Targeting, tiles, 140);
        Commits.Events.Clear();
    }

    internal static IEnumerable<object[]> Cases(string resource)
    {
        using var input = typeof(RawFlightFixture1458).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        int index = 0;
        foreach (var row in json.RootElement.EnumerateArray())
            yield return [index++, row.GetRawText()];
    }

    internal static NpcStateUpdate ReadState(JsonElement state, float scale, float difficulty, int sprite) =>
        new(state.GetProperty("type").GetInt32(), checked((short)state.GetProperty("net").GetInt32()),
            state.GetProperty("x").GetSingle(), state.GetProperty("y").GetSingle(),
            state.GetProperty("vx").GetSingle(), state.GetProperty("vy").GetSingle(),
            checked((ushort)state.GetProperty("target").GetInt32()), Ai(state.GetProperty("ai")),
            NpcSimulationState.Initial with
            {
                Life = state.GetProperty("life").GetInt32(), LifeMax = state.GetProperty("lifeMax").GetInt32(),
                BaseDamage = state.GetProperty("damage").GetInt32(), BaseDefense = state.GetProperty("defense").GetInt32(),
                DamageOverride = state.GetProperty("damage").GetInt32(), DefenseOverride = state.GetProperty("defense").GetInt32(),
                Alpha = state.GetProperty("alpha").GetInt32(), MoneyValue = state.GetProperty("value").GetSingle(),
                TimeLeft = state.GetProperty("timeLeft").GetInt32(), Scale = scale, SpawnDifficulty = difficulty,
                DirectionX = state.GetProperty("direction").GetInt32(), DirectionY = state.GetProperty("directionY").GetInt32(),
                SpriteDirection = sprite, LocalAi = Ai(state.GetProperty("localAi")), NoGravity = true,
                CollideX = state.GetProperty("collideX").GetBoolean(), CollideY = state.GetProperty("collideY").GetBoolean(),
                Wet = state.GetProperty("wet").GetBoolean(),
                HitboxOverride = new(state.GetProperty("width").GetInt32(), state.GetProperty("height").GetInt32()),
                Friendly = false, Chaseable = true, Immortal = false
            });

    internal static NpcSnapshot Snapshot(in NpcSnapshot identity, in NpcStateUpdate update) =>
        new(identity.Handle, identity.Revision, update.Type, update.NetId, update.PositionX, update.PositionY,
            update.VelocityX, update.VelocityY, update.Target, update.Ai, update.Simulation);

    private static NpcAiState Ai(JsonElement ai) => new(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle());

    internal static void AssertState(JsonElement expected, in NpcSnapshot actual)
    {
        Assert.Equal(expected.GetProperty("x").GetSingle(), actual.PositionX);
        Assert.Equal(expected.GetProperty("y").GetSingle(), actual.PositionY);
        Assert.Equal(expected.GetProperty("vx").GetSingle(), actual.VelocityX);
        Assert.Equal(expected.GetProperty("vy").GetSingle(), actual.VelocityY);
        Assert.Equal(expected.GetProperty("target").GetInt32(), actual.Target);
        Assert.Equal(expected.GetProperty("direction").GetInt32(), actual.Simulation.DirectionX);
        Assert.Equal(expected.GetProperty("directionY").GetInt32(), actual.Simulation.DirectionY);
        Assert.Equal(expected.GetProperty("life").GetInt32(), actual.Simulation.Life);
        Assert.Equal(expected.GetProperty("lifeMax").GetInt32(), actual.Simulation.LifeMax);
        Assert.Equal(expected.GetProperty("alpha").GetInt32(), actual.Simulation.Alpha);
        Assert.Equal(Ai(expected.GetProperty("ai")), actual.Ai);
    }

    internal sealed class Players : IRuntimePlayerSlotSnapshotLookup
    {
        internal readonly bool Exists;
        internal PlayerStateSnapshot Player;
        internal Players(int layout, bool bee)
        {
            Exists = bee ? layout is 1 or 2 : layout != 0;
            Player = new(new(new(0), new(1)), new(1), 0, 0, 0, 0, 0, 0,
                bee ? 1400.75f : 1400f, bee ? 1238.5f : 1238f, 0, 0, 0, 0, 0, 0, 0, 0, 0)
            {
                IsDead = layout >= 2, ItemAnimation = 0,
                Zones = new(0, 0, 0, (byte)(!bee && layout == 3 ? 64 : 0), 0, 0)
            };
        }
        internal VanillaNpcTargetCandidate Candidate => new(0, Player.PositionX + 10, Player.PositionY + 21,
            0, true, Player.IsDead, false, false);
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        {
            player = Player;
            return Exists && slot.Value == 0;
        }
    }

    internal sealed class CommitRecorder : INpcStateCommitSink, INpcBirthRetentionSink
    {
        internal List<(NpcStateCommitKind Kind, NpcSnapshot State)> Events { get; } = [];
        internal RuntimeNpcReplicationRegistry? Replication;
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot state)
        {
            Events.Add((kind, state));
            Replication?.NpcStateCommitted(kind, in state);
        }
        public void NpcBirthRetained(in NpcSnapshot state) => Replication?.NpcBirthRetained(in state);
    }
    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate update) { update = default; return false; }
    }
}
