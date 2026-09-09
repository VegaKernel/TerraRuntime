using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class FallingBlockRuntimeTests(ITestOutputHelper output)
{
    [Fact]
    public void Detachment_uses_ClearTile_and_preserves_source_cell_paint_actuator_and_wires()
    {
        var tiles = Scene(); tiles.EnableFallingBlockUpdates();
        var before = new WorldTile { Type = 53, Shape = 1, TileColor = 12, Wall = 1, WallColor = 7,
            Flags = WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.Actuator |
                WorldTileFlags.WireRed | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock };
        tiles.Set(30, 20, before);
        var runtime = new ServerRuntimeState(worldTiles: tiles, projectiles: new RuntimeProjectileStore(),
            projectileStepper: new VanillaProjectileWorldStateStepper(tiles));
        runtime.Tick();
        var after = tiles.Get(30, 20);
        Assert.Equal(before.Type, after.Type);
        Assert.Equal(0, after.Shape);
        Assert.Equal(before.Flags & ~(WorldTileFlags.Active | WorldTileFlags.Inactive), after.Flags);
        Assert.Equal(before.TileColor, after.TileColor);
        Assert.Equal(before.Wall, after.Wall);
        Assert.Equal(before.WallColor, after.WallColor);
    }

    [Fact]
    public void Conveyor_landing_retains_material_until_the_world_item_pool_has_space()
    {
        var tiles = Scene(); tiles.EnableFallingBlockUpdates();
        tiles.Set(30, 40, new WorldTile { Type = 421, Flags = WorldTileFlags.Active });
        tiles.Set(30, 20, new WorldTile { Type = 53, Flags = WorldTileFlags.Active });
        var projectiles = new RuntimeProjectileStore(); var items = new RuntimeWorldItemStore();
        for (int i = 0; i < 400; i++)
            Assert.True(items.TryAllocate(new(100, 100, 0, 0, 1, 0, WorldItemOwnershipMode.None, 2,
                false, 0, 0, byte.MaxValue, 0, byte.MaxValue, 0), out _));
        var runtime = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles, worldItems: items,
            projectileStepper: new VanillaProjectileWorldStateStepper(tiles));
        for (int i = 0; i < 100; i++) runtime.Tick();
        Assert.False(tiles.Get(30, 39).IsActive);
        Assert.Equal(0, projectiles.ActiveCount);
        Assert.True(items.TryRemove(0, out _));
        runtime.Tick();
        Assert.True(items.TryGetActive(0, out var drop));
        Assert.True(drop.TryGetItemType(out var type));
        Assert.Equal(169, type.Value);
        Assert.Equal(1, drop.Stack);
        for (int i = 0; i < 20; i++) runtime.Tick();
        Assert.Equal(1, runtime.AppliedWorldItemAllocations);
    }

    [Theory]
    [InlineData(31,false)] [InlineData(56,false)] [InlineData(67,false)] [InlineData(71,false)]
    [InlineData(179,false)] [InlineData(241,false)] [InlineData(812,false)]
    [InlineData(31,true)] [InlineData(56,true)] [InlineData(67,true)] [InlineData(71,true)]
    [InlineData(179,true)] [InlineData(241,true)] [InlineData(812,true)]
    public void Motion_matches_actual_official_AI010_and_HandleMovement(int id,bool wet)
    {
        // Original Linux1.4.5.8 SHA4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034.
        // Independently invoked AI_010 + HandleMovement, ordinary player255/channel=false. No source bodies copied.
        var tiles=Scene();
        if(wet)for(int x=0;x<100;x++)for(int y=0;y<40;y++)tiles.Set(x,y,new WorldTile{LiquidAmount=255});
        var store=new RuntimeProjectileStore();
        Assert.True(store.TrySpawnVanilla(new(new ProjectileTypeId(id),255,483,325,0,.5f,default,0,10,0,10),out var p));
        var executor=new RuntimeProjectileStateExecutor(store);
        var stepper=new VanillaProjectileWorldStateStepper(tiles);
        float[] expected=wet?[325.5f,326.41f,327.73f,412.90002f]:[325.91f,327.23f,328.96002f,421.10004f];
        for(int n=1;n<=20;n++)
        {
            executor.Tick(stepper);Assert.True(store.TryGet(p.Handle,out var current));
            if(n<=3||n==20)Assert.Equal(expected[n==20?3:n-1],current.PositionY);
            Assert.Equal(1,current.Ai.Ai0);
        }
    }

    [Fact]
    public void Large_wake_burst_is_bounded_and_material_is_conserved_after_settling()
    {
        var tiles=Scene();tiles.EnableFallingBlockUpdates();
        for(int x=10;x<90;x++)for(int y=19;y<=20;y++)tiles.Set(x,y,new WorldTile{Type=53,Flags=WorldTileFlags.Active});
        var projectiles=new RuntimeProjectileStore();var items=new RuntimeWorldItemStore();
        var runtime=new ServerRuntimeState(worldTiles:tiles,projectiles:projectiles,worldItems:items,projectileStepper:new VanillaProjectileWorldStateStepper(tiles));
        runtime.Tick();Assert.InRange(projectiles.ActiveCount,1,WorldTileAuthority.FallingSpawnsPerTick);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        for(int n=0;n<300;n++){runtime.Tick();Assert.InRange(projectiles.ActiveCount,0,RuntimeFallingBlockProjectiles.Capacity);}
        elapsed.Stop();
        output.WriteLine($"160-block collapse / 300 ticks: {elapsed.Elapsed.TotalMilliseconds:F3} ms wall; {GC.GetAllocatedBytesForCurrentThread() - allocated} bytes allocated on writer (includes assertions). Not a before/after optimization comparison.");
        Assert.Equal(0,projectiles.ActiveCount);
        int material=0;
        for(int x=0;x<100;x++)for(int y=0;y<100;y++)if(tiles.Get(x,y) is {IsActive:true,Type:53})material++;
        var buffer=new WorldItemSnapshot[400];int count=items.CopyActive(buffer);
        for(int i=0;i<count;i++){Assert.True(buffer[i].TryGetItemType(out var item));Assert.Equal(169,item.Value);material+=buffer[i].Stack;}
        Assert.Equal(160,material);
    }

    [Theory]
    [InlineData(53,31)] [InlineData(112,56)] [InlineData(116,67)] [InlineData(123,71)]
    [InlineData(224,179)] [InlineData(234,241)] [InlineData(495,812)]
    public void Unsupported_block_becomes_trusted_projectile_then_exactly_one_landed_tile(int tile, int projectile)
    {
        var tiles = Scene();
        var updates = tiles.EnableFallingBlockUpdates();
        tiles.Set(30,20,new WorldTile {Type=(ushort)tile,Flags=WorldTileFlags.Active});
        var projectiles = new RuntimeProjectileStore(); var items = new RuntimeWorldItemStore();
        var runtime = new ServerRuntimeState(worldTiles:tiles,projectiles:projectiles,worldItems:items,
            projectileStepper:new VanillaProjectileWorldStateStepper(tiles));
        runtime.Tick();
        Assert.False(tiles.Get(30,20).IsActive);
        var buffer=new ProjectileSnapshot[1001];
        Assert.Equal(1,projectiles.CopyActive(buffer));
        Assert.Equal(projectile,buffer[0].Type.Value);
        Assert.True(projectiles.IsCombatTrusted(buffer[0].Handle));
        Assert.Equal(byte.MaxValue,buffer[0].Spawner);
        Assert.Equal(325f,buffer[0].PositionY);
        Assert.Equal(.5f,buffer[0].VelocityY);
        for(int tick=0;tick<100;tick++)runtime.Tick();
        Assert.True(tiles.Get(30,39).IsActive);
        Assert.Equal(tile,tiles.Get(30,39).Type);
        Assert.Equal(0,projectiles.ActiveCount);
        Assert.Equal(0,items.CopyActive(new WorldItemSnapshot[400]));
    }

    [Theory]
    [InlineData(19)] [InlineData(1)]
    public void Platform_and_half_block_support_keep_the_falling_material(int support)
    {
        var tiles=Scene();tiles.EnableFallingBlockUpdates();
        tiles.Set(30,40,new WorldTile{Type=(ushort)support,Flags=WorldTileFlags.Active,Shape=(byte)(support==1?1:0)});
        tiles.Set(30,20,new WorldTile{Type=53,Flags=WorldTileFlags.Active});
        var projectiles=new RuntimeProjectileStore();
        var runtime=new ServerRuntimeState(worldTiles:tiles,projectiles:projectiles,projectileStepper:new VanillaProjectileWorldStateStepper(tiles));
        for(int n=0;n<100;n++)runtime.Tick();
        Assert.True(tiles.Get(30,39).IsActive);
        Assert.Equal(53,tiles.Get(30,39).Type);
        Assert.Equal(0,tiles.Get(30,40).Shape);
    }

    [Fact]
    public void Chest_above_and_full_projectile_pool_do_not_delete_sand()
    {
        var tiles=Scene();tiles.EnableFallingBlockUpdates();
        tiles.Set(30,20,new WorldTile{Type=53,Flags=WorldTileFlags.Active});
        tiles.Set(30,19,new WorldTile{Type=21,Flags=WorldTileFlags.Active});
        var projectiles=new RuntimeProjectileStore(1);
        var runtime=new ServerRuntimeState(worldTiles:tiles,projectiles:projectiles,projectileStepper:new VanillaProjectileWorldStateStepper(tiles));
        runtime.Tick();Assert.True(tiles.Get(30,20).IsActive);
        tiles.Set(30,19,default);
        Assert.True(projectiles.TrySpawnVanilla(new(new ProjectileTypeId(714),255,100,100,0,0,default,0,0,0,0),out _));
        runtime.Tick();Assert.True(tiles.Get(30,20).IsActive);Assert.Equal(1,projectiles.ActiveCount);
    }

    [Fact]
    public void Untrusted_falling_projectile_never_creates_terrain()
    {
        var tiles=Scene();var projectiles=new RuntimeProjectileStore();
        Assert.True(projectiles.TrySpawnVanilla(new(new ProjectileTypeId(31),255,483,325,0,.5f,default,0,10,0,10),out _));
        var runtime=new ServerRuntimeState(worldTiles:tiles,projectiles:projectiles,projectileStepper:new VanillaProjectileWorldStateStepper(tiles));
        for(int n=0;n<100;n++)runtime.Tick();
        Assert.False(tiles.Get(30,39).IsActive);
    }

    [Fact]
    public void Queue_overflow_has_bounded_storage_and_resumable_scan()
    {
        var queue=new WorldFallingBlockUpdates();
        for(int i=0;i<10000;i++)queue.Wake(i);
        Assert.Equal(WorldFallingBlockUpdates.Capacity,queue.Count);
        var seen=new bool[10000];int work=0;
        while(queue.TryTake(10000,out int index)){seen[index]=true;Assert.True(++work<=30000);}
        Assert.All(seen,Assert.True);
    }

    private static WorldTileStore Scene()
    {
        var tiles=new WorldTileStore(new WorldDimensions(100,100));
        for(int x=0;x<100;x++)tiles.Set(x,40,new WorldTile{Type=1,Flags=WorldTileFlags.Active});
        return tiles;
    }
}
