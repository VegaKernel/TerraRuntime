using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Application;
using TerraRuntime.World;
using TerraRuntime.Gameplay.Npcs;
using Xunit;

namespace TerraRuntime.Tests.NpcRemaining1458;

public sealed class NpcRemainingDefinitions1458Tests
{
    [Fact]
    public void Remaining_original_definitions_materialize_exact_contexts_without_runtime_AI_admission()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            bool windows = platform == "Windows";
            using var stream = typeof(NpcRemainingDefinitions1458Tests).Assembly.GetManifestResourceStream("NpcRemaining" + platform + "1458.json.gz")!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var doc = JsonDocument.Parse(gzip);
            int classic = 0;
            foreach (var row in doc.RootElement.GetProperty("classic").EnumerateArray())
            {
                int type = row.GetProperty("type").GetInt32(), net = row.GetProperty("requested").GetInt32();
                Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), new((short)net), out var definition));
                if (type == 664)
                {
                    Assert.True(definition.DefinitionOnly); Assert.False(definition.TryResolveHitbox(1f, out _));
                    continue;
                }
                bool retainedChild = type is 384 or 387 or 390 or 606;
                Assert.False(definition.DefinitionOnly); Assert.Equal(!retainedChild, definition.CreationDefaultsOnly);
                Assert.Equal(retainedChild, definition.SupportsRuntimeLifecycle);
                Assert.Equal(VanillaNpcBehaviorFamily.None, definition.BehaviorFamily);
                Assert.Equal(retainedChild ? VanillaNpcPhysicsFamily.GenericGround : VanillaNpcPhysicsFamily.None,
                    definition.PhysicsFamily);
                if (windows != OperatingSystem.IsWindows()) continue;
                var store = new RuntimeNpcStore(4);
                var update = new NpcStateUpdate(type, (short)net, 1000, 1000, 0, 0, 255, default, NpcSimulationState.Initial);
                Assert.True(store.TrySpawnVanilla(in update, out var born), $"classic {net}");
                Assert.Equal(default(NpcAiState), born.Ai);
                Assert.Equal(row.GetProperty("lifeMax").GetInt32(), born.Simulation.LifeMax);
                Assert.Equal(row.GetProperty("damage").GetInt32(), born.Simulation.BaseDamage);
                Assert.Equal(row.GetProperty("defense").GetInt32(), born.Simulation.BaseDefense);
                Assert.Equal(row.GetProperty("value").GetSingle(), born.Simulation.MoneyValue);
                Assert.Equal(row.GetProperty("knockBackResist").GetSingle(), born.Simulation.KnockBackResist);
                Assert.Equal(row.GetProperty("scale").GetSingle(), born.Simulation.Scale);
                Assert.Equal(row.GetProperty("alpha").GetInt32(), born.Simulation.Alpha);
                Assert.Equal(row.GetProperty("friendly").GetBoolean(), born.Simulation.Friendly);
                Assert.Equal(row.GetProperty("immortal").GetBoolean(), born.Simulation.Immortal);
                Assert.Equal(row.GetProperty("chaseable").GetBoolean(), born.Simulation.Chaseable);
                Assert.Equal(row.GetProperty("width").GetInt32(), definition.Width);
                Assert.Equal(row.GetProperty("height").GetInt32(), definition.Height);
                Assert.Equal(row.GetProperty("dontTakeDamage").GetBoolean(), born.Simulation.DontTakeDamage);
                Assert.Equal(row.GetProperty("noGravity").GetBoolean(), born.Simulation.NoGravity);
                Assert.Equal(row.GetProperty("noTileCollide").GetBoolean(), born.Simulation.NoTileCollide);
                Assert.Equal(row.GetProperty("hidden").GetBoolean(), born.Simulation.Hidden);
                Assert.Equal(row.GetProperty("town").GetBoolean(), born.Simulation.TownNpc);
                Assert.True(!VanillaNpcAiCoverageCatalog.TryGet(new(type), out var coverage) || !coverage.FullVanillaAiParity);
                if (!retainedChild)
                {
                    var stepper = new CountingLifecycle();
                    Assert.Equal(0, new RuntimeNpcAiStateExecutor(store).Tick(stepper).Applied); Assert.Equal(0, stepper.Calls);
                    var damage = new NpcDamageRequest(born.Handle, DamageSource.Environment, 100000);
                    Assert.False(new RuntimeNpcDamageExecutor(store).TryApply(in damage, out _));
                    Assert.True(store.TryGet(born.Handle, out var retained)); Assert.Equal(born, retained);
                }
                var intentStore = new RuntimeNpcStore(4);
                var intent = new NpcAiSpawnIntent(new(type), 1000, 1000, 0f, 0f, 255)
                    { NetIdOverride = new((short)net) };
                Assert.True(intentStore.TrySpawnIntent(in intent, out var intentBorn));
                Assert.Equal(born.Simulation.LifeMax, intentBorn.Simulation.LifeMax);
                Assert.Equal(1000f - definition.Width * .5f, intentBorn.PositionX);
                Assert.Equal(1000f - definition.Height, intentBorn.PositionY);
                classic++;
            }
            Assert.Equal(windows == OperatingSystem.IsWindows() ? 191 : 0, classic);
            int contexts = 0;
            foreach (var row in doc.RootElement.GetProperty("facts").EnumerateArray())
            {
                int net = row[0].GetInt32(), type = net < 0 ? 183 : net, mask = row[1].GetInt32();
                if (type == 664) continue;
                Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), new((short)net), out var definition));
                var context = new VanillaNpcSpawnContext(row[3].GetSingle(), row[2].GetInt32(), (mask & 2) != 0)
                {
                    HardMode = (mask & 1) != 0, TenthAnniversaryWorld = (mask & 4) != 0,
                    RemixWorld = (mask & 8) != 0, DownedPlantera = (mask & 16) != 0,
                    WorldDifficulty = (mask & 2) != 0 ? 2f : 1f,
                    DownedAnyMechanicalBoss = false, DownedGolem = false
                };
                Assert.True(VanillaNpcSpawnDefaults.TryResolve(in definition, in context, windows, out var actual), $"resolve {net}/{mask}/{row[3]}");
                Assert.True(actual.Hitbox.Width == row[4].GetInt32() && actual.Hitbox.Height == row[5].GetInt32() &&
                    Bits(actual.Scale) == Bits(row[6].GetSingle()) && actual.LifeMax == row[7].GetInt32() &&
                    actual.Damage == row[8].GetInt32() && actual.Defense == row[9].GetInt32() &&
                    Bits(actual.KnockBackResist!.Value) == Bits(row[10].GetSingle()) &&
                    Bits(actual.VerifiedMoneyValue!.Value) == Bits(row[11].GetSingle()) &&
                    actual.Difficulty == row[17].GetSingle(),
                    $"{platform} net{net} mask{mask} players{row[2]} strength{row[3]} expected={row} actual={actual}");
                contexts++;
            }
            Assert.Equal(97792, contexts);
            foreach (var row in doc.RootElement.GetProperty("conditional").GetProperty("rows").EnumerateArray())
            {
                int mask = row.GetProperty("mask").GetInt32();
                Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(548), out var definition));
                var context = new VanillaNpcSpawnContext(1f, 1, (mask & 1) != 0) { HardMode = (mask & 2) != 0,
                    WorldDifficulty = row.GetProperty("worldDifficulty").GetSingle(),
                    DownedAnyMechanicalBoss = (mask & 4) != 0, DownedGolem = (mask & 8) != 0 };
                Assert.True(VanillaNpcSpawnDefaults.TryResolve(in definition, in context, windows, out var actual));
                Assert.Equal(row.GetProperty("life").GetInt32(), actual.LifeMax);
                Assert.Equal(row.GetProperty("defense").GetInt32(), actual.Defense);
                if(windows == OperatingSystem.IsWindows())
                {
                    int mode = row.GetProperty("mode").GetInt32();
                    var worldMetadata = new WorldFileRuntimeMetadata { GameMode = (byte)mode, HardMode = (mask & 2) != 0,
                        DownedMechBossAny = (mask & 4) != 0, DownedGolemBoss = (mask & 8) != 0,
                        GetGoodWorld = (mask & 1) != 0 };
                    var store = new RuntimeNpcStore(4);
                    var clock = new RuntimeWorldClock(0,true,(VanillaMoonPhase)0,0,1,getGoodWorld:worldMetadata.GetGoodWorld);
                    _ = new ServerRuntimeState(npcs:store,worldClock:clock,
                        townCommerceWorldFacts: RuntimeTownCommerceWorldFacts1458.FromMetadata(worldMetadata),
                        expertMode:mode>=1,masterMode:mode==2);
                    var update = new NpcStateUpdate(548,548,1000,1000,0,0,255,default,NpcSimulationState.Initial);
                    Assert.True(store.TrySpawnVanilla(in update,out var born));
                    Assert.Equal(actual.LifeMax,born.Simulation.LifeMax); Assert.Equal(actual.Defense,born.Simulation.BaseDefense);
                }
            }
        }
        var unknownWorldStore = new RuntimeNpcStore(2);
        _ = new ServerRuntimeState(npcs:unknownWorldStore);
        var unknownCrystal = new NpcStateUpdate(548,548,1000,1000,0,0,255,default,NpcSimulationState.Initial);
        Assert.False(unknownWorldStore.TrySpawnVanilla(in unknownCrystal,out _));
        Assert.Equal(0,unknownWorldStore.ActiveCount);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(548), out var conditional));
        var unknown = new VanillaNpcSpawnContext(2f, 1, true) { HardMode = true };
        Assert.False(VanillaNpcSpawnDefaults.TryResolve(in conditional, in unknown, out _));
        var refused = new RuntimeNpcStore(2); refused.SetVanillaSpawnContextSource(() => unknown);
        var rng = new CountingRandom(); refused.SetVanillaSpawnRandomSource(rng);
        var denied = new NpcStateUpdate(548,548,1000,1000,0,0,255,default,NpcSimulationState.Initial);
        Assert.False(refused.TrySpawnVanilla(in denied, out _)); Assert.Equal(0,refused.ActiveCount); Assert.Equal(0,rng.Draws);
        int created = 0, metadata = 0, lifecycle = 0;
        for (int type=1;type<=696;type++)
            if (VanillaNpcDefinitionCatalog.TryGet(new(type),out var d))
            {
                if(d.DefinitionOnly)metadata++;else created++;
                if(d.SupportsRuntimeLifecycle)lifecycle++;
            }
        Assert.Equal(690,created);Assert.Equal(1,metadata);
        // Previous431, the verified37 undead/gem cohort, four bunny siblings,
        // seventeen ordinary melee actors, twenty-two nonresident ground critters
        // sixteen further ground-combat identities and seven transformation-dependent identities.
        Assert.Equal(546, lifecycle + CountSignedLifecycle());
        int signed=0;
        for(short net=-65;net<0;net++) for(int type=1;type<=696;type++)
            if(VanillaNpcDefinitionCatalog.TryGet(new(type),new(net),out var d) && !d.DefinitionOnly) signed++;
        Assert.Equal(65,signed);
    }
    private sealed class CountingRandom : IVanillaNpcRandom
    {
        public int Draws;
        public int NextInt32(int minimum,int maximum) { Draws++; return minimum; }
    }
    private sealed class CountingLifecycle : INpcAiStateStepper, INpcAiPrepass1458
    {
        public int Calls;
        public bool TryRunPrepass(in NpcSnapshot npc,out bool consumed) { Calls++; consumed=false;return true; }
        public bool TryStepState(in NpcSnapshot npc,out NpcStateUpdate next) { Calls++;next=default;return false; }
    }
    private static int Bits(float x) => BitConverter.SingleToInt32Bits(x);
    private static int CountSignedLifecycle()
    {
        int count=0;
        for(short net=-65;net<0;net++)for(int type=1;type<=696;type++)
            if(VanillaNpcDefinitionCatalog.TryGet(new(type),new(net),out var d)&&d.SupportsRuntimeLifecycle)count++;
        return count;
    }
}
