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

public sealed class RuntimeTownNpcDangerDispatcher1458Tests
{
    public static IEnumerable<object[]> OriginalCases()
    {
        foreach ((string family, string name) in new[] { ("danger", "TownDangerPhase1458"),
            ("home", "TownHomePhase1458"), ("attack", "TownActiveCombat1458") })
        {
            using Stream resource = typeof(RuntimeTownNpcDangerDispatcher1458Tests).Assembly.GetManifestResourceStream(name)!;
            using var gzip = new GZipStream(resource, CompressionMode.Decompress);
            using JsonDocument json = JsonDocument.Parse(gzip);
            foreach (JsonElement row in json.RootElement.EnumerateArray()) yield return [family, row.Clone()];
        }
    }

    // Original executable SHA4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034.
    // Raw AI and actual NPC.UpdateNPC were captured independently; expected RNG is the next original sample.
    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void Source_common_phase_pins_danger_body_combat_home_physics_and_next_random(string family, JsonElement row)
    {
        var f = new Fixture(family, row);
        bool outer = row.GetProperty("outer").GetBoolean();
        string scenario = row.GetProperty("scenario").GetString()!;
        NpcSnapshot[] peers = new NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
        int count = f.Npcs.CopyActive(peers);
        if (outer && family == "danger" && scenario == "equalCenter")
        {
            // The source outer pass strikes the resident here. Until that separate damage transaction is
            // owned, admit no outer NPC mutation; retain all32 independent original contact rows as fence evidence.
            Assert.False(RuntimeTownNpcSchedule1458.AdmitsOuterContact(in f.Before, peers.AsSpan(0, count)));
            var summary = f.Schedule.Tick(in f.Conditions, f.Bounds, f.Status, f.Combat,
                f.Conversations, default, f.PlayerDanger);
            Assert.Equal(1, summary.RejectedCommits); Assert.Equal(f.Before, f.Current);
            Assert.Empty(f.Sink.Commits);
            Assert.Equal(new VanillaUnifiedRandom1458(f.Seed).Next(), f.Random.Stream.Next());
            return;
        }

        NpcStateUpdate actual;
        NpcAiProjectileIntent? intent = null;
        if (outer)
        {
            RuntimeTownNpcCombatTickSummary1458 summary = f.Schedule.Tick(in f.Conditions, f.Bounds, f.Status,
                f.Combat, f.Conversations, default, f.PlayerDanger);
            Assert.Equal(0, summary.RejectedCommits);
            NpcSnapshot committed = f.Current;
            Assert.Single(f.Sink.Commits);
            Assert.Equal(f.Before.Revision.Value + 1, committed.Revision.Value);
            actual = Update(in committed);
        }
        else
        {
            Span<RuntimeTownNpcHomeCommit> homes = stackalloc RuntimeTownNpcHomeCommit[RuntimeTownNpcStateStore.MaximumTownNpcs];
            Assert.Equal(1, f.Town.CopyHomeBaselines(homes));
            Span<RuntimeTownNpcMeleeIntent1458> melee = stackalloc RuntimeTownNpcMeleeIntent1458[RuntimeNpcStore.MaximumAddressableCapacity];
            Assert.True(f.Schedule.TryPlanUnifiedResident(in f.Before, in homes[0], in f.Conditions,
                f.Bounds, f.Conversations, default, f.PlayerDanger, peers.AsSpan(0, count), f.Status, f.Combat,
                melee, out actual, out _, out intent, out _));
            Assert.Equal(f.Before, f.Current); Assert.Empty(f.Sink.Commits);
        }
        Assert.Equal(Ai(row, "ai"), actual.Ai);
        Assert.Equal(Ai(row, "local"), actual.Simulation.LocalAi);
        Assert.Equal(row.GetProperty("x").GetSingle(), actual.PositionX);
        Assert.Equal(row.GetProperty("y").GetSingle(), actual.PositionY);
        Assert.Equal(row.GetProperty("vx").GetSingle(), actual.VelocityX);
        Assert.Equal(row.GetProperty("vy").GetSingle(), actual.VelocityY);
        Assert.Equal(row.GetProperty("direction").GetInt32(), actual.Simulation.DirectionX);
        Assert.Equal(row.GetProperty("sprite").GetInt32(), actual.Simulation.SpriteDirection);
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Stream.Next());
        if (family == "attack")
        {
            JsonElement[] shots = row.GetProperty("shots").EnumerateArray().ToArray();
            if (outer)
            {
                Span<ProjectileSnapshot> spawned = stackalloc ProjectileSnapshot[4];
                Assert.Equal(shots.Length, f.Projectiles.CopyActive(spawned));
                for (int i = 0; i < shots.Length; i++)
                    AssertShot(shots[i], spawned[i].Type.Value, spawned[i].PositionX, spawned[i].PositionY,
                        spawned[i].VelocityX, spawned[i].VelocityY, spawned[i].Damage);
            }
            else
            {
                Assert.Equal(shots.Length == 1, intent.HasValue);
                if (intent is NpcAiProjectileIntent shot)
                {
                    Assert.True(VanillaDefinitionCatalog.TryGet(shot.Type, out VanillaProjectileDefinition definition));
                    AssertShot(shots[0], shot.Type.Value, shot.PositionX - definition.Width / 2,
                        shot.PositionY - definition.Height / 2, shot.VelocityX, shot.VelocityY, shot.Damage);
                }
            }
        }
    }

    private static void AssertShot(JsonElement row, int type, float x, float y, float vx, float vy, int damage)
    {
        Assert.Equal(row.GetProperty("type").GetInt32(), type);
        Assert.Equal(row.GetProperty("x").GetSingle(), x); Assert.Equal(row.GetProperty("y").GetSingle(), y);
        Assert.Equal(row.GetProperty("vx").GetSingle(), vx); Assert.Equal(row.GetProperty("vy").GetSingle(), vy);
        Assert.Equal(row.GetProperty("damage").GetInt32(), damage);
    }
    private static NpcAiState Ai(JsonElement row, string name)
    {
        float[] values = row.GetProperty(name).EnumerateArray().Select(x => x.GetSingle()).ToArray();
        return new(values[0], values[1], values[2], values[3]);
    }
    private static NpcStateUpdate Update(in NpcSnapshot npc) => new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY,
        npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);

    private sealed class Fixture
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeTownNpcStateStore Town;
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(100, 80));
        internal readonly RuntimeProjectileStore Projectiles = new(32);
        internal readonly Sink Sink = new();
        internal readonly OwnedRandom Random;
        internal readonly RuntimeNpcStinkyStatus1458 Status;
        internal readonly RuntimeTownNpcSchedule1458 Schedule;
        internal readonly RuntimeTownNpcCombat1458 Combat;
        internal readonly RuntimeTownNpcScheduleConditions1458 Conditions;
        internal readonly NpcSnapshot Before;
        internal readonly int Seed;
        internal readonly RuntimeTownPlayerBounds1458[] Bounds;
        internal readonly RuntimeTownPlayerConversation1458[] Conversations;
        internal readonly RuntimeTownPlayerDanger1458[] PlayerDanger;
        internal NpcSnapshot Current { get { Assert.True(Npcs.TryGet(Before.Handle, out var npc)); return npc; } }
        internal Fixture(string family, JsonElement row)
        {
            int type = row.GetProperty("type").GetInt32(); string scenario = row.GetProperty("scenario").GetString()!;
            bool homeCase = family == "home", activeAttack = family == "attack";
            Seed = row.GetProperty("seed").GetInt32(); Random = new(Seed);
            float x = homeCase && (scenario.Contains("Away") || scenario == "away") ? 200f : 639f;
            for (int column = 0; column < 100; column++) Tiles.Set(column, 30, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Town = new(new WorldNpcPersistence([], [new WorldTownNpc(type, "Resident", x, 440f, false, 40, 30, null, false)], []),
                [new WorldTownRoom(type, 40, 30)], Tiles.Dimensions);
            Npcs = new(commitSink: Sink); Assert.True(Town.TryReserveRuntimeSlots(Npcs));
            Assert.True(Npcs.TryGetActive(0, out var initial));
            float state = activeAttack ? type is 17 or 18 ? 10f : type is 19 or 22 ? 12f : 15f : row.GetProperty("state").GetSingle();
            int attackTime = type switch { 17 or 18 => 34, 19 => 40, 22 => 30, 353 => 12, _ => 15 }, fireTick = type == 17 ? 10 : 1;
            string clock = activeAttack ? row.GetProperty("clock").GetString()! : "";
            float timer = !activeAttack ? 300f : clock is "expire" or "blockedRepeat" ? 1f : clock == "fire" ? attackTime - fireTick + 1 : attackTime;
            var input = new NpcStateUpdate(type, (short)type, x, 440f, homeCase || activeAttack ? .5f : 0f, 0f, 255,
                new(state, timer, 17f, 23f), initial.Simulation with { DirectionX = 1, SpriteDirection = activeAttack ? 1 : -1,
                    Life = 250, LifeMax = 250, HitboxOverride = new(18, 40),
                    LocalAi = new(0f, 0f, clock == "blockedRepeat" ? 8f : 0f, activeAttack ? clock == "fire" ? fireTick - 1 : 0 : 9) });
            Assert.True(Npcs.TryUpdate(initial.Handle, in input, out Before));
            Status = new(Npcs);
            var marked = new List<NpcHandle>();
            void Enemy(byte slot, float dx, int enemyType = 3, int damage = 10, bool friendly = false,
                bool invulnerable = false, bool ghosting = false, bool chaseable = true, bool stinky = false)
            {
                var enemy = new NpcStateUpdate(enemyType, (short)enemyType, 639f + dx, 440f, 0f, 0f, 255, default,
                    NpcSimulationState.Initial with { Life = 100, LifeMax = 100, Friendly = friendly, DamageOverride = damage,
                        DontTakeDamage = invulnerable, Immortal = false, NoTileCollide = ghosting, Chaseable = chaseable, HitboxOverride = new(18, 40) });
                Assert.True(Npcs.TrySpawn(slot, in enemy, out var npc)); if (stinky) marked.Add(npc.Handle);
            }
            if (activeAttack)
            { if (scenario != "missing") Enemy(1, scenario == "leftMismatch" ? -50f : 50f, ghosting: scenario == "ghostLos"); }
            else if (!homeCase)
                switch (scenario)
                {
                    case "damage0": Enemy(1, 50, damage: 0); break;
                    case "liveDamage1": Enemy(1, 50, damage: 1); break;
                    case "liveDamage100": Enemy(1, 50, damage: 100); break;
                    case "friendly": Enemy(1, 50, friendly: true); break;
                    case "friendlyStinky": Enemy(1, 50, friendly: true, stinky: true); break;
                    case "damage0Stinky": Enemy(1, 50, damage: 0, stinky: true); break;
                    case "invulnerable": Enemy(1, 50, invulnerable: true); break;
                    case "notChaseable": Enemy(1, 50, chaseable: false); break;
                    case "excluded690": Enemy(1, 50, enemyType: 690); break;
                    case "excluded645": Enemy(1, 50, enemyType: 645); break;
                    case "solidLos": Enemy(1, 80); break;
                    case "noTileCollideLos": Enemy(1, 80, ghosting: true); break;
                    case "equalCenter": Enemy(1, 0); break;
                    case "left": Enemy(1, -50); break;
                    case "tie": Enemy(1, -50); Enemy(2, 50); break;
                    case "farAttackThenNearInvulnerable": Enemy(1, -80); Enemy(2, -40, invulnerable: true); break;
                    case "nearInvulnerableThenFarAttack": Enemy(1, -40, invulnerable: true); Enemy(2, -80); break;
                    case "edgeRange": Enemy(1, 320); break;
                    case "justInsideRange": Enemy(1, 319.99f); break;
                    case "pretty201": Enemy(1, 201); break;
                    case "pretty200": Enemy(1, 200); break;
                }
            if (scenario.Contains("Los")) for (int y = 25; y < 30; y++) Tiles.Set(42, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            if (homeCase && scenario is "chair" or "talkChair")
            { Tiles.Set(40, 28, new WorldTile { Type = 15, Flags = WorldTileFlags.Active }); Tiles.Set(40, 29, new WorldTile { Type = 15, FrameY = 18, Flags = WorldTileFlags.Active }); }
            Bounds = homeCase && (scenario.Contains("talk") || scenario == "seenAway") ? [new(x - 50f, 439f, 20f, 42f)] : [];
            Conversations = Bounds.Length == 1 ? [new(0, scenario.Contains("talk") ? (short)0 : (short)-1, Bounds[0], true)] : [];
            PlayerDanger = scenario.StartsWith("stinky") ? [new(scenario == "stinky255" ? (byte)255 : (byte)0,
                new(688f, 439f, 20f, 42f), scenario == "stinkyDeadPlayer", true)] : Bounds.Length == 1 ? [new(0, Bounds[0], false, false)] : [];
            Status.BeginWorldTick(); if (homeCase && scenario == "selfStinky") marked.Add(Before.Handle);
            foreach (NpcHandle handle in marked) Assert.True(Status.TryApply(handle, 180));
            Status.BeginWorldTick(); Status.BeginWorldTick();
            Schedule = new(Town, Npcs, Tiles, new NpcRuntimeTownScheduleRandom1458(Random));
            Combat = new(Town, Npcs, Projectiles, Tiles, default, new(), false, false, new NpcRuntimeTownCombatRandom1458(Random));
            Conditions = new(!homeCase, false, false, false, false); Sink.Commits.Clear();
        }
    }
    private sealed class OwnedRandom(int seed) : IVanillaNpcRandom
    {
        internal VanillaUnifiedRandom1458 Stream { get; } = new(seed);
        public int NextInt32(int min, int max) => Stream.Next(min, max);
        public double NextDouble() => Stream.NextDouble();
    }
    private sealed class Sink : INpcStateCommitSink
    {
        internal List<NpcStateCommitKind> Commits { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc) => Commits.Add(kind);
    }
}
