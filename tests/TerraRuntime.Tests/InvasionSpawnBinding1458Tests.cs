using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Application;
using TerraRuntime.World;
namespace TerraRuntime.Tests;
public sealed class InvasionSpawnBinding1458Tests
{
    [Fact]
    public void Actual_source_68_profiles_match_eligibility_and_selected_Goblin_cursor()
    {
        using var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("InvasionSpawnSelection1458")!;
        using var unpacked=new GZipStream(source,CompressionMode.Decompress);
        using var data=JsonDocument.Parse(unpacked);
        int eligibility=0,selection=0;
        foreach(var row in data.RootElement.GetProperty("rows").EnumerateArray())
        {
            var random=new SystemVanillaNpcRandom(row.GetProperty("seed").GetInt32());
            if(row.GetProperty("phase").GetString()=="SpawnAnNPC-Goblin")
            {
                Assert.Equal(row.GetProperty("type").GetInt32(),RuntimeInvasionSpawn1458.SelectGoblin(
                    row.GetProperty("hard").GetBoolean(),row.GetProperty("summoner").GetBoolean(),random).Value);
                selection++;
            }
            else
            {
                var c=row.GetProperty("context");
                var invasion=default(InvasionState1458) with { Type=c.GetProperty("invasionType").GetInt32(),
                    Delay=c.GetProperty("invasionDelay").GetInt32(),Size=c.GetProperty("invasionSize").GetInt32(),X=c.GetProperty("invasionX").GetDouble() };
                var slots=new RetainedInvasionTownSlot1458[200];
                Array.Fill(slots,new(false,false,null));
                if(c.GetProperty("town").GetBoolean())slots[0]=new(c.GetProperty("townActive").GetBoolean(),true,c.GetProperty("townCenterX").GetSingle());
                Assert.True(RuntimeInvasionSpawn1458.TryShouldSpawn(in invasion,c.GetProperty("playerX").GetSingle(),
                    c.GetProperty("playerY").GetSingle(),40,c.GetProperty("spawnY").GetInt32(),100,slots,random,out bool actual));
                Assert.Equal(row.GetProperty("result").GetBoolean(),actual);eligibility++;
            }
            Assert.Equal(row.GetProperty("next").GetInt32(),random.NextInt32(0,int.MaxValue));
        }
        Assert.Equal(28,eligibility);Assert.Equal(40,selection);
    }
    [Fact]
    public void Retained_town_first_zero_break_and_unknown_facts_are_not_an_active_census()
    {
        var invasion=default(InvasionState1458) with { Type=1, Size=80, SizeStart=80, X=50 };
        var random=new SystemVanillaNpcRandom(1);
        var slots=new RetainedInvasionTownSlot1458[200];
        Array.Fill(slots,new(false,false,null));
        slots[0]=new(false,true,5000);slots[1]=new(true,true,5000);
        Assert.True(RuntimeInvasionSpawn1458.TryShouldSpawn(in invasion,5000,100,40,30,100,slots,random,out bool actual));
        Assert.False(actual); // First source Next3 zero exits even though another matching resident remains.
        Assert.Equal(237820880,random.NextInt32(0,int.MaxValue));
        random=new(1);
        Assert.False(RuntimeInvasionSpawn1458.TryShouldSpawn(in invasion,5000,100,40,30,100,[],random,out _));
        Assert.Equal(new SystemVanillaNpcRandom(1).NextInt32(0,int.MaxValue),random.NextInt32(0,int.MaxValue));
        random=new(1);slots[0]=new(false,null,null);
        Assert.False(RuntimeInvasionSpawn1458.TryShouldSpawn(in invasion,5000,100,40,30,100,slots,random,out _));
        Assert.Equal(new SystemVanillaNpcRandom(1).NextInt32(0,int.MaxValue),random.NextInt32(0,int.MaxValue));
    }
    [Fact]
    public void Source_Goblin_daytime_and_Town_move_in_read_current_invasion_owner()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("InvasionDayFighterContext1458")!;
        using var unpacked=new GZipStream(stream,CompressionMode.Decompress);
        using var original=JsonDocument.Parse(unpacked);
        Assert.Equal(8,original.RootElement.GetProperty("rows").GetArrayLength());
        foreach(var row in original.RootElement.GetProperty("rows").EnumerateArray())
        {
            Assert.Equal(row.GetProperty("invasion").GetInt32()==1,row.GetProperty("notDiscouraged").GetBoolean());
            Assert.False(row.GetProperty("sourceGraveyard").GetBoolean());
            Assert.Equal((int)byte.MaxValue,row.GetProperty("target").GetInt32());
            Assert.Equal(new SystemVanillaNpcRandom(0).NextInt32(0,int.MaxValue),row.GetProperty("next").GetInt32());
        }
        foreach(int type in new[]{26,27,28,111})
        {
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type),out var definition));
            var stepper=new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
            stepper.EnableZombieMotion(100);stepper.SetWorldConditions(true,false);
            stepper.SetCandidates([new(7,220,100,0,true,false,false,false)]);
            var npc=new NpcSnapshot(new(1,new(1)),new(1),type,(short)type,100,80,0,0,255,default,
                NpcSimulationState.Initial with {Life=definition.LifeMax,LifeMax=definition.LifeMax,
                    DirectionX=1,DirectionY=1,OldPositionX=99,OldPositionY=80,TimeLeft=750});
            stepper.SetInvasionType(0);
            Assert.True(stepper.TryStepState(in npc,out var ordinary),$"ordinary type={type}");
            Assert.Equal(10,ordinary.Simulation.TimeLeft);Assert.Equal((ushort)255,ordinary.Target);
            stepper.SetInvasionType(1);
            Assert.True(stepper.TryStepState(in npc,out var invasion),$"invasion type={type}");
            Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft,invasion.Simulation.TimeLeft);
            Assert.Equal((ushort)7,invasion.Target);
            stepper.SetInvasionType(0);
            Assert.True(stepper.TryStepState(in npc,out var ended));Assert.Equal(ordinary,ended);
            stepper.SetInvasionType(null);
            Assert.False(stepper.TryStepState(in npc,out _));
        }
        AssertTownMoveInUsesCurrentOwner();
    }

    private static void AssertTownMoveInUsesCurrentOwner()
    {
        var tiles = new WorldTileStore(new WorldDimensions(120, 100));
        for (int x = 20; x <= 31; x++)
            for (int y = 20; y <= 29; y++)
            {
                bool wall = x is 20 or 31 || y is 20 or 29;
                tiles.Set(x, y, new WorldTile { Type = wall ? (ushort)1 : (ushort)0, Wall = 1,
                    Flags = wall ? WorldTileFlags.Active : WorldTileFlags.None });
            }
        foreach (var furniture in new[] { (22, 26, 15), (24, 26, 14), (26, 23, 4), (28, 25, 10) })
            tiles.Set(furniture.Item1, furniture.Item2, new WorldTile { Type = (ushort)furniture.Item3,
                Wall = 1, Flags = WorldTileFlags.Active });

        var npcs = new RuntimeNpcStore();
        var town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [], []), [], tiles.Dimensions);
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        var invasion = new RuntimeWorldInvasion1458(default(InvasionState1458));
        var authority = new TownNpcAuthority(new PlayerAuthority(null, tiles), npcs, new RuntimeProjectileStore(),
            tiles, new RuntimeWorldProgressionMutations(), town, default(VanillaTownSpawnWorldFacts1458),
            null, null, null, false, false, invasion, false, false);
        Assert.True(invasion.TryCapture(out var before));
        var start = new InvasionTransition1458(before.State with { Type = 1, Size = 80, SizeStart = 80 }, default);
        Assert.True(invasion.TryAdopt(in before, in start, out var started));
        for (int tick = 0; tick < 7200; tick++) authority.TickLifecycle(null);
        Assert.Equal(0, npcs.ActiveCount);
        var clear = new InvasionTransition1458(started.State with { Type = 0, Size = 0 }, default);
        Assert.True(invasion.TryAdopt(in started, in clear, out _));
        for (int tick = 0; tick < 7200; tick++) authority.TickLifecycle(null);
        Assert.Equal(1, npcs.ActiveCount);
        Assert.True(npcs.TryGetActive(0, out var resident));
        Assert.Equal(VanillaNpcIds.Guide, resident.TypeIdentity);
    }
}
