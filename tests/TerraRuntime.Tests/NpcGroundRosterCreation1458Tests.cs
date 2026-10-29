using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class NpcGroundRosterCreation1458Tests
{
    // Independent original TerrariaServer 1.4.5.8 SetDefaults captures, including genuine
    // x86 Windows arithmetic. Expectations never come from the runtime catalog.
    private static JsonDocument ReadSource()
    {
        using var stream = typeof(NpcGroundRosterCreation1458Tests).Assembly
            .GetManifestResourceStream(OperatingSystem.IsWindows() ? "GroundRosterWindows1458" : "GroundRosterLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    [Fact]
    public void Both_existing_creation_paths_materialize_the_complete_source_ground_roster()
    {
        using var source = ReadSource();
        int positiveFighters = 0, signedFighters = 0, townsAndCritters = 0;
        foreach (var row in source.RootElement.GetProperty("classic").EnumerateArray())
        {
            int style = Number(row, "aiStyle");
            if (style is not (3 or 7)) continue;
            int requested = Number(row, "requested"), type = Number(row, "type");
            if (style == 7) townsAndCritters++;
            else if (requested < 0) signedFighters++;
            else positiveFighters++;

            // Each fresh store exercises real allocator/normalizer/publication, including signed IDs.
            var sink = new Capture();
            var store = new RuntimeNpcStore(commitSink: sink);
            var update = new NpcStateUpdate(type, checked((short)requested), 1000f, 900f,
                0f, 0f, 255, default, NpcSimulationState.Initial);
            Assert.True(store.TrySpawnVanilla(in update, out var direct), $"Direct {requested}");
            Assert.Equal(direct, sink.Last);
            AssertClassic(row, direct);
            Assert.Equal(1000f, direct.PositionX);
            Assert.Equal(900f, direct.PositionY);

            var intent = new NpcAiSpawnIntent(new(type), 1000, 1000, 0f, 0f, 255)
                { NetIdOverride = new NpcNetId(checked((short)requested)) };
            Assert.True(store.TrySpawnIntent(in intent, out var child), $"Intent {requested}");
            Assert.Equal(child, sink.Last);
            AssertClassic(row, child);
            Assert.Equal(1000f - Number(row, "width") * .5f, child.PositionX);
            Assert.Equal(1000f - Number(row, "height"), child.PositionY);
            Assert.Equal(VanillaNpcDefinitionCatalog.NewNpcTimeLeft, child.Simulation.TimeLeft);
            Assert.Equal(2, sink.Count);
            Assert.Equal(2, store.ActiveCount);
        }
        Assert.Equal(191, positiveFighters);
        Assert.Equal(27, signedFighters);
        Assert.Equal(82, townsAndCritters);
    }

    [Fact]
    public void Source_world_and_difficulty_context_reaches_real_births_before_publication()
    {
        using var source = ReadSource();
        // Ordinary fighter/hardmode upgrade, signed truncation/override, Remix181,
        // Pumpkin/Frost Moon multiplayer, Martian471 balance, scaled631 and non-scaling AI007.
        int[] selected = [3, -26, -13, -15, 181, 305, 471, 631, 687];
        var types = source.RootElement.GetProperty("classic").EnumerateArray()
            .ToDictionary(row => Number(row, "requested"), row => Number(row, "type"));
        int cases = 0;
        foreach (var row in source.RootElement.GetProperty("contexts").EnumerateArray())
        {
            int requested = row[0].GetInt32();
            if (!selected.Contains(requested)) continue;
            int mask = row[1].GetInt32();
            var context = new VanillaNpcSpawnContext(row[3].GetSingle(), row[2].GetInt32(), (mask & 2) != 0)
            {
                HardMode = (mask & 1) != 0, TenthAnniversaryWorld = (mask & 4) != 0,
                RemixWorld = (mask & 8) != 0, DownedPlantera = (mask & 16) != 0
            };
            var sink = new Capture();
            var random = new ZeroRandom();
            var store = new RuntimeNpcStore(commitSink: sink);
            int samples = 0;
            store.SetVanillaSpawnContextSource(() => { samples++; return context; });
            store.SetVanillaSpawnRandomSource(random);
            var intent = new NpcAiSpawnIntent(new(types[requested]), 1000, 1000, 0f, 0f, 255)
                { NetIdOverride = new NpcNetId(checked((short)requested)) };
            Assert.True(store.TrySpawnIntent(in intent, out var npc), $"id={requested}, mask={mask}, difficulty={context.Difficulty}");
            Assert.Equal(1, samples);
            Assert.Equal(context.GoodWorld ? 1 : 0, random.Draws);
            Assert.Equal(1, sink.Count);
            Assert.Equal(npc, sink.Last);
            Assert.Equal(1000f - row[4].GetInt32() * .5f, npc.PositionX);
            Assert.Equal(1000f - row[5].GetInt32(), npc.PositionY);
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition));
            Assert.True(definition.TryResolveHitbox(npc.Simulation, out var body));
            Assert.Equal(row[4].GetInt32(), body.Width);
            Assert.Equal(row[5].GetInt32(), body.Height);
            Assert.Equal(row[6].GetSingle(), npc.Simulation.Scale);
            Assert.Equal(row[7].GetInt32(), npc.Simulation.Life);
            Assert.Equal(row[7].GetInt32(), npc.Simulation.LifeMax);
            Assert.Equal(row[7].GetInt32(), npc.Simulation.BaseLifeMax);
            Assert.Equal(row[8].GetInt32(), npc.Simulation.BaseDamage);
            Assert.Equal(row[9].GetInt32(), npc.Simulation.BaseDefense);
            Assert.Equal(row[8].GetInt32(), npc.Simulation.DamageOverride ?? definition.Damage);
            Assert.Equal(row[9].GetInt32(), npc.Simulation.DefenseOverride ?? definition.Defense);
            Assert.Equal(row[10].GetSingle(), npc.Simulation.KnockBackResist);
            Assert.Equal(row[11].GetSingle(), npc.Simulation.MoneyValue);
            Assert.Equal(row[12].GetInt32(), npc.Simulation.Alpha);
            Assert.Equal(row[13].GetBoolean(), npc.Simulation.NoGravity);
            Assert.Equal(row[14].GetBoolean(), npc.Simulation.NoTileCollide);
            Assert.Equal(row[15].GetBoolean(), npc.Simulation.DontTakeDamage);
            Assert.Equal(row[16].GetBoolean(), npc.Simulation.Hidden);
            Assert.Equal(row[17].GetSingle(), npc.Simulation.SpawnDifficulty);
            cases++;
        }
        Assert.Equal(4608, cases);
    }

    [Fact]
    public void Definition_materialization_does_not_grant_unverified_autonomous_ai_or_full_parity()
    {
        // Source identities still fenced after the ground-combat cohort: Martian Officer,
        // Medusa, Fish230, Old Man, Travelling Merchant and Skeleton Merchant. Penguin148
        // now has the separately composed nonresident ground-critter lifecycle.
        foreach (int type in new[] { 383, 480, 230, 37, 368, 453 })
        {
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), out var definition));
            Assert.False(definition.DefinitionOnly);
            Assert.Equal(VanillaNpcBehaviorFamily.None, definition.BehaviorFamily);
            Assert.Equal(VanillaNpcPhysicsFamily.None, definition.PhysicsFamily);
            Assert.True(!VanillaNpcAiCoverageCatalog.TryGet(new(type), out var coverage) ||
                !coverage.FullVanillaAiParity);
            var store = new RuntimeNpcStore();
            Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(new(type), 1000, 1000, 0f, 0f, 255), out var npc));
            Assert.True(npc.Simulation.LifeMax > 0);
            Assert.True(definition.TryResolveHitbox(npc.Simulation, out var body));
            Assert.True(body.IsValid);
            var dispatcher = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
            Assert.False(dispatcher.TryStepState(in npc, out _));
        }
    }

    [Fact]
    public void Newly_materialized_definitions_keep_unproved_runtime_lifecycle_fenced()
    {
        // Independent starting-main census, minus the separately verified cohorts.
        int[] promoted = [44,104,140,181,185,196,197,198,201,202,203,236,379,380,381,382,383,
            385,386,389,391,409,411,415,419,424,425,426,427,428,429,449,450,451,452,464,470,
            471,480,481,482,489,508,518,520,524,525,526,527,528,529,530,532,534,536,580,582,
            586,590,631,632,635,691,37,148,149,230,299,300,303,337,361,362,364,366,367,368,
            443,445,447,453,538,539,540,593,602,608,610,616,617,625,639,640,641,642,643,644,
            645,646,647,648,649,650,651,652,687,-13,-14,-15,-26,-27,-28,-29,-30,-31,-32,-33,
            -34,-35,-36,-37,-44,-45,-46,-47,-48,-49,-50,-51,-52,-53,-54,-55];
        using var source = ReadSource();
        int fenced = 0;
        foreach (var row in source.RootElement.GetProperty("classic").EnumerateArray())
        {
            int net = Number(row, "requested"), type = Number(row, "type");
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), new((short)net), out var definition));
            bool undeadRuntime = net is 201 or 202 or 203 or 635 or -13 or -14 or
                >= -37 and <= -26 or >= -55 and <= -44;
            bool nonresidentGroundCritterRuntime = net is 46 or 148 or 149 or 299 or 300 or 303 or 337 or
                361 or 366 or 367 or 443 or 445 or 447 or 538 or 539 or 540 or 610 or 616 or 617 or 625 or
                639 or 640 or 641 or 642 or 643 or 644 or 645 or 646 or 647 or 648 or 649 or 650 or 651 or 652;
            bool ordinaryMeleeRuntime = net is 44 or 104 or 140 or 181 or 196 or 197 or 464 or 470 or 489 or
                524 or 525 or 526 or 527 or 528 or 529 or 536 or 691;
            bool groundCombatRuntime = net is -15 or 185 or 385 or 482 or 508 or 580 or 582 or 590 or
                381 or 382 or 449 or 450 or 451 or 452 or 481 or 520;
            bool expected = promoted.Contains(net) && !undeadRuntime && !nonresidentGroundCritterRuntime && !ordinaryMeleeRuntime && !groundCombatRuntime;
            Assert.Equal(expected, definition.CreationDefaultsOnly);
            Assert.Equal(!expected, definition.SupportsRuntimeLifecycle);
            if (!expected) continue;
            fenced++;
            var store = new RuntimeNpcStore(2);
            var update = new NpcStateUpdate(type, (short)net, 1000, 900, 0, 0, 255, default,
                NpcSimulationState.Initial);
            Assert.True(store.TrySpawnVanilla(in update, out var born));
            AssertClassic(row, born);
            var status = new RuntimeNpcBuffStatus1458(store);
            Assert.False(status.EnsureGeneration(born.Handle));
            Assert.False(status.TryApply(born.Handle, 60));
            Assert.False(new RuntimeVanillaNpcRoleBoundary(store).TryClassify(born.Handle, out _));
            var stepper = new CountingLifecycle();
            Assert.Equal(0, new RuntimeNpcAiStateExecutor(store).Tick(stepper).Applied);
            Assert.Equal(0, stepper.Calls);
            var request = new NpcDamageRequest(born.Handle, DamageSource.Environment, 100000);
            Assert.False(new RuntimeNpcDamageExecutor(store).TryApply(in request, out _));
            Assert.True(store.TryGet(born.Handle, out var retained));
            Assert.Equal(born, retained);
            var dead = update with { Simulation = born.Simulation with { Life = 0 } };
            Assert.True(store.TryUpdate(born.Handle, in dead, out var retainedDead));
            Assert.False(new RuntimeNpcDeathLifecycleFinalizer(store)
                .TryFinalizeWhenLootUnsupported(retainedDead.Handle, out _));
            Assert.True(store.TryGet(born.Handle, out retained));
            Assert.Equal(retainedDead, retained);
        }
        Assert.Equal(37, fenced);
    }

    private sealed class CountingLifecycle : INpcAiStateStepper, INpcAiPrepass1458
    {
        public int Calls;
        public bool TryRunPrepass(in NpcSnapshot npc, out bool consumed)
        { Calls++; consumed = false; return true; }
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        { Calls++; next = default; return false; }
    }

    private static void AssertClassic(JsonElement row, in NpcSnapshot npc)
    {
        int type = Number(row, "type"), requested = Number(row, "requested");
        Assert.Equal(type, npc.Type);
        Assert.Equal(requested, npc.NetId);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), new((short)requested), out var definition));
        Assert.True(definition.TryResolveHitbox(npc.Simulation, out var body));
        Assert.Equal(Number(row, "width"), body.Width);
        Assert.Equal(Number(row, "height"), body.Height);
        Assert.Equal(Number(row, "lifeMax"), npc.Simulation.LifeMax);
        Assert.Equal(Number(row, "lifeMax"), npc.Simulation.Life);
        Assert.Equal(Number(row, "lifeMax"), npc.Simulation.BaseLifeMax);
        Assert.Equal(Number(row, "damage"), npc.Simulation.BaseDamage);
        Assert.Equal(Number(row, "defense"), npc.Simulation.BaseDefense);
        Assert.Equal(Number(row, "damage"), npc.Simulation.DamageOverride ?? definition.Damage);
        Assert.Equal(Number(row, "defense"), npc.Simulation.DefenseOverride ?? definition.Defense);
        Assert.Equal(row.GetProperty("scale").GetSingle(), npc.Simulation.Scale);
        Assert.Equal(row.GetProperty("knockBackResist").GetSingle(), npc.Simulation.KnockBackResist);
        Assert.Equal(row.GetProperty("value").GetSingle(), npc.Simulation.MoneyValue);
        Assert.Equal(Number(row, "alpha"), npc.Simulation.Alpha);
        Assert.Equal(row.GetProperty("town").GetBoolean(), npc.Simulation.TownNpc);
        Assert.Equal(row.GetProperty("friendly").GetBoolean(), npc.Simulation.Friendly);
        Assert.Equal(row.GetProperty("chaseable").GetBoolean(), npc.Simulation.Chaseable);
        Assert.Equal(row.GetProperty("immortal").GetBoolean(), npc.Simulation.Immortal);
        Assert.Equal(row.GetProperty("noGravity").GetBoolean(), npc.Simulation.NoGravity);
        Assert.Equal(row.GetProperty("noTileCollide").GetBoolean(), npc.Simulation.NoTileCollide);
        Assert.Equal(row.GetProperty("dontTakeDamage").GetBoolean(), npc.Simulation.DontTakeDamage);
        Assert.Equal(row.GetProperty("hidden").GetBoolean(), npc.Simulation.Hidden);
        Assert.True(!VanillaNpcAiCoverageCatalog.TryGet(new(type), out var coverage) ||
            !coverage.FullVanillaAiParity);
    }

    private static int Number(JsonElement row, string name) => row.GetProperty(name).GetInt32();

    private sealed class Capture : INpcStateCommitSink
    {
        public NpcSnapshot Last { get; private set; }
        public int Count { get; private set; }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
        {
            Assert.Equal(NpcStateCommitKind.Spawn, kind);
            Last = snapshot;
            Count++;
        }
    }

    private sealed class ZeroRandom : IVanillaNpcRandom
    {
        public int Draws { get; private set; }
        public int NextInt32(int minimum, int maximum)
        {
            Assert.Equal(0, minimum);
            Assert.Equal(3, maximum);
            Draws++;
            return 0;
        }
    }
}
