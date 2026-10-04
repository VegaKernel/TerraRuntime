using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcSocialNurse1458Tests
{
    public static IEnumerable<object[]> SocialCases() => Rows("TownSocial1458");
    public static IEnumerable<object[]> NurseCases() => Rows("TownNurse1458");
    private static IEnumerable<object[]> Rows(string name)
    {
        using var stream = typeof(RuntimeTownNpcSocialNurse1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray()) yield return [row.Clone()];
    }

    [Theory, MemberData(nameof(SocialCases))]
    public void Source_two_resident_AI_and_outer_frames_keep_peer_clock_and_next_rng(JsonElement row)
    {
        var f = new Fixture(row, nurse: false);
        string family = row.GetProperty("family").GetString()!;
        bool outer = family == "frame" || row.GetProperty("outer").GetBoolean();
        if (outer)
        {
            var result = f.Schedule.Tick(in f.Conditions, [], f.Status, f.Combat);
            Assert.Equal(0, result.RejectedCommits);
            AssertNpc(row.GetProperty(family == "frame" ? "first" : "finalFirst"), f.Current(0), frames: true);
            if (f.Npcs.TryGetActive(1, out var peer)) AssertNpc(row.GetProperty("finalPartner"), peer, frames: true);
        }
        else
        {
            f.RawPhase(0);
            AssertNpc(row.GetProperty("first"), f.Current(0), frames: false);
            if (f.Npcs.TryGetActive(1, out var firstPeer)) AssertNpc(row.GetProperty("partner"), firstPeer, frames: false);
            if (f.Npcs.TryGetActive(1, out _)) f.RawPhase(1);
            AssertNpc(row.GetProperty("finalFirst"), f.Current(0), frames: false);
            if (f.Npcs.TryGetActive(1, out var peer)) AssertNpc(row.GetProperty("finalPartner"), peer, frames: false);
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
    }

    [Theory, MemberData(nameof(NurseCases))]
    public void Source_Nurse_admission_active_13_shot_and_outer_frame_match_original(JsonElement row)
    {
        if (row.TryGetProperty("scenario", out var scenario) && scenario.GetString() == "overfull")
        {
            // The original scan includes overfull actors. Such an imported state has no owner under
            // the existing runtime life invariant; retain an explicit admission boundary.
            Assert.Equal(13f, row.GetProperty("nurse").GetProperty("ai")[0].GetSingle());
            Assert.Equal(300, row.GetProperty("target").GetProperty("life").GetInt32());
            var table = new RuntimeNpcStore();
            var invalid = new NpcStateUpdate(17, 17, 679, 440, 0, 0, 255, default,
                NpcSimulationState.Initial with { Life = 300, LifeMax = 250 });
            Assert.False(table.TrySpawn(0, in invalid, out _));
            return;
        }
        var f = new Fixture(row, nurse: true);
        bool outer = row.GetProperty("outer").GetBoolean();
        NpcAiProjectileIntent? planned = null;
        if (outer)
        {
            var result = f.Schedule.Tick(in f.Conditions, [], f.Status, f.Combat);
            Assert.Equal(0, result.RejectedCommits);
        }
        else planned = f.RawPhase(0);
        AssertNpc(row.GetProperty("nurse"), f.Current(0), outer);
        JsonElement[] expected = row.GetProperty("shots").EnumerateArray().ToArray();
        if (outer)
        {
            Span<ProjectileSnapshot> shots = stackalloc ProjectileSnapshot[8];
            Assert.Equal(expected.Length, f.Projectiles.CopyActive(shots));
            for (int i = 0; i < expected.Length; i++)
                AssertShot(expected[i], shots[i].Type, shots[i].PositionX, shots[i].PositionY,
                    shots[i].VelocityX, shots[i].VelocityY, shots[i].Damage, shots[i].Ai);
        }
        else
        {
            Assert.Equal(expected.Length > 0, planned.HasValue);
            if (planned is { } shot)
            {
                Assert.True(VanillaDefinitionCatalog.TryGet(shot.Type, out var definition));
                AssertShot(expected[0], shot.Type, shot.PositionX - definition.Width / 2,
                    shot.PositionY - definition.Height / 2, shot.VelocityX, shot.VelocityY, shot.Damage, shot.InitialAi);
            }
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
    }

    private static void AssertShot(JsonElement e, ProjectileTypeId type, float x, float y, float vx, float vy, int damage, ProjectileAiState ai)
    {
        Assert.Equal(e.GetProperty("type").GetInt32(), type.Value);
        Assert.Equal(e.GetProperty("x").GetSingle(), x); Assert.Equal(e.GetProperty("y").GetSingle(), y);
        Assert.Equal(e.GetProperty("vx").GetSingle(), vx); Assert.Equal(e.GetProperty("vy").GetSingle(), vy);
        Assert.Equal(e.GetProperty("damage").GetInt32(), damage);
        Assert.Equal(e.GetProperty("ai")[0].GetSingle(), ai.Ai0);
    }

    private static void AssertNpc(JsonElement e, NpcSnapshot n, bool frames)
    {
        Assert.Equal(Ai(e, "ai"), n.Ai); Assert.Equal(Ai(e, "local"), n.Simulation.LocalAi);
        Assert.Equal(e.GetProperty("x").GetSingle(), n.PositionX); Assert.Equal(e.GetProperty("y").GetSingle(), n.PositionY);
        Assert.Equal(e.GetProperty("vx").GetSingle(), n.VelocityX); Assert.Equal(e.GetProperty("vy").GetSingle(), n.VelocityY);
        Assert.Equal(e.GetProperty("direction").GetInt32(), n.Simulation.DirectionX);
        Assert.Equal(e.GetProperty("sprite").GetInt32(), n.Simulation.SpriteDirection);
        Assert.Equal(e.GetProperty("life").GetInt32(), n.Simulation.Life);
        if (e.TryGetProperty("breath", out var breath)) Assert.Equal(breath.GetInt32(), n.Simulation.Breath);
        if (frames)
        {
            Assert.Equal(e.GetProperty("frameCounter").GetDouble(), n.Simulation.FrameCounter);
            Assert.Equal(e.GetProperty("frameIndex").GetInt32(), n.Simulation.FrameIndex);
        }
    }
    private static NpcAiState Ai(JsonElement e, string name) =>
        new(e.GetProperty(name)[0].GetSingle(), e.GetProperty(name)[1].GetSingle(),
            e.GetProperty(name)[2].GetSingle(), e.GetProperty(name)[3].GetSingle());

    internal sealed class Fixture
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeProjectileStore Projectiles = new(32);
        internal readonly RuntimeTownNpcStateStore Town;
        internal readonly WorldTileStore Tiles = new(new(100, 80));
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeNpcStinkyStatus1458 Status;
        internal readonly RuntimeTownNpcSchedule1458 Schedule;
        internal readonly RuntimeTownNpcCombat1458 Combat;
        internal readonly RuntimeTownNpcScheduleConditions1458 Conditions = new(true, false, false, false, false);
        internal Fixture(JsonElement row, bool nurse, RuntimeNpcReplicationRegistry? replication = null)
        {
            Npcs = new(commitSink: replication);
            Random = new(row.GetProperty("seed").GetInt32());
            var adapter = new SystemVanillaNpcRandom(Random);
            for (int x = 0; x < 100; x++) Tiles.Set(x, 30, new() { Type = 1, Flags = WorldTileFlags.Active });
            int type = nurse ? 18 : 17;
            string family = row.GetProperty("family").GetString()!;
            bool activeNurse = nurse && family == "active";
            string scenario = nurse && !activeNurse ? row.GetProperty("scenario").GetString()! : "";
            float state = nurse ? activeNurse ? 13f : 0f : family == "initiation" ? 0f : family == "frame" ? 16f : row.GetProperty("state").GetSingle();
            float timer = activeNurse || family == "maintenance" ? row.GetProperty("timer").GetSingle() : 300f;
            float peerX = activeNurse ? 639f + row.GetProperty("dx").GetSingle() : scenario is "range499" ? 1138f : scenario == "range500" ? 1139f : 679f;
            WorldTownNpc[] towns = nurse ? [new(type, "Nurse", 639, 440, false, 40, 30, null, false)] :
                [new(type, "A", 639, 440, false, 40, 30, null, false), new(type, "B", peerX, 440, false, 40, 30, null, false)];
            Town = new(new([], towns, []), [new(type, 40, 30)], Tiles.Dimensions);
            Assert.True(Town.TryReserveRuntimeSlots(Npcs));
            NpcStateUpdate State(int npcType, float x, float s, float t, float targetSlot) =>
                new(npcType, (short)npcType, x, 440, .5f, 0, 255, new(s, t, targetSlot, 0),
                    NpcSimulationState.Initial with { DirectionX = 1, SpriteDirection = -1,
                        Life = 250, LifeMax = 250, BaseLifeMax = 250, BaseDefense = 0,
                        KnockBackResist = 1f, HitboxOverride = new(18, 40), FrameIndex = 0,
                        LocalAi = new(0, 0, 0, 9), Breath = 200 });
            Assert.True(Npcs.TryGetActive(0, out var initial));
            NpcStateUpdate own = State(type, 639, state, timer, nurse ? activeNurse ? 1f : 17f : state == 0f ? 0f : 1f);
            if (nurse) own = own with { Ai = own.Ai with { Ai3 = activeNurse ? 0f : 23f },
                Simulation = own.Simulation with { SpriteDirection = activeNurse ? row.GetProperty("sprite").GetInt32() : -1,
                    Breath = activeNurse || scenario == "breathZero" ? 0 : 200,
                    Life = scenario == "self" ? 100 : 250,
                    LocalAi = own.Simulation.LocalAi with { Ai3 = activeNurse ? row.GetProperty("clock").GetSingle() : 9f } },
                VelocityX = scenario == "airborne" ? 0f : .5f, VelocityY = scenario == "airborne" ? 1f : 0f };
            if (family == "frame") own = own with { Simulation = own.Simulation with {
                FrameCounter = row.GetProperty("frame").GetDouble(),
                LocalAi = new(0, 0, row.GetProperty("ownTwo").GetSingle(), row.GetProperty("ownThree").GetSingle()) } };
            Assert.True(Npcs.TryUpdate(initial.Handle, in own, out _));
            float peerState = state switch { 3 => 4, 4 => 3, 16 => 17, 17 => 16, _ => 0 };
            NpcStateUpdate partner = State(17, peerX, peerState, timer, nurse ? 17f : 0f);
            if (nurse) partner = partner with { Ai = partner.Ai with { Ai3 = 23f },
                Simulation = partner.Simulation with { Life = scenario == "healthy" ? 250 : scenario == "overfull" ? 300 : 200 } };
            if (family == "frame") partner = partner with { Simulation = partner.Simulation with { LocalAi = default } };
            if (Npcs.TryGetActive(1, out var currentPeer)) Assert.True(Npcs.TryUpdate(currentPeer.Handle, in partner, out _));
            else Assert.True(Npcs.TrySpawn(1, in partner, out currentPeer));
            if (family == "maintenance" && !row.GetProperty("peerActive").GetBoolean()) Assert.True(Npcs.TryDespawn(currentPeer.Handle));
            if (scenario is "mostMissing" or "tie")
            { var second = State(17, 719, 0, 300, 17) with { Simulation = partner.Simulation with { Life = scenario == "tie" ? 200 : 100 } }; Assert.True(Npcs.TrySpawn(2, in second, out _)); }
            if (scenario == "blocked") for (int y = 0; y < 30; y++) Tiles.Set(41, y, new() { Type = 1, Flags = WorldTileFlags.Active });
            bool danger = scenario == "hostile" || family == "maintenance" && row.GetProperty("danger").GetBoolean();
            if (danger)
            { var enemy = new NpcStateUpdate(3, 3, nurse ? 739 : 647, 440, 0, 0, 255, default, NpcSimulationState.Initial with {
                Life = 100, LifeMax = 100, Friendly = false, DamageOverride = 10, Immortal = false,
                Chaseable = true, NoTileCollide = true, HitboxOverride = new(18, 40) });
                Assert.True(Npcs.TrySpawn(2, in enemy, out _)); }
            Status = new(Npcs); Status.BeginWorldTick();
            Schedule = new(Town, Npcs, Tiles, new NpcRuntimeTownScheduleRandom1458(adapter));
            Combat = new(Town, Npcs, Projectiles, Tiles, default, new(), false, false,
                new NpcRuntimeTownCombatRandom1458(adapter), replication);
        }
        internal NpcSnapshot Current(byte slot) { Assert.True(Npcs.TryGetActive(slot, out var n)); return n; }
        internal NpcAiProjectileIntent? RawPhase(byte slot)
        {
            var before = Current(slot);
            Span<NpcSnapshot> peers = stackalloc NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
            int count = Npcs.CopyActive(peers);
            Span<RuntimeTownNpcHomeCommit> homes = stackalloc RuntimeTownNpcHomeCommit[RuntimeTownNpcStateStore.MaximumTownNpcs];
            int homeCount = Town.CopyHomeBaselines(homes);
            RuntimeTownNpcHomeCommit home = default;
            for (int i = 0; i < homeCount; i++) if (homes[i].NpcSlot == slot) home = homes[i];
            Span<RuntimeTownNpcMeleeIntent1458> melee = stackalloc RuntimeTownNpcMeleeIntent1458[RuntimeNpcStore.MaximumAddressableCapacity];
            Assert.True(Schedule.TryPlanUnifiedResident(in before, in home, in Conditions, [], [], [], [],
                peers[..count], Status, Combat, melee, out var next, out _, out var shot, out _, out var pair));
            if (pair is { } peer) Assert.True(Npcs.TryUpdatePairUnpublished(in before, in next, peer.Expected, peer.Update, out _, out _));
            else Assert.True(Npcs.TryUpdateUnpublished(before.Handle, in next, out _));
            return shot;
        }
    }
}
