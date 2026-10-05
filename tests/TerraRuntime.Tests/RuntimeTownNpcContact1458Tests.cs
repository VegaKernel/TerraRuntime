using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcContact1458Tests
{
    public static IEnumerable<object[]> OriginalCases()
    {
        using Stream stream = typeof(RuntimeTownNpcContact1458Tests).Assembly.GetManifestResourceStream("TownContact1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using JsonDocument json = JsonDocument.Parse(gzip);
        foreach (JsonElement row in json.RootElement.EnumerateArray()) yield return [row.Clone()];
    }

    // Independently captured original GetHurtByOtherNPCs / full UpdateNPC, not values derived from our resolver.
    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void Original_contact_pins_damage_ai_reset_immunity_same_tick_physics_and_random(JsonElement row)
    {
        var f = new Fixture(row.GetProperty("scenario").GetString()!, row.GetProperty("state").GetSingle(),
            row.GetProperty("seed").GetInt32());
        bool outer = row.GetProperty("outer").GetBoolean();
        NpcStateUpdate actual;
        if (outer)
        {
            var summary = f.Tick();
            Assert.Equal(0, summary.RejectedCommits);
            Assert.True(f.Npcs.TryGet(f.Before.Handle, out var current));
            Assert.Equal(f.Before.Revision.Value + 1, current.Revision.Value);
            Assert.Single(f.Sink.Commits);
            actual = Update(in current);
        }
        else
        {
            Span<NpcSnapshot> peers = stackalloc NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
            int count = f.Npcs.CopyActive(peers);
            var input = Update(in f.Before);
            Assert.True(f.Combat.TryPlanContact(in f.Before, in input, peers[..count], true, out actual, out _));
            Assert.Empty(f.Sink.Commits);
            Assert.True(f.Npcs.TryGet(f.Before.Handle, out var unchanged)); Assert.Equal(f.Before, unchanged);
        }
        Assert.Equal(Ai(row, "ai"), actual.Ai);
        Assert.Equal(Ai(row, "local"), actual.Simulation.LocalAi);
        Assert.Equal(row.GetProperty("life").GetInt32(), actual.Simulation.Life);
        Assert.Equal(row.GetProperty("immune").GetInt32(), actual.Simulation.HostileContactImmunity);
        Assert.Equal(row.GetProperty("justHit").GetBoolean(), actual.Simulation.JustHit);
        Assert.Equal(row.GetProperty("x").GetSingle(), actual.PositionX);
        Assert.Equal(row.GetProperty("y").GetSingle(), actual.PositionY);
        Assert.Equal(row.GetProperty("vx").GetSingle(), actual.VelocityX);
        Assert.Equal(row.GetProperty("vy").GetSingle(), actual.VelocityY);
        Assert.Equal(row.GetProperty("direction").GetInt32(), actual.Simulation.DirectionX);
        Assert.Equal(row.GetProperty("sprite").GetInt32(), actual.Simulation.SpriteDirection);
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
    }

    [Fact]
    public void Potentially_lethal_town_contact_rejects_whole_ai_phase_and_retains_live_random()
    {
        var f = new Fixture("overlap", 0f, 1458);
        var lowLife = Update(in f.Before) with { Simulation = f.Before.Simulation with { Life = 1 } };
        Assert.True(f.Npcs.TryUpdate(f.Before.Handle, in lowLife, out var before));
        f.Sink.Commits.Clear();
        var expected = f.Random.Clone();
        Assert.Equal(1, f.Tick().RejectedCommits);
        Assert.True(f.Npcs.TryGet(before.Handle, out var retained)); Assert.Equal(before, retained);
        Assert.Empty(f.Sink.Commits); Assert.True(f.Random.HasSameState(expected));
    }

    [Fact]
    public void Generation_reuse_clears_retained_contact_immunity()
    {
        var f = new Fixture("overlap", 0f, 1458);
        Assert.Equal(0, f.Tick().RejectedCommits);
        Assert.True(f.Npcs.TryGet(f.Before.Handle, out var hit)); Assert.Equal(30, hit.Simulation.HostileContactImmunity);
        Assert.True(f.Npcs.TryDespawn(hit.Handle));
        var input = Update(in f.Before) with { Simulation = f.Before.Simulation with { HostileContactImmunity = 0 } };
        Assert.True(f.Npcs.TrySpawn(0, in input, out var replaced));
        Assert.NotEqual(hit.Handle, replaced.Handle); Assert.Equal(0, replaced.Simulation.HostileContactImmunity);
        Assert.Equal(0, f.Tick().RejectedCommits);
        Assert.True(f.Npcs.TryGet(replaced.Handle, out var current)); Assert.Equal(30, current.Simulation.HostileContactImmunity);
    }

    [Fact]
    public void Publication_observes_accepted_random_and_callback_draw_is_retained()
    {
        var baseline = new Fixture("overlap", 0f, 1458);
        Assert.Equal(0, baseline.Tick().RejectedCommits);
        int expectedCallback = baseline.Random.Next(), expectedAfterCallback = baseline.Random.Next();
        var f = new Fixture("overlap", 0f, 1458);
        int observed = -1;
        f.Sink.OnCommit = _ => observed = f.Random.Next();
        Assert.Equal(0, f.Tick().RejectedCommits);
        Assert.Equal(expectedCallback, observed);
        Assert.Equal(expectedAfterCallback, f.Random.Next());
        Assert.Single(f.Sink.Commits);
    }

    [Fact]
    public void Contact_immunity_ages_once_and_new_strike_is_admitted_on_thirtieth_tick()
    {
        var f = new Fixture("overlap", 0f, 1458);
        Assert.Equal(0, f.Tick().RejectedCommits);
        Assert.True(f.Npcs.TryGet(f.Before.Handle, out var hit));
        int life = hit.Simulation.Life;
        for (int tick = 1; tick <= 30; tick++)
        {
            Assert.True(f.Npcs.TryGet(f.Before.Handle, out var resident));
            Assert.True(f.Npcs.TryGetActive(1, out var attacker));
            var contact = Update(in attacker) with { PositionX = resident.PositionX, PositionY = resident.PositionY };
            Assert.True(f.Npcs.TryUpdate(attacker.Handle, in contact, out _));
            f.Sink.Commits.Clear();
            Assert.Equal(0, f.Tick().RejectedCommits);
            Assert.True(f.Npcs.TryGet(f.Before.Handle, out var current));
            Assert.Equal(tick == 30 ? 30 : 30 - tick, current.Simulation.HostileContactImmunity);
            if (tick < 30) Assert.Equal(life, current.Simulation.Life);
            else Assert.True(current.Simulation.Life < life);
            Assert.Single(f.Sink.Commits);
        }
    }

    [Fact]
    public void Regeneration_preflight_uses_the_healed_life_before_contact_admission()
    {
        var f = new Fixture("overlap", 0f, 1458);
        var input = Update(in f.Before) with { Simulation = f.Before.Simulation with {
            Life = 12, FriendlyRegenerationCounter = 180 } };
        Assert.True(f.Npcs.TryUpdate(f.Before.Handle, in input, out _));
        Assert.Equal(0, f.Tick().RejectedCommits);
        Assert.True(f.Npcs.TryGet(f.Before.Handle, out var current));
        Assert.Equal(0, current.Simulation.FriendlyRegenerationCounter);
        Assert.InRange(current.Simulation.Life, 1, 5);
    }

    [Fact]
    public void Actual_world_tick_applies_contact_once_and_retains_owned_immunity()
    {
        var f = new Fixture("overlap", 0f, 1458);
        var world = new ServerRuntimeState(npcs: f.Npcs, worldTiles: f.Tiles, townNpcs: f.Town,
            naturalSpawnRandom: f.Adapter, npcAiStepper: new RejectedForeignStep());
        f.Sink.Commits.Clear();
        world.Tick();
        Assert.True(f.Npcs.TryGet(f.Before.Handle, out var hit));
        Assert.Equal(30, hit.Simulation.HostileContactImmunity);
        Assert.InRange(hit.Simulation.Life, 238, 242);
        Assert.Single(f.Sink.Commits);
        world.Tick();
        Assert.True(f.Npcs.TryGet(f.Before.Handle, out var next));
        Assert.Equal(29, next.Simulation.HostileContactImmunity);
        Assert.Equal(hit.Simulation.Life, next.Simulation.Life);
    }

    [Fact]
    public void Potentially_lethal_phase_cannot_close_the_previously_remembered_door()
    {
        var f = new Fixture("friendly", 1f, 14);
        f.Tiles.Set(41, 26, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        for (int row = 0; row < 3; row++)
            f.Tiles.Set(41, 27 + row, new WorldTile { Type = 10, Flags = WorldTileFlags.Active, FrameY = (short)(row * 18) });
        Assert.Equal(0, f.Tick().RejectedCommits);
        Assert.True(f.Schedule.HasRememberedDoor(f.Before.Handle));
        Assert.Equal((ushort)11, f.Tiles.Get(41, 27).Type);
        Assert.True(f.Npcs.TryGet(f.Before.Handle, out var current));
        var moved = Update(in current) with { PositionX = 703f, VelocityX = .5f,
            Ai = new(1, 300, 0, 0), Simulation = current.Simulation with { Life = 1 } };
        Assert.True(f.Npcs.TryUpdate(current.Handle, in moved, out var before));
        Assert.True(f.Npcs.TryGetActive(1, out var enemy));
        var lethal = Update(in enemy) with { PositionX = 703f,
            Simulation = enemy.Simulation with { Friendly = false, DamageOverride = 10 } };
        Assert.True(f.Npcs.TryUpdate(enemy.Handle, in lethal, out _));
        var random = f.Random.Clone(); f.Sink.Commits.Clear();
        Assert.Equal(1, f.Tick().RejectedCommits);
        Assert.True(f.Npcs.TryGet(before.Handle, out var retained)); Assert.Equal(before, retained);
        Assert.Equal((ushort)11, f.Tiles.Get(41, 27).Type);
        Assert.True(f.Schedule.HasRememberedDoor(before.Handle));
        Assert.True(f.Random.HasSameState(random)); Assert.Empty(f.Sink.Commits);
    }

    private sealed class RejectedForeignStep : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }

    private static NpcAiState Ai(JsonElement row, string key)
    {
        float[] values = row.GetProperty(key).EnumerateArray().Select(x => x.GetSingle()).ToArray();
        return new(values[0], values[1], values[2], values[3]);
    }
    private static NpcStateUpdate Update(in NpcSnapshot npc) => new(npc.Type, npc.NetId, npc.PositionX,
        npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);

    internal sealed class Fixture
    {
        internal readonly Sink Sink;
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeTownNpcStateStore Town;
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(100, 80));
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly SystemVanillaNpcRandom Adapter;
        internal readonly RuntimeNpcBuffStatus1458 Status;
        internal readonly RuntimeTownNpcSchedule1458 Schedule;
        internal readonly RuntimeTownNpcCombat1458 Combat;
        internal readonly NpcSnapshot Before;
        internal Fixture(string scenario, float state, int seed, RuntimeNpcReplicationRegistry? replication = null)
        {
            Sink = new Sink(replication);
            for (int x = 0; x < 100; x++) Tiles.Set(x, 30, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Town = new(new WorldNpcPersistence([], [new WorldTownNpc(17, "Resident", 639, 440, false, 40, 30, null, false)], []),
                [new WorldTownRoom(17, 40, 30)], Tiles.Dimensions);
            Npcs = new(commitSink: Sink); Assert.True(Town.TryReserveRuntimeSlots(Npcs));
            Assert.True(Npcs.TryGetActive(0, out var initial));
            int immune = scenario == "immuneOne" ? 1 : scenario == "immuneTwo" ? 2 : scenario == "immuneThirty" ? 30 : 0;
            var resident = new NpcStateUpdate(17, 17, 639, 440, 0, 0, 255, new(state, 300, 17, 23),
                initial.Simulation with { Life = scenario == "lowLife16" ? 16 : 250, LifeMax = 250, BaseLifeMax = 250,
                    BaseDefense = 0, DefenseOverride = 15, HostileContactImmunity = immune,
                    KnockBackResist = 1f, DirectionX = 1, SpriteDirection = -1, HitboxOverride = new(18, 40),
                    Immortal = scenario == "residentImmortal", DontTakeDamage = scenario == "residentInvulnerable",
                    LocalAi = new(0, 0, 0, 9) });
            Assert.True(Npcs.TryUpdate(initial.Handle, in resident, out Before));
            int type = scenario == "excluded690" ? 690 : scenario == "excluded624" ? 624 : scenario == "excluded645" ? 645 : 3;
            float dx = scenario == "leftOverlap" ? -5f : scenario == "edgeTouch" ? 18f : scenario == "subpixelOverlap" ? 17.99f : 0f;
            var enemy = new NpcStateUpdate(type, (short)type, 639 + dx, 440, 0, 0, 255, default,
                NpcSimulationState.Initial with { Life = scenario == "targetDead" ? 0 : 100, LifeMax = 100,
                    Friendly = scenario == "friendly", Immortal = false, Chaseable = true,
                    DamageOverride = scenario == "damageZero" ? 0 : 10, HitboxOverride = new(18, 40),
                    NoTileCollide = scenario == "noTileCollide" });
            Assert.True(Npcs.TrySpawn(1, in enemy, out _));
            if (scenario == "firstOfTwo")
            {
                var second = enemy with { Simulation = enemy.Simulation with { DamageOverride = 50 } };
                Assert.True(Npcs.TrySpawn(2, in second, out _));
            }
            Random = new(seed); var adapter = Adapter = new SystemVanillaNpcRandom(Random);
            Status = new(Npcs); Status.BeginWorldTick();
            Schedule = new(Town, Npcs, Tiles, new NpcRuntimeTownScheduleRandom1458(adapter), actorOccupancy: new FreeDoorCells());
            Combat = new(Town, Npcs, new RuntimeProjectileStore(), Tiles, default, new(), false, false,
                new NpcRuntimeTownCombatRandom1458(adapter), contactReplication: replication);
            Sink.Commits.Clear();
        }
        internal RuntimeTownNpcCombatTickSummary1458 Tick()
        {
            var conditions = new RuntimeTownNpcScheduleConditions1458(true, false, false, false, false);
            return Schedule.Tick(in conditions, [], Status, Combat);
        }
    }
    private sealed class FreeDoorCells : IVanillaTallGateOccupancyProbe
    { public bool IsActorFree(int x, int y) => true; }

    internal sealed class Sink(RuntimeNpcReplicationRegistry? replication = null) : INpcStateCommitSink
    {
        internal readonly List<NpcSnapshot> Commits = [];
        internal Action<NpcSnapshot>? OnCommit;
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc) { Commits.Add(npc); replication?.NpcStateCommitted(kind, in npc); OnCommit?.Invoke(npc); }
    }
}
