using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class DarkCasterAiTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Creation_prefix_reentry_cannot_publish_a_sphere_for_a_newer_caster(bool replace)
    {
        var row = Rows.First(x => x.GetProperty("good").GetBoolean() && x.GetProperty("children").GetArrayLength() == 1 &&
            x.GetProperty("before").GetProperty("ai")[0].GetSingle() == 98);
        var (store, ai, npc, random) = Setup(row, row.GetProperty("before"), row.GetProperty("randomBefore"));
        random.BeforeDraw = () =>
        {
            Assert.True(store.TryGet(npc.Handle, out var current));
            var update = new NpcStateUpdate(current.Type, current.NetId, 777, current.PositionY, 0, 0, current.Target, current.Ai, current.Simulation);
            if (replace)
            {
                Assert.True(store.TryDespawn(current.Handle));
                Assert.True(store.TrySpawn(current.Handle.Slot, in update, out _));
            }
            else Assert.True(store.TryUpdate(current.Handle, in update, out _));
        };
        new RuntimeNpcAiStateExecutor(store).Tick(new CasterOnly(ai));
        Assert.False(store.TryGetActive(row.GetProperty("children")[0].GetProperty("slot").GetByte(), out _));
        var expected = new CapturedRandom(row.GetProperty("randomBefore")); expected.NextInt32(0, 3);
        random.AssertSame(expected); Assert.Equal(1, random.Calls);
    }

    private static readonly JsonElement[] Edges = Read("CasterTeleportEdge1458", "a785b8a748e753940f56a955f1888e3a469a8de664768f37de45f4c1e6319e75");
    public static TheoryData<int> EdgeCases => new(Enumerable.Range(0, Edges.Length));

    [Theory, MemberData(nameof(EdgeCases))]
    public void Valid_search_windows_touching_world_edges_preserve_original_search_and_RNG(int index)
    {
        var row = Edges[index]; var before = row.GetProperty("before");
        var world = new WorldTileStore(new WorldDimensions(400, 400));
        int floorY = row.GetProperty("floorY").GetInt32(), tx = row.GetProperty("targetX").GetInt32(), ty = row.GetProperty("targetY").GetInt32();
        for (int x = 0; x < 400; x++)
        {
            world.Set(x, floorY, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            world.Set(x, floorY - 1, new WorldTile { Wall = 7 });
        }
        var random = new CapturedRandom(row.GetProperty("randomBefore"));
        bool found = new VanillaCasterWorldEnvironment(world).TryFindTeleportSpot(
            before.GetProperty("x").GetSingle() + 9, before.GetProperty("y").GetSingle() + 20,
            tx, ty, false, [new(0,tx * 16,ty * 16,0,true,false,false,false)], random, out int chosenX, out int chosenY);
        Assert.Equal(row.GetProperty("found").GetBoolean(), found);
        if (found) { Assert.Equal(row.GetProperty("x").GetSingle(), chosenX); Assert.Equal(row.GetProperty("y").GetSingle(), chosenY); }
        random.AssertState(row.GetProperty("randomAfter"));
    }

    private static readonly JsonElement[] Coupled = Read("CasterSphereCoupled1458", "7e328c3a6f501272c5252872f9be8d31dcb0feb4ddded273e3ce96d3d060a612");
    public static TheoryData<int> CoupledCases => new(Enumerable.Range(0, Coupled.Length));
    private static readonly JsonElement[] Rows = Read("DarkCasterAi1458", "3dec0335a7463c1ae1d53d0b77117e12a79ddcd396d92ac6b1b32fcaf2e8123c");
    private static readonly JsonElement[] Traces = Read("DarkCasterTrace1458", "0d41754fa5ebd91aba65ea1b6801cc67330317f03794156b87d745094e3e6358");
    private static readonly JsonElement[] Queries = Read("CasterTeleportQuery1458", "1bc4b5096cc037e83b8a29f01e2f53803fac871314cd282c1cbb18ae352f0690");
    private static readonly JsonElement[] WindowsQueries = Read("CasterTeleportWindows1458", "a9cf39b46fc556d641f9f42df2d07cb945881bc384f1582cb4ec3c3de74a8f8b");
    private static readonly JsonElement[] Firing = Read("NpcFiringDistance1458", "1300c160baac6a0a111e79f9bb6d076cbd12b3a9a3a329637b743ad0a16077b9");
    private static readonly WorldTileStore[] AiWorlds = Enumerable.Range(0, 5).Select(i => World(i, false)).ToArray();
    private static readonly WorldTileStore[] QueryWorlds = Enumerable.Range(0, 10).Select(i => World(i, true)).ToArray();
    public static TheoryData<int> Cases => new(Enumerable.Range(0, Rows.Length));
    public static TheoryData<int> TraceCases => new(Enumerable.Range(0, Traces.Length));
    public static TheoryData<int> QueryCases => new(Enumerable.Range(0, Queries.Length + WindowsQueries.Length));
    public static TheoryData<int> FiringCases => new(Enumerable.Range(0, Firing.Length));

    [Theory, MemberData(nameof(CoupledCases))]
    public void Ascending_caster_and_new_sphere_pass_matches_original_including_full_table(int index)
    {
        var row = Coupled[index];
        var (store, ai, npc, random) = Setup(row, row.GetProperty("before"), row.GetProperty("randomBefore"), slot: 0);
        if (row.GetProperty("full").GetBoolean())
            for (int i = 1; i < 200; i++)
                if (!store.TryGetActive((byte)i, out _))
                    Assert.True(store.TrySpawn((byte)i, new(1,1,0,0,0,0,0,default,NpcSimulationState.Initial), out _));
        int children = row.GetProperty("children").GetArrayLength();
        Assert.Equal(1 + children, new RuntimeNpcAiStateExecutor(store).Tick(new CasterOnly(ai, true)).Applied);
        Assert.True(store.TryGet(npc.Handle, out var after)); AssertState(row.GetProperty("afterCaster"), after);
        foreach (var child in row.GetProperty("children").EnumerateArray())
        {
            Assert.True(store.TryGetActive(child.GetProperty("slot").GetByte(), out var sphere));
            AssertState(child.GetProperty("after"), sphere);
        }
        random.AssertState(row.GetProperty("randomAfter"));
    }

    [Theory, MemberData(nameof(Cases))]
    public void Committed_caster_matches_original_AI_children_and_RNG(int index)
    {
        var row = Rows[index];
        var (store, ai, npc, random) = Setup(row, row.GetProperty("before"), row.GetProperty("randomBefore"));
        Assert.True(ai.TryStepState(in npc, out _)); Assert.True(ai.TryStepState(in npc, out _));
        random.AssertState(row.GetProperty("randomBefore"));
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(store).Tick(new CasterOnly(ai)).Applied);
        Assert.True(store.TryGet(npc.Handle, out var after));
        AssertState(row.GetProperty("after"), after); AssertChildren(row, store);
        random.AssertState(row.GetProperty("randomAfter"));
    }

    [Theory, MemberData(nameof(TraceCases))]
    public void Five_calls_preserve_queued_teleport_marker_and_ordered_RNG(int index)
    {
        var row = Traces[index]; var trace = row.GetProperty("trace");
        var (store, ai, npc, random) = Setup(row, trace[0].GetProperty("before"), trace[0].GetProperty("randomBefore"));
        var executor = new RuntimeNpcAiStateExecutor(store);
        for (int step = 0; step < trace.GetArrayLength(); step++)
        {
            if (step == 2 && store.TryGetActive(5, out var head)) Assert.True(store.TryDespawn(head.Handle));
            Assert.True(store.TryGet(npc.Handle, out var before)); AssertState(trace[step].GetProperty("before"), before);
            random.AssertState(trace[step].GetProperty("randomBefore"));
            Assert.Equal(1, executor.Tick(new CasterOnly(ai)).Applied);
            Assert.True(store.TryGet(npc.Handle, out var after)); AssertState(trace[step].GetProperty("after"), after);
            AssertChildren(trace[step], store); random.AssertState(trace[step].GetProperty("randomAfter"));
        }
    }

    [Theory, MemberData(nameof(QueryCases))]
    public void Teleport_query_matches_original_geometry_players_and_RNG(int index)
    {
        bool fna = index < Queries.Length;
        var row = fna ? Queries[index] : WindowsQueries[index - Queries.Length]; var before = row.GetProperty("before");
        var random = new CapturedRandom(row.GetProperty("randomBefore"));
        int actor = row.GetProperty("actor").GetInt32();
        var candidate = new VanillaNpcTargetCandidate(0, 1510, 1021, 0, actor != 4, actor == 2, actor == 3, false)
            { VelocityX = actor == 0 ? 0 : 20, VelocityY = actor == 0 ? 0 : 15 };
        bool found = new VanillaCasterWorldEnvironment(QueryWorlds[row.GetProperty("layout").GetInt32()], useFnaRectangleUnion: fna).TryFindTeleportSpot(
            before.GetProperty("x").GetSingle() + before.GetProperty("width").GetInt32() * .5f,
            before.GetProperty("y").GetSingle() + before.GetProperty("height").GetInt32() * .5f,
            94, 63, row.GetProperty("head").GetBoolean(), [candidate], random, out int x, out int y);
        Assert.Equal(row.GetProperty("found").GetBoolean(), found);
        if (found) { Assert.Equal(row.GetProperty("x").GetSingle(), x); Assert.Equal(row.GetProperty("y").GetSingle(), y); }
        random.AssertState(row.GetProperty("randomAfter"));
    }

    [Fact]
    public void Default_world_query_uses_the_original_host_platform_rectangle_behavior()
    {
        var row = OperatingSystem.IsWindows() ? WindowsQueries[74] : Queries[74];
        var random = new CapturedRandom(row.GetProperty("randomBefore"));
        var before = row.GetProperty("before");
        var player = new VanillaNpcTargetCandidate(0, 1510, 1021, 0, true, false, false, false)
            { VelocityX = 20, VelocityY = 15 };
        Assert.True(new VanillaCasterWorldEnvironment(QueryWorlds[1]).TryFindTeleportSpot(
            before.GetProperty("x").GetSingle() + 9, before.GetProperty("y").GetSingle() + 20,
            94, 63, false, [player], random, out int x, out int y));
        Assert.Equal(row.GetProperty("x").GetSingle(), x); Assert.Equal(row.GetProperty("y").GetSingle(), y);
        random.AssertState(row.GetProperty("randomAfter"));
    }

    [Theory, MemberData(nameof(FiringCases))]
    public void Shared_firing_rectangle_matches_original_fractional_edges(int index)
    {
        var r = Firing[index];
        Assert.Equal(r.GetProperty("allowed").GetBoolean(), VanillaNpcGlobalFiringDistance.Contains(
            r.GetProperty("sx").GetSingle(), r.GetProperty("sy").GetSingle(), r.GetProperty("tx").GetSingle(), r.GetProperty("ty").GetSingle()));
    }

    [Fact]
    public void Teleport_publishes_only_final_attack_timer()
    {
        var row = TeleportCase(); var notifications = new Notifications();
        var (store, ai, npc, random) = Setup(row, row.GetProperty("before"), row.GetProperty("randomBefore"), notifications);
        notifications.Updates.Clear();
        new RuntimeNpcAiStateExecutor(store).Tick(new CasterOnly(ai));
        var update = Assert.Single(notifications.Updates);
        AssertState(row.GetProperty("after"), update);
        Assert.Equal(19f, update.Ai.Ai1);
        AssertChildren(row, store); random.AssertState(row.GetProperty("randomAfter"));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void Superseded_proposal_does_not_consume_RNG_or_create_spheres(bool invalid)
    {
        var row = TeleportCase();
        var (store, ai, npc, random) = Setup(row, row.GetProperty("before"), row.GetProperty("randomBefore"));
        var stepper = new AlteredProposal(ai, store, invalid);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(store).Tick(stepper).Rejected);
        random.AssertState(row.GetProperty("randomBefore"));
        Assert.Equal(row.GetProperty("headMarker").GetInt32() >= 0 ? 2 : 1, store.ActiveCount);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void Teleport_query_cannot_overwrite_a_new_revision_or_generation(bool replace)
    {
        var row = TeleportCase(); var notifications = new Notifications();
        var (store, ai, npc, random) = Setup(row, row.GetProperty("before"), row.GetProperty("randomBefore"), notifications);
        int drawsAtMutation = 0;
        ai.SetCasterEnvironment(new QueryCallback(new VanillaCasterWorldEnvironment(AiWorlds[1]), () =>
        {
            Assert.True(store.TryGet(npc.Handle, out var current));
            var update = FromOriginal(32, row.GetProperty("before")) with { PositionX = 1777 };
            if (replace) { Assert.True(store.TryDespawn(current.Handle)); Assert.True(store.TrySpawn(10, in update, out _)); }
            else Assert.True(store.TryUpdate(current.Handle, in update, out _));
            drawsAtMutation = random.Calls;
        }));
        notifications.Updates.Clear();
        new RuntimeNpcAiStateExecutor(store).Tick(new CasterOnly(ai));
        Assert.True(store.TryGetActive(10, out var after)); Assert.Equal(1777, after.PositionX);
        Assert.True(drawsAtMutation > 0); Assert.Equal(drawsAtMutation, random.Calls);
        Assert.Equal(replace ? 0 : 1, notifications.Updates.Count);
        Assert.Equal(1, store.ActiveCount);
    }

    [Fact]
    public void Publication_observer_supersedes_spawn_and_dust_continuation()
    {
        var row = Rows.First(r => r.GetProperty("headMarker").GetInt32() == -1 &&
            r.GetProperty("children").GetArrayLength() == 1 && r.GetProperty("timer").GetInt32() == 0);
        var notifications = new Notifications();
        var (store, ai, npc, random) = Setup(row, row.GetProperty("before"), row.GetProperty("randomBefore"), notifications);
        notifications.Callback = () =>
        {
            notifications.Callback = null;
            Assert.True(store.TryUpdate(npc.Handle, FromOriginal(32, row.GetProperty("before")) with { PositionX = 1777 }, out _));
        };
        new RuntimeNpcAiStateExecutor(store).Tick(new CasterOnly(ai));
        Assert.Equal(1, store.ActiveCount); random.AssertState(row.GetProperty("randomBefore"));
        Assert.True(store.TryGet(npc.Handle, out var current)); Assert.Equal(1777, current.PositionX);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void World_motion_keeps_caster_AI_and_pre_motion_sphere_anchor(bool teleport)
    {
        var row = teleport ? TeleportCase() : Rows.First(r => r.GetProperty("headMarker").GetInt32() == -1 &&
            r.GetProperty("children").GetArrayLength() == 1 && r.GetProperty("timer").GetInt32() == 0);
        var (store, ai, npc, random) = Setup(row, row.GetProperty("before"), row.GetProperty("randomBefore"));
        var wrapped = new VanillaNpcWorldMotionAiStepper(new CasterOnly(ai), AiWorlds[row.GetProperty("layout").GetInt32()]);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(store).Tick(wrapped).Applied);
        Assert.True(store.TryGet(npc.Handle, out var after));
        Assert.Equal(ReadAi(row.GetProperty("after").GetProperty("ai")), after.Ai);
        Assert.NotEqual(row.GetProperty("after").GetProperty("y").GetSingle(), after.PositionY);
        AssertChildren(row, store); random.AssertState(row.GetProperty("randomAfter"));
    }

    private static JsonElement TeleportCase() => Rows.First(r => r.GetProperty("mode").GetInt32() == 0 &&
        !r.GetProperty("good").GetBoolean() && r.GetProperty("headMarker").GetInt32() == -1 &&
        r.GetProperty("localMarker").GetInt32() == 0 && r.GetProperty("layout").GetInt32() == 1 &&
        r.GetProperty("timer").GetInt32() == 649 && r.GetProperty("attackTimer").GetInt32() == 26);

    private sealed class Notifications : INpcStateCommitSink
    {
        public List<NpcSnapshot> Updates { get; } = [];
        public Action? Callback { get; set; }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc)
        {
            if (kind == NpcStateCommitKind.Update && npc.Type == 32) { Updates.Add(npc); Callback?.Invoke(); }
        }
    }

    private sealed class AlteredProposal(INpcAiStateStepper inner, RuntimeNpcStore store, bool invalid) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            if (npc.Type != 32 || !inner.TryStepState(in npc, out next)) return false;
            if (invalid) next = next with { VelocityX = float.NaN };
            else Assert.True(store.TryUpdate(npc.Handle, next with { PositionX = 1777 }, out _));
            return true;
        }
    }

    private sealed class QueryCallback(IVanillaCasterEnvironment inner, Action callback) : IVanillaCasterEnvironment
    {
        public bool TryFindTeleportSpot(float x, float y, int tx, int ty, bool head,
            ReadOnlySpan<VanillaNpcTargetCandidate> players, IVanillaNpcRandom random, out int chosenX, out int chosenY)
        {
            bool result = inner.TryFindTeleportSpot(x, y, tx, ty, head, players, random, out chosenX, out chosenY);
            callback(); return result;
        }
    }

    private static (RuntimeNpcStore, VanillaNpcTargetingAiStepper, NpcSnapshot, CapturedRandom) Setup(JsonElement row, JsonElement before, JsonElement rng, INpcStateCommitSink? sink = null, byte slot = 10)
    {
        var random = new CapturedRandom(rng); var store = new RuntimeNpcStore(commitSink: sink);
        bool good = row.GetProperty("good").GetBoolean(); int mode = row.GetProperty("mode").GetInt32();
        int marker = row.GetProperty("headMarker").GetInt32();
        if (marker >= 0) Assert.True(store.TrySpawn(5, new(35,35,960,890,0,0,0,new(1,0,0,marker),NpcSimulationState.Initial),out _));
        Assert.True(store.TrySpawn(slot, FromOriginal(32, before), out var npc));
        store.SetVanillaSpawnRandomSource(random);
        store.SetVanillaSpawnContextSource(() => new(mode + 1 + (good ? 1 : 0), 1, good)
            { SkeletronActive = store.TryGetActive(5, out _) });
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: random);
        ai.SetWorldConditions(false, false, good, mode > 0 || good, mode > 1 || good && mode > 0);
        ai.SetCandidates([new(0,1510,1021,0,true,false,false,false)]);
        ai.SetCasterEnvironment(new VanillaCasterWorldEnvironment(AiWorlds[row.TryGetProperty("layout", out var layout) ? layout.GetInt32() : 0]));
        Span<NpcSnapshot> peers = stackalloc NpcSnapshot[2]; int count = store.CopyActive(peers); ai.SetNpcPeers(peers[..count]);
        return (store, ai, npc, random);
    }

    private static void AssertChildren(JsonElement row, RuntimeNpcStore store)
    {
        var children = row.GetProperty("children");
        int count = 0;
        for (int i = 0; i < 200; i++) if (store.TryGetActive((byte)i, out var n) && n.Type == 33) count++;
        Assert.Equal(children.GetArrayLength(), count);
        foreach (var child in children.EnumerateArray())
        {
            Assert.True(store.TryGetActive(child.GetProperty("slot").GetByte(), out var npc));
            AssertState(child.GetProperty("state"), npc);
        }
    }

    private static WorldTileStore World(int layout, bool query)
    {
        var world = new WorldTileStore(new WorldDimensions(400,400));
        for (int x = 0; x < 400; x++)
        {
            var floor = new WorldTile { Type = (ushort)(query && layout == 4 ? 19 : 1),
                Flags = layout == 0 ? WorldTileFlags.None : WorldTileFlags.Active };
            if (query && layout == 5) floor.Flags |= WorldTileFlags.Inactive;
            var above = new WorldTile { Wall = (ushort)(query ? (layout == 3 ? 0 : 7) : (layout is 1 or 3 ? 7 : 0)) };
            if (query && layout is 6 or 7 or 8)
            {
                above.Type = (ushort)(layout == 7 ? 19 : 1); above.Flags = WorldTileFlags.Active;
                if (layout == 8) above.Flags |= WorldTileFlags.Inactive;
            }
            bool lava = query ? layout is 2 or 3 or 9 : layout >= 3;
            above.LiquidKind = lava ? WorldLiquidKind.Lava : WorldLiquidKind.Water;
            above.LiquidAmount = (byte)(lava && !(query && layout == 9) ? 128 : 0);
            world.Set(x,80,in floor); world.Set(x,79,in above);
        }
        return world;
    }

    private sealed class CasterOnly(INpcAiStateStepper inner, bool includeSpheres = false) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        { next = default; return (npc.Type == 32 || includeSpheres && npc.Type == 33) && inner.TryStepState(in npc, out next); }
    }
    private static NpcStateUpdate FromOriginal(int type, JsonElement row) => new(type, (short)type,
        row.GetProperty("x").GetSingle(), row.GetProperty("y").GetSingle(),
        row.GetProperty("vx").GetSingle(), row.GetProperty("vy").GetSingle(),
        row.GetProperty("target").GetUInt16(), ReadAi(row.GetProperty("ai")),
        NpcSimulationState.Initial with
        {
            Life = row.GetProperty("life").GetInt32(), LifeMax = row.GetProperty("lifeMax").GetInt32(),
            DirectionX = row.GetProperty("direction").GetInt32(), DirectionY = row.GetProperty("directionY").GetInt32(),
            SpriteDirection = row.GetProperty("spriteDirection").GetInt32(), Rotation = row.GetProperty("rotation").GetSingle(),
            DamageOverride = row.GetProperty("damage").GetInt32(), DefenseOverride = row.GetProperty("defense").GetInt32(),
            Alpha = row.GetProperty("alpha").GetInt32(), Scale = row.GetProperty("scale").GetSingle(),
            NoGravity = row.GetProperty("noGravity").GetBoolean(), NoTileCollide = row.GetProperty("noTileCollide").GetBoolean(),
            DontTakeDamage = row.GetProperty("dontTakeDamage").GetBoolean(), TimeLeft = row.GetProperty("timeLeft").GetInt32(),
            LocalAi = ReadAi(row.GetProperty("localAi")), JustHit = row.GetProperty("justHit").GetBoolean()
        });

    private static NpcAiState ReadAi(JsonElement ai) => new(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle());

    private static void AssertState(JsonElement row, NpcSnapshot npc)
    {
        Assert.Equal(row.GetProperty("active").GetBoolean(), npc.IsActive);
        Assert.Equal(row.GetProperty("x").GetSingle(), npc.PositionX);
        Assert.Equal(row.GetProperty("y").GetSingle(), npc.PositionY);
        Assert.Equal(row.GetProperty("vx").GetSingle(), npc.VelocityX);
        Assert.Equal(row.GetProperty("vy").GetSingle(), npc.VelocityY);
        Assert.Equal(row.GetProperty("target").GetInt32(), npc.Target);
        Assert.Equal(ReadAi(row.GetProperty("ai")), npc.Ai);
        Assert.Equal(ReadAi(row.GetProperty("localAi")), npc.Simulation.LocalAi);
        Assert.Equal(row.GetProperty("direction").GetInt32(), npc.Simulation.DirectionX);
        Assert.Equal(row.GetProperty("directionY").GetInt32(), npc.Simulation.DirectionY);
        Assert.Equal(row.GetProperty("spriteDirection").GetInt32(), npc.Simulation.SpriteDirection);
        Assert.Equal(row.GetProperty("rotation").GetSingle(), npc.Simulation.Rotation);
        Assert.Equal(row.GetProperty("dontTakeDamage").GetBoolean(), npc.Simulation.DontTakeDamage);
        Assert.Equal(row.GetProperty("timeLeft").GetInt32(), npc.Simulation.TimeLeft);
        Assert.Equal(row.GetProperty("alpha").GetInt32(), npc.Simulation.Alpha);
        Assert.Equal(row.GetProperty("justHit").GetBoolean(), npc.Simulation.JustHit);
        Assert.Equal(row.GetProperty("life").GetInt32(), npc.Simulation.Life);
        Assert.Equal(row.GetProperty("lifeMax").GetInt32(), npc.Simulation.LifeMax);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out var combatDefinition));
        Assert.Equal(row.GetProperty("damage").GetInt32(), npc.Simulation.DamageOverride ?? combatDefinition.Damage);
        Assert.Equal(row.GetProperty("defense").GetInt32(), npc.Simulation.DefenseOverride ?? combatDefinition.Defense);
        Assert.Equal(row.GetProperty("noGravity").GetBoolean(), npc.Simulation.NoGravity);
        Assert.Equal(row.GetProperty("noTileCollide").GetBoolean(), npc.Simulation.NoTileCollide);
        Assert.Equal(row.GetProperty("scale").GetSingle(), npc.Simulation.Scale);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out var definition));
        Assert.True(definition.TryResolveHitbox(npc.Simulation, out var hitbox));
        Assert.Equal(row.GetProperty("width").GetInt32(), hitbox.Width);
        Assert.Equal(row.GetProperty("height").GetInt32(), hitbox.Height);
    }

    private sealed class CapturedRandom : IVanillaNpcRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(0);
        private static readonly FieldInfo Index = typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo Seeds = typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.Instance | BindingFlags.NonPublic)!;
        public int Calls { get; private set; }
        public Action? BeforeDraw { get; set; }
        public CapturedRandom(JsonElement state)
        {
            Index.SetValue(random, state.GetProperty("index").GetUInt32());
            state.GetProperty("seed").EnumerateArray().Select(x => x.GetInt32()).ToArray().CopyTo((int[])Seeds.GetValue(random)!, 0);
        }
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Assert.True(exclusiveMax > inclusiveMin);
            Calls++;
            var callback = BeforeDraw; BeforeDraw = null; callback?.Invoke();
            return random.Next(inclusiveMin, exclusiveMax);
        }
        public double NextDouble() => throw new InvalidOperationException("AI9 does not request doubles.");
        public void AssertSame(CapturedRandom other)
        {
            Assert.Equal(Index.GetValue(other.random), Index.GetValue(random));
            Assert.Equal((int[])Seeds.GetValue(other.random)!, (int[])Seeds.GetValue(random)!);
        }
        public void AssertState(JsonElement state)
        {
            Assert.Equal(state.GetProperty("index").GetUInt32(), (uint)Index.GetValue(random)!);
            Assert.Equal(state.GetProperty("seed").EnumerateArray().Select(x => x.GetInt32()), (int[])Seeds.GetValue(random)!);
        }
    }

    private static JsonElement[] Read(string name, string hash)
    {
        using var resource = typeof(DarkCasterAiTests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
