using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringDartTrap1458Tests
{
    public static IEnumerable<object[]> Births() => Rows("WiringTrapBirth1458");
    public static IEnumerable<object[]> Circuits() => Rows("WiringTrapCircuit1458");
    public static IEnumerable<object[]> Continuations() => Rows("WiringTrapContinuation1458");
    public static IEnumerable<object[]> NpcHits() => Rows("WiringTrapNpcHit1458");
    public static IEnumerable<object[]> NpcDeaths() => Rows("WiringTrapNpcDeath1458");
    public static IEnumerable<object[]> VenomDots() => Rows("WiringTrapVenomDot1458");

    private static IEnumerable<object[]> Rows(string name)
    {
        using var input = typeof(WiringDartTrap1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        Assert.Equal("4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034", json.RootElement.GetProperty("officialSha256").GetString());
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Births))]
    public void Source_trap_birth_and_duplicate_match_original(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var fixture = new Fixture(row, 65, 136);
        var device = new WorldWiringDeviceExecutor1458(fixture.Tiles, fireTrap: (x, y) => fixture.Fire(x, y));
        bool supported = row.GetProperty("style").GetInt32() is >= 0 and <= 5;
        Assert.Equal(supported, device.Hit(65, 65, WorldWireChannel.Red, (_, _) => { }));
        if (supported) EqualProjectiles(row.GetProperty("first"), fixture.Shots);
        else Assert.Equal(0, fixture.Shots.ActiveCount);
        Assert.Equal(supported ? 1 : 0, fixture.Circuit.RegisteredMechanismCount);
        var before = fixture.Random.Clone();
        Assert.False(device.Hit(65, 65, WorldWireChannel.Blue, (_, _) => { }));
        Assert.True(before.HasSameState(fixture.Random));
        if (supported) EqualProjectiles(row.GetProperty("duplicate"), fixture.Shots);
    }

    [Theory, MemberData(nameof(Circuits))]
    public void Real_world_switch_plate_timer_wire_dart_and_shared_cooldown_match_original_timeline(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var fixture = new Fixture(row, 70, row.GetProperty("origin").GetInt32());
        Assert.True(fixture.Circuit.HitSwitch(65, 65));
        int tick = 0;
        foreach (var point in row.GetProperty("events").EnumerateArray())
        {
            while (tick < point.GetProperty("tick").GetInt32()) { fixture.Authority.TickMechanisms(); tick++; }
            EqualProjectiles(point.GetProperty("projectiles"), fixture.Shots);
            Assert.Equal(point.GetProperty("mechs").GetInt32(), fixture.Circuit.RegisteredMechanismCount);
            var rng = point.GetProperty("rng"); const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
            Assert.Equal(rng.GetProperty("inext").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", access)!.GetValue(fixture.Random)!);
            Assert.Equal(rng.GetProperty("SeedArray").EnumerateArray().Select(x => x.GetInt32()),
                (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", access)!.GetValue(fixture.Random)!);
        }
    }

    private static void EqualProjectiles(JsonElement expected, RuntimeProjectileStore store)
    {
        Assert.Equal(expected.GetArrayLength(), store.ActiveCount);
        foreach (var item in expected.EnumerateArray())
        {
            Assert.True(store.TryGetActive(item.GetProperty("whoAmI").GetUInt16(), out var shot));
            Assert.Equal(item.GetProperty("type").GetInt32(), shot.Type.Value);
            Assert.Equal(item.GetProperty("owner").GetByte(), shot.Spawner);
            Assert.Equal(item.GetProperty("position").GetProperty("X").GetSingle(), shot.PositionX);
            Assert.Equal(item.GetProperty("position").GetProperty("Y").GetSingle(), shot.PositionY);
            Assert.Equal(item.GetProperty("velocity").GetProperty("X").GetSingle(), shot.VelocityX);
            Assert.Equal(item.GetProperty("velocity").GetProperty("Y").GetSingle(), shot.VelocityY);
            Assert.Equal(item.GetProperty("damage").GetInt16(), shot.Damage);
            Assert.Equal(item.GetProperty("originalDamage").GetInt16(), shot.OriginalDamage);
            Assert.Equal(item.GetProperty("knockBack").GetSingle(), shot.KnockBack);
            Assert.True(store.IsCombatTrusted(shot.Handle)); Assert.False(store.TryGetCombatTrustedOwner(shot.Handle, out _));
            Assert.True(store.TryGetLifecycle(shot.Handle, out var life));
            Assert.Equal(item.GetProperty("timeLeft").GetInt32(), life.TimeLeft);
            Assert.Equal(item.GetProperty("npcProj").GetBoolean(), life.NpcProjectile);
        }
    }

    [Theory, MemberData(nameof(Continuations))]
    public void Actual_projectile_owner_matches_original_air_wall_and_world_edge_lifetime(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var fixture = new Fixture(row, 65, 136);
        if (row.GetProperty("barrier").GetBoolean())
        {
            int orientation = row.GetProperty("orientation").GetInt32();
            int dx = orientation == 0 ? -1 : orientation == 1 ? 1 : 0, dy = orientation >= 4 ? 1 : orientation >= 2 ? -1 : 0;
            for (int offset = -3; offset <= 3; offset++) fixture.Tiles.Set(65 + dx * 10 + (dx == 0 ? offset : 0), 65 + dy * 10 + (dy == 0 ? offset : 0),
                new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        }
        Assert.True(fixture.Fire(65, 65));
        foreach (var point in row.GetProperty("frames").EnumerateArray())
        {
            Assert.True(fixture.Actors.TryTickState());
            EqualProjectiles(point.GetProperty("projectiles"), fixture.Shots);
            if (fixture.Shots.TryGetActive(0, out var shot))
            {
                var expected = point.GetProperty("projectiles")[0].GetProperty("ai");
                Assert.Equal(expected[0].GetSingle(), shot.Ai.Ai0);
                Assert.Equal(expected[1].GetSingle(), shot.Ai.Ai1);
                Assert.Equal(expected[2].GetSingle(), shot.Ai.Ai2);
            }
            var rng = point.GetProperty("rng"); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Assert.Equal(rng.GetProperty("inext").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(fixture.Random)!);
            Assert.Equal(rng.GetProperty("SeedArray").EnumerateArray().Select(v => v.GetInt32()),
                (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(fixture.Random)!);
        }
        Assert.Equal(0, fixture.Shots.ActiveCount);
    }

    [Theory, MemberData(nameof(VenomDots))]
    public void Retained_venom_counter_flags_and_visual_entropy_match_original(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        int type = row.GetProperty("type").GetInt32(); int mask = row.GetProperty("flagsMask").GetInt32();
        var store = new RuntimeNpcStore();
        Assert.True(store.TrySpawnVanilla(new(type, (short)type, 1040, 1040, 0, 0, 255,
            new NpcAiState(0, row.GetProperty("held").GetInt32(), 0, 0),
            NpcSimulationState.Initial with
            {
                Life = 1000,
                LifeMax = 1000,
                LifeRegenCounter = row.GetProperty("counter").GetInt32(),
                LiquidContact = row.GetProperty("lava").GetBoolean() ? NpcLiquidContactKind.Lava : NpcLiquidContactKind.None,
                DontTakeDamage = row.GetProperty("protection").GetInt32() == 2,
                Immortal = row.GetProperty("protection").GetInt32() == 1
            }), out var npc));
        Assert.True(TerraRuntime.Gameplay.Npcs.VanillaNpcDebuffRegeneration1458.TryStep(in npc, row.GetProperty("good").GetBoolean(),
            (mask & 2) != 0, (mask & 4) != 0, (mask & 1) != 0, out var result));
        Assert.Equal(row.GetProperty("life").GetInt32(), result.Life);
        Assert.Equal(row.GetProperty("afterCounter").GetInt32(), result.Counter);
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var plan = new RuntimeNpcBuffPlan1458(npc, 1, default, new((mask & 4) != 0, false, (mask & 2) != 0), false, false,
            result.Life, result.Counter, result.Damage)
        { Venom = (mask & 1) != 0 };
        RuntimeNpcBuffStatus1458.PlanVisualOffers(in plan, new NpcRuntimeTownCombatRandom1458(new SystemVanillaNpcRandom(random)), row.GetProperty("good").GetBoolean());
        var rng = row.GetProperty("rng"); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(rng.GetProperty("inext").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!);
        Assert.Equal(rng.GetProperty("SeedArray").EnumerateArray().Select(v => v.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(59)]
    public void Venom_duration_one_keeps_its_source_phase_then_expiry_drains_the_residual_counter(int type)
    {
        var store = new RuntimeNpcStore();
        Assert.True(store.TrySpawnVanilla(new(type, (short)type, 1040, 1040, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 1000, LifeMax = 1000, LifeRegenCounter = -1739, Immortal = false }), out var npc));
        var status = new RuntimeNpcBuffStatus1458(store);
        Assert.True(status.TryApply(npc.Handle, TerraRuntime.Contracts.Gameplay.VanillaBuffIds.Venom, 1));
        Assert.True(status.TryPlan(in npc, out var plan));
        Assert.True(plan.Venom); Assert.True(plan.Expired);
        Assert.Equal(-1799, plan.CounterAfter); Assert.Equal(1000, plan.LifeAfter);
        var update = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY,
            npc.Target, npc.Ai, npc.Simulation with { Life = plan.LifeAfter, LifeRegenCounter = plan.CounterAfter });
        Assert.True(store.TryUpdate(npc.Handle, in update, out var accepted));
        Assert.True(status.Commit(in plan, in accepted));
        Span<TerrariaNpcBuffEntryState> buffs = stackalloc TerrariaNpcBuffEntryState[20];
        Assert.True(status.TryCopyWireBuffs(npc.Handle, buffs, out int count)); Assert.Equal(0, count);
        Assert.True(status.TryPlan(in accepted, out var next));
        Assert.False(next.Venom); Assert.Equal(-119, next.CounterAfter); Assert.Equal(986, next.LifeAfter);
        Assert.Equal(14, next.DotDamage);
    }

    [Theory, MemberData(nameof(NpcDeaths))]
    public void Lethal_dart_contacts_complete_existing_joint_death_once(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        using var fixtureRow = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            seed = row.GetProperty("seed").GetInt32(),
            mode = row.GetProperty("mode").GetInt32(),
            remix = false,
            style = row.GetProperty("style").GetInt32(),
            orientation = 1
        }));
        var fixture = new Fixture(fixtureRow.RootElement, 65, 136); int type = row.GetProperty("targetType").GetInt32();
        Assert.True(fixture.Npcs.TrySpawnVanilla(new(type, (short)type, 1040, 1040, 0, 0, 255, default,
            NpcSimulationState.Initial with
            {
                Life = 1,
                LifeMax = 1000,
                DefenseOverride = 0,
                HitboxOverride = new(40, 40),
                MoneyValue = 0,
                ExtraMoneyValue = 0,
                Midas = false,
                Immortal = false
            }), out var npc));
        Assert.True(fixture.Fire(65, 65)); fixture.PrepareCapturedContact(row); fixture.Random.CopyStateFrom(new(row.GetProperty("seed").GetInt32()));
        fixture.Combat.Tick();
        Assert.False(fixture.Npcs.TryGet(npc.Handle, out _));
        Assert.Equal(1, fixture.Combat.Kills); Assert.Equal(1, fixture.Combat.CommittedHits);
        var items = new WorldItemSnapshot[fixture.Items.Capacity];
        int count = fixture.Items.CopyActive(items); Assert.Equal(row.GetProperty("items").GetArrayLength(), count);
        int index = 0; foreach (var expected in row.GetProperty("items").EnumerateArray())
        {
            var item = items[index++]; Assert.Equal(expected.GetProperty("slot").GetInt32(), item.Handle.Slot);
            Assert.Equal(expected.GetProperty("type").GetInt32(), item.ItemNetId);
            Assert.Equal(expected.GetProperty("stack").GetInt32(), item.Stack);
            Assert.Equal(expected.GetProperty("prefix").GetByte(), item.Prefix);
            Assert.Equal(expected.GetProperty("position").GetProperty("X").GetSingle(), item.PositionX);
            Assert.Equal(expected.GetProperty("position").GetProperty("Y").GetSingle(), item.PositionY);
            Assert.Equal(expected.GetProperty("velocity").GetProperty("X").GetSingle(), item.VelocityX);
            Assert.Equal(expected.GetProperty("velocity").GetProperty("Y").GetSingle(), item.VelocityY);
        }
        var rng = row.GetProperty("firstRandom"); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(rng.GetProperty("inext").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(fixture.Random)!);
        Assert.Equal(rng.GetProperty("SeedArray").EnumerateArray().Select(v => v.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(fixture.Random)!);
        var accepted = fixture.Random.Clone(); fixture.Combat.Tick();
        Assert.Equal(1, fixture.Combat.Kills); Assert.True(accepted.HasSameState(fixture.Random));
    }

    private sealed class Fixture
    {
        internal readonly WorldTileStore Tiles = new(new(128, 128));
        internal readonly RuntimeProjectileStore Shots = new(capacity: 1001);
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly WorldTileAuthority Authority;
        internal readonly WorldWiringCircuit1458 Circuit;
        internal readonly ProjectileAuthority Actors;
        internal readonly RuntimeProjectileNpcCombatPass Combat;
        internal readonly RuntimeNpcBuffStatus1458 Status;
        internal Fixture(JsonElement row, int trapX, int origin)
        {
            Random = new(row.GetProperty("seed").GetInt32());
            for (int x = 65; x <= trapX; x++) Tiles.Set(x, 65, new WorldTile { Flags = WorldTileFlags.WireRed });
            if (trapX != 65) Tiles.Set(65, 65, new WorldTile { Type = (ushort)origin, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
            Tiles.Set(trapX, 65, new WorldTile
            {
                Type = 137,
                Flags = WorldTileFlags.Active | WorldTileFlags.WireRed,
                FrameX = (short)(row.GetProperty("orientation").GetInt32() * 18),
                FrameY = (short)(row.GetProperty("style").GetInt32() * 18)
            });
            var state = new ServerRuntimeState(worldTiles: Tiles, projectiles: Shots, npcs: Npcs, worldItems: Items,
                expertMode: row.GetProperty("mode").GetInt32() > 0, masterMode: row.GetProperty("mode").GetInt32() == 2,
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random), naturalSpawnRandom: new SystemVanillaNpcRandom(Random),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { RemixWorld = row.GetProperty("remix").GetBoolean() });
            var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
            Authority = graph.WorldTileAuthority;
            Actors = graph.Projectiles;
            Combat = graph.Npcs.ProjectileNpcCombat;
            Status = (RuntimeNpcBuffStatus1458)typeof(NpcAuthority).GetField("npcBuffStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graph.Npcs)!;
            Circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Authority)!;
        }
        internal void PrepareCapturedContact(JsonElement row)
        {
            if (!row.TryGetProperty("projectile", out var image) || image.GetProperty("type").GetInt32() != 188) return;
            for (int i = 0; i < 6; i++) Assert.True(Actors.TryTickState());
            Assert.True(Shots.TryGetActive(1, out var current));
            Assert.True(Shots.TryGetLifecycle(current.Handle, out var life));
            var pos = image.GetProperty("position"); var vel = image.GetProperty("velocity");
            var update = new ProjectileStateUpdate(current.Type, current.Spawner, pos.GetProperty("X").GetSingle(), pos.GetProperty("Y").GetSingle(),
                vel.GetProperty("X").GetSingle(), vel.GetProperty("Y").GetSingle(), default, 0, current.Damage, current.KnockBack, 0);
            var local = image.GetProperty("localAI");
            life = life with { TimeLeft = image.GetProperty("timeLeft").GetInt32(), LocalAi = new(local[0].GetSingle(), local[1].GetSingle(), local[2].GetSingle()) };
            Assert.True(Shots.TryPrepareSimulation(in current, out var actor)); Assert.NotNull(actor);
            Assert.True(actor.TryStageMotion(in update, in life)); Assert.True(actor.TryAdoptUnpublished(out _, out _));
        }
        internal bool Fire(int x, int y) => (bool)typeof(WorldTileAuthority).GetMethod("TryFireWiringTrap", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Authority, [x, y])!;
    }

    [Theory, MemberData(nameof(NpcHits))]
    public void Dart_actual_npc_contacts_match_original_damage_immunity_status_and_entropy(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        // The source damage oracle uses a right-facing dart and a retained
        // nonlethal physical NPC preimage; no player owns this source.
        using var fixtureRow = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            seed = row.GetProperty("seed").GetInt32(),
            mode = row.GetProperty("mode").GetInt32(),
            remix = false,
            style = row.GetProperty("style").GetInt32(),
            orientation = 1
        }));
        var fixture = new Fixture(fixtureRow.RootElement, 65, 136); int type = row.GetProperty("targetType").GetInt32();
        Assert.True(fixture.Npcs.TrySpawnVanilla(new(type, (short)type, 1040, 1040, 0, 0, 255, default,
            NpcSimulationState.Initial with
            {
                Life = 1000,
                LifeMax = 1000,
                DefenseOverride = 0,
                HitboxOverride = new(40, 40),
                MoneyValue = 0,
                ExtraMoneyValue = 0,
                Midas = false,
                Immortal = false
            }), out var npc));
        Assert.True(fixture.Fire(65, 65)); fixture.PrepareCapturedContact(row); fixture.Random.CopyStateFrom(new(row.GetProperty("seed").GetInt32()));
        fixture.Combat.Tick();
        Assert.True(fixture.Npcs.TryGet(npc.Handle, out var after)); var expected = row.GetProperty("first");
        Assert.Equal(expected.GetProperty("life").GetInt32(), after.Simulation.Life);
        Assert.Equal(expected.GetProperty("vx").GetSingle(), after.VelocityX);
        Assert.Equal(expected.GetProperty("vy").GetSingle(), after.VelocityY);
        Span<TerrariaNpcBuffEntryState> buffs = stackalloc TerrariaNpcBuffEntryState[20];
        Assert.True(fixture.Status.TryCopyWireBuffs(npc.Handle, buffs, out int count));
        var types = expected.GetProperty("buffType").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var times = expected.GetProperty("buffTime").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        Assert.Equal(types.Count(t => t != 0), count);
        int next = 0; for (int i = 0; i < types.Length; i++) if (types[i] != 0)
        { Assert.Equal(types[i], buffs[next].BuffType); Assert.Equal(times[i], buffs[next++].Duration); }
        var rng = row.GetProperty("firstRandom"); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(rng.GetProperty("inext").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(fixture.Random)!);
        Assert.Equal(rng.GetProperty("SeedArray").EnumerateArray().Select(v => v.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(fixture.Random)!);
        var accepted = fixture.Random.Clone(); fixture.Combat.Tick();
        Assert.True(fixture.Npcs.TryGet(npc.Handle, out var duplicate)); Assert.Equal(after, duplicate);
        Assert.True(accepted.HasSameState(fixture.Random)); Assert.Equal(1, fixture.Combat.CommittedHits);
    }
}
