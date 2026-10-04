using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MothronWorldExecution1458Tests
{
    public static IEnumerable<object[]> OuterCases() => Read("MothronOuter1458");
    public static IEnumerable<object[]> DefaultCases() => Read("MothronDefaults1458");

    private static IEnumerable<object[]> Read(string name)
    {
        using var stream = typeof(MothronWorldExecution1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
            yield return [row.Clone()];
    }

    [Theory]
    [MemberData(nameof(DefaultCases))]
    public void Original_SetDefaults_matches_normal_expert_and_master(JsonElement row)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(row.GetProperty("type").GetInt32()), out var definition));
        var context = new VanillaNpcSpawnContext(row.GetProperty("mode").GetInt32() + 1f, 1, false);
        Assert.True(VanillaNpcSpawnDefaults.TryResolve(in definition, in context, false, out var defaults));
        Assert.Equal(row.GetProperty("width").GetInt32(), defaults.Hitbox.Width);
        Assert.Equal(row.GetProperty("height").GetInt32(), defaults.Hitbox.Height);
        Assert.Equal(row.GetProperty("damage").GetInt32(), defaults.Damage);
        Assert.Equal(row.GetProperty("defense").GetInt32(), defaults.Defense);
        Assert.Equal(row.GetProperty("lifeMax").GetInt32(), defaults.LifeMax);
        Assert.Equal(row.GetProperty("knockback").GetSingle(), defaults.KnockBackResist);
        Assert.Equal(row.GetProperty("noGravity").GetBoolean(), definition.NoGravityAtSpawn);
        Assert.Equal(row.GetProperty("style").GetInt32(), definition.AiStyle.Value);
    }

    [Theory]
    [MemberData(nameof(OuterCases))]
    public void Accepted_AI_and_collision_match_original_complete_UpdateNPC(JsonElement row)
    {
        float F(string key) => row.GetProperty(key).GetSingle();
        int I(string key) => row.GetProperty(key).GetInt32();
        bool B(string key) => row.GetProperty(key).GetBoolean();
        var tiles = Tiles(B("solid"));
        var random = new SystemVanillaNpcRandom(I("seed"));
        var stepper = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        var world = new VanillaNpcWorldMotionAiStepper(stepper, tiles, 140d);
        stepper.SetWorldConditions(true, false, expertMode: B("expert"), eclipseActive: B("eclipse"));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, F("x") + I("width") * .5f + F("dx"),
            F("y") + I("height") * .5f + F("dy"), 0, true, false, false, false)]);
        var store = new RuntimeNpcStore();
        store.SetVanillaSpawnRandomSource(random);
        store.SetVanillaSpawnContextSource(() => new(B("expert") ? 2f : 1f, 1, false));
        var initial = Initial(I("type"), F("phase"), F("clock"), B("expert")) with
        {
            PositionX = F("x"),
            PositionY = F("y"),
            VelocityX = F("vx"),
            VelocityY = F("vy")
        };
        initial = initial with
        {
            Ai = initial.Ai with { Ai1 = F("phase") == 3.2f ? F("vx") < 0f ? -1f : 1f : initial.Ai.Ai1 },
            Simulation = initial.Simulation with
            {
                CollideX = F("vx") < 0f,
                CollideY = F("vx") > 0f,
                OldVelocityX = F("vx"),
                OldVelocityY = F("vy"),
                JustHit = B("hit")
            }
        };
        Assert.True(store.TrySpawn((byte)I("actorSlot"), in initial, out var before));
        if (B("nearPeer"))
        {
            var peer = Initial(479, 0f, 0f, false) with
            { PositionX = F("x") + 10f, PositionY = F("y") + 10f, VelocityX = 1f, VelocityY = 2f };
            Assert.True(store.TrySpawn(50, in peer, out _));
        }
        Span<NpcSnapshot> peers = stackalloc NpcSnapshot[200];
        stepper.SetNpcPeers(peers[..store.CopyActive(peers)]);
        Assert.True(world.TryStepState(in before, out var proposed));
        Assert.True(store.TryUpdateUnpublished(before.Handle, in proposed, out var accepted));
        var sink = new Mutations(store);
        var completed = world.CompleteCommittedState(in before, in accepted, sink);
        Assert.True(completed.IsActive);
        Assert.Equal(I("outType"), completed.Type);
        Assert.Equal(F("outX"), completed.PositionX);
        Assert.Equal(F("outY"), completed.PositionY);
        Assert.Equal(F("outVx"), completed.VelocityX);
        Assert.Equal(F("outVy"), completed.VelocityY);
        var ai = row.GetProperty("outAi");
        Assert.Equal(new NpcAiState(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle()), completed.Ai);
        Assert.Equal(I("life"), completed.Simulation.Life);
        Assert.Equal(I("lifeMax"), completed.Simulation.LifeMax);
        Assert.Equal(I("damage"), completed.Simulation.DamageOverride);
        Assert.Equal(I("defense"), completed.Simulation.DefenseOverride);
        Assert.Equal(I("direction"), completed.Simulation.DirectionX);
        Assert.Equal(I("directionY"), completed.Simulation.DirectionY);
        Assert.Equal(F("rotation"), completed.Simulation.Rotation);
        Assert.Equal(F("knockback"), completed.Simulation.KnockBackResist);
        Assert.Equal(B("noGravity"), completed.Simulation.NoGravity);
        Assert.Equal(B("noTile"), completed.Simulation.NoTileCollide);
        Assert.Equal(B("dontTakeDamage"), completed.Simulation.DontTakeDamage);
        Assert.Equal(B("collideX"), completed.Simulation.CollideX);
        Assert.Equal(B("collideY"), completed.Simulation.CollideY);
        // Complete UpdateNPC consumes netUpdate in its server replication tail. Whole-AI fixtures below
        // that boundary separately verify the retained forced-update intent before publication.
        if (B("nearPeer"))
        {
            Assert.True(store.TryGetActive(50, out var peer));
            Assert.Equal(F("peerVx"), peer.VelocityX);
            Assert.Equal(F("peerVy"), peer.VelocityY);
        }
        Assert.Equal(row.GetProperty("spawned").GetArrayLength(), sink.Children.Count);
        foreach (var child in row.GetProperty("spawned").EnumerateArray())
        {
            Assert.Single(sink.Children);
            Assert.Equal(child.GetProperty("slot").GetInt32(), sink.Children[0].Handle.Slot);
            Assert.Equal(child.GetProperty("x").GetSingle(), sink.Children[0].PositionX);
            Assert.Equal(child.GetProperty("y").GetSingle(), sink.Children[0].PositionY);
        }
        Assert.Equal(I("nextRandom"), random.NextInt32(0, int.MaxValue));
        // UpdateNPC's later FindFrame and CheckActive are separate owners, outside this accepted AI/physics seam.
    }

    private static WorldTileStore Tiles(bool solid)
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 500));
        for (int x = 0; x < 600; x++)
            tiles.Set(x, 80, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        for (int x = 60; x <= 70; x++)
            for (int y = 61; y <= 65; y++)
                tiles.Set(x, y, new WorldTile { Type = 1, Flags = solid ? WorldTileFlags.Active : 0 });
        return tiles;
    }

    internal static NpcStateUpdate Initial(int type, float phase, float clock, bool expert = false)
    {
        VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type), out var definition);
        int lifeMax = type == 478 ? 200 : definition.LifeMax * (expert ? 2 : 1);
        float ai1 = phase is 4.1f or 4.2f ? 65f : phase == 3.2f ? 1f : clock;
        return new(type, (short)type, 1000, 1000, 0, 0, 0,
            new(phase, ai1, phase is 4.1f or 4.2f ? 80f : .5f, phase == 4.2f ? clock : 0f),
            NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                SpriteDirection = 1,
                Rotation = .2f,
                Life = type == 478 ? 100 : lifeMax,
                LifeMax = lifeMax,
                TimeLeft = 750,
                BaseDamage = definition.Damage * (expert ? 2 : 1),
                BaseDefense = definition.Defense,
                DamageOverride = definition.Damage * (expert ? 2 : 1),
                DefenseOverride = definition.Defense,
                BaseLifeMax = lifeMax,
                SpawnDifficulty = expert ? 2f : 1f,
                NoGravity = type != 478,
                KnockBackResist = 1f
            });
    }

    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    internal sealed class Mutations(RuntimeNpcStore store) : INpcAiCommittedNpcMutationSink
    {
        public List<NpcSnapshot> Children
        {
            get;
        } = [];
        public bool TryGetActive(byte slot, out NpcSnapshot npc) => store.TryGetActive(slot, out npc);
        public bool TryUpdateState(in NpcSnapshot expected, in NpcStateUpdate update, out NpcSnapshot committed)
        {
            committed = default;
            return store.TryGet(expected.Handle, out var current) && current == expected &&
                store.TryUpdateUnpublished(expected.Handle, in update, out committed);
        }
        public bool TrySpawn(in NpcSnapshot source, in NpcAiSpawnIntent intent, out NpcSnapshot spawned)
        {
            bool success = store.TrySpawnIntent(in source, in intent, out spawned);
            if (success)
                Children.Add(spawned);
            return success;
        }
        public bool TrySpawn(in NpcAiSpawnIntent intent, out NpcSnapshot spawned) => throw new NotSupportedException();
        public bool TryUpdateAi(in NpcSnapshot expected, NpcAiState ai, out NpcSnapshot committed) => throw new NotSupportedException();
        public int TryHeal(NpcHandle npc, int maximumAmount) => throw new NotSupportedException();
        public bool TrySpawnProjectile(in NpcSnapshot source, in NpcAiProjectileIntent intent, out ProjectileSnapshot spawned) => throw new NotSupportedException();
        public bool TryAnnounceSkeletronTaunt(in NpcSnapshot source, int variant) => throw new NotSupportedException();
        public bool TryUpdateVelocity(NpcHandle npc, float x, float y, out NpcSnapshot committed) => throw new NotSupportedException();
        public bool TryDespawn(NpcHandle npc) => throw new NotSupportedException();
        public bool TryTranslate(NpcHandle npc, float x, float y, out NpcSnapshot committed) => throw new NotSupportedException();
        public bool TryLinkFollower(NpcHandle npc, byte slot) => throw new NotSupportedException();
    }
}
