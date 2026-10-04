using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class BossRecoveryPipeline1458Tests
{
    // Original BeforeLoot -> imported rules -> recovery. Boss-specific DoDeathEvents excluded from
    // this isolated ordering oracle; real accepted-death tests below cover the production boundary.
    public static IEnumerable<object[]> CoupledRows() => BossRecovery1458Tests.Rows("BossRecoveryCoupled1458")
        .Where(r => r.GetProperty("difficulty").GetInt32() == 0 && r.GetProperty("mode").GetInt32() == 0 &&
            r.GetProperty("type").GetInt32() is 4 or 13 or 14 or 15 or 35 or 50 or 113 or 125 or 126 or 127 or 134 or 222 or 245 or 262 or 266 or 398 or 657 or 668)
        .Select(r => new object[] { r });


    public static IEnumerable<object[]> RecoverySuffixRows() => BossRecovery1458Tests.Rows("BossRecoveryCoupled1458")
        .Where(r => r.GetProperty("type").GetInt32() is 4 or 13 or 14 or 15 or 35 or 50 or 113 or 125 or 126 or 127 or 134 or 222 or 245 or 262 or 266 or 398 or 657 or 668)
        .Select(r => new object[] { r });

    [Theory]
    [MemberData(nameof(RecoverySuffixRows))]
    public void Recovery_suffix_matches_original_post_imported_rng_and_all_seasonal_live_bodies(JsonElement row)
    {
        var f = new Fixture(row.GetProperty("seed").GetInt32(),row.GetProperty("mode").GetInt32());
        int type=row.GetProperty("type").GetInt32();
        bool paired=row.GetProperty("paired").GetBoolean();
        var npc=f.Spawn(type,row.GetProperty("npcWidth").GetInt32(),row.GetProperty("npcHeight").GetInt32());
        if(paired && type is 125 or 126)f.Spawn(type==125?126:125);
        if(paired && type is 4 or 113)f.Daily.Record(type==4?VanillaNpcIds.WallOfFlesh:VanillaNpcIds.EyeOfCthulhu);
        // Capture contains the original UnifiedRandom cursor immediately after imported rule/materialization
        // execution. Advance only this isolated suffix; this does not assert legacy imported rules are identical.
        for(int i=0;i<row.GetProperty("drawsBeforeRecovery").GetInt32();i++)f.Random.Next();
        Invoke(f.Pipeline,"DropBossRecoveryItemsIfEligible",npc,paired && type is 13 or 14 or 15);
        var expected=row.GetProperty("drops").EnumerateArray().Skip(row.GetProperty("importedCount").GetInt32()).ToArray();
        var actual=f.Items();
        Assert.Equal(expected.Length,actual.Length);
        for(int i=0;i<expected.Length;i++)
        {
            var e=expected[i];var a=actual[i];
            Assert.Equal((e.GetProperty("id").GetInt32(),e.GetProperty("stack").GetInt32()),((int)a.ItemNetId,(int)a.Stack));
            Assert.Equal((e.GetProperty("x").GetSingle(),e.GetProperty("y").GetSingle(),e.GetProperty("vx").GetSingle(),e.GetProperty("vy").GetSingle()),(a.PositionX,a.PositionY,a.VelocityX,a.VelocityY));
        }
        Assert.Equal(row.GetProperty("next").GetInt32(),f.Random.Next());
        Assert.Equal(row.GetProperty("eoc").GetBoolean(),f.Daily.EyeKilled);
        Assert.Equal(row.GetProperty("wof").GetBoolean(),f.Daily.WallKilled);
    }

    [Theory]
    [MemberData(nameof(CoupledRows))]
    public void Imported_loot_then_recovery_matches_original_live_integer_bodies(JsonElement row)
    {
        var f = new Fixture(row.GetProperty("seed").GetInt32());
        int type = row.GetProperty("type").GetInt32();
        bool terminal = row.GetProperty("paired").GetBoolean();
        var npc = f.Spawn(type, row.GetProperty("npcWidth").GetInt32(), row.GetProperty("npcHeight").GetInt32());
        if (terminal && type is 125 or 126) f.Spawn(type == 125 ? 126 : 125);
        if (terminal && type is 4 or 113) f.Daily.Record(type == 4 ? VanillaNpcIds.WallOfFlesh : VanillaNpcIds.EyeOfCthulhu);
        Assert.True((bool)Invoke(f.Pipeline, "TryExecuteImportedLoot", npc, terminal)!);
        Invoke(f.Pipeline, "DropBossRecoveryItemsIfEligible", npc, terminal && type is 13 or 14 or 15);
        var items = f.Items();
        var expected = row.GetProperty("drops").EnumerateArray().ToArray();
        for (int i = 0; i < Math.Min(expected.Length,items.Length); i++)
        {
            var e = expected[i]; var a = items[i];
            Assert.Equal((e.GetProperty("id").GetInt32(), e.GetProperty("prefix").GetInt32(), e.GetProperty("stack").GetInt32()),
                ((int)a.ItemNetId, (int)a.Prefix, (int)a.Stack));
            Assert.Equal((e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("vx").GetSingle(), e.GetProperty("vy").GetSingle()),
                (a.PositionX, a.PositionY, a.VelocityX, a.VelocityY));
        }
        Assert.Equal(expected.Length, items.Length);
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
        Assert.Equal(row.GetProperty("eoc").GetBoolean(), f.Daily.EyeKilled);
        Assert.Equal(row.GetProperty("wof").GetBoolean(), f.Daily.WallKilled);
    }

    [Theory]
    [InlineData(4,28)] [InlineData(50,28)] [InlineData(35,188)] [InlineData(222,1134)]
    [InlineData(125,499)] [InlineData(126,499)] [InlineData(127,499)] [InlineData(134,499)]
    [InlineData(245,499)] [InlineData(262,499)] [InlineData(657,499)] [InlineData(668,188)]
    public void Accepted_melee_death_delivers_recovery_once(int type, int potion)
    {
        var f = new Fixture(1458);
        var npc = f.Spawn(type, 101, 121);
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(npc));
        Assert.Single(f.Items(), i => i.ItemNetId == potion);
        Assert.InRange(f.Items().Count(i => i.ItemNetId == 58), 5, 9);
        int count = f.Items().Length;

        uint cursor=f.RngCursor;
        Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, f.Hit(npc));
        Assert.Equal(cursor,f.RngCursor);
        Assert.Equal(count, f.Items().Length);
        // Rejected stale generation cannot produce a second recovery or daily-state change.
        Assert.False(f.Npcs.TryGet(npc.Handle, out _));
    }

    [Fact]
    public void Twin_and_eater_recovery_is_terminal_only()
    {
        foreach (int type in new[] {125,13})
        {
            var f = new Fixture(1458);
            var first = f.Spawn(type);
            var last = f.Spawn(type == 125 ? 126 : 15);
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(first));
            Assert.DoesNotContain(f.Items(), i => i.ItemNetId is 28 or 499);
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(last));
            Assert.Single(f.Items(), i => i.ItemNetId == (type == 125 ? 499 : 28));
        }
    }

    [Fact]
    public void Moon_lord_recovery_only_at_accepted_death_tick_600_not_departure()
    {
        foreach (float phase in new[] {2f,3f})
        {
            var f = new Fixture(1458);
            var npc = f.Spawn(398);
            var update = new NpcStateUpdate(npc.Type,npc.NetId,npc.PositionX,npc.PositionY,0,0,0,new(phase,599,0,0),npc.Simulation with {Life=0});
            Assert.True(f.Npcs.TryUpdate(npc.Handle,in update,out npc));
            f.Pipeline.NpcAiStateCommitted(in npc);
            Assert.Empty(f.Items());
            if (phase == 3f) continue;
            update=update with {Ai=new(2,600,0,0)};
            Assert.True(f.Npcs.TryUpdate(npc.Handle,in update,out npc));
            f.Pipeline.NpcAiStateCommitted(in npc);
            Assert.Single(f.Items(), i=>i.ItemNetId==3544);
            var count=f.Items().Length;
            f.Pipeline.NpcAiStateCommitted(in npc);
            Assert.Equal(count,f.Items().Length);
        }
    }

    [Fact]
    public void Daily_flags_reset_at_dusk_but_not_dawn_and_do_not_follow_new_world()
    {
        foreach (bool day in new[]{true,false})
        {
            var clock=new RuntimeWorldClock(day ? 54000 : 32400,day,0,0,1);
            clock.BossRecoveryDailyState.Record(VanillaNpcIds.EyeOfCthulhu);
            clock.Tick();
            Assert.Equal(!day,clock.BossRecoveryDailyState.EyeKilled);
        }
        Assert.False(new RuntimeWorldClock(0,true,0,0,1).BossRecoveryDailyState.EyeKilled);
    }


    [Theory]
    [InlineData(399,true)] [InlineData(390,true)] [InlineData(391,true)] [InlineData(389,true)]
    public void Unknown_allocation_lease_rejects_before_any_death_mutation_or_rng(int active,bool leased)
    {
        var f=new Fixture(1458);
        for(int i=0;i<active;i++) Assert.True(f.Store.TryAllocateDrop(new(10,20,0,0,1,0,WorldItemOwnershipMode.None,1,false,0,0),out _));
        WorldItemDropReservation reservation=default;
        if(leased)Assert.True(f.Store.TryReserveDropSlot(out reservation));
        var npc=f.Spawn(4);
        Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected,f.Hit(npc));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,f.Pipeline.TryStrikeEnvironment(npc.Handle,100_000));
        var wire=new TerraRuntime.Protocol.TerrariaNpcDamageState(npc.Handle.Slot,
            RuntimeNpcPacketProjection.ToProtocolGeneration(npc.Handle.Generation),1000,0,2,0);
        Assert.Equal(RuntimeNpcNetworkDamageResult.Rejected,f.Pipeline.TryApply(new(GameCommandSourceId.FromConnection(1),new(new(0),new(1))),in wire));
        Assert.True(f.Npcs.TryGet(npc.Handle,out var unchanged));
        Assert.Equal(npc,unchanged);
        Assert.Equal(active,f.Store.ActiveCount);
        Assert.False(f.Daily.EyeKilled);
        Assert.Equal(new VanillaUnifiedRandom1458(1458).Next(),f.Random.Next());
        if(leased)Assert.True(f.Store.TryReleaseDropReservation(in reservation));
    }

    [Fact]
    public void Sufficient_preflight_capacity_does_not_reserve_slots_ahead_of_source_loot_order()
    {
        var f=new Fixture(1458);
        for(int i=0;i<373;i++)Assert.True(f.Store.TryAllocateDrop(new(10,20,0,0,9999,0,WorldItemOwnershipMode.None,1,false,0,0),out _));
        var npc=f.Spawn(4,101,121);
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed,f.Hit(npc));
        Assert.Equal(new short[]{47,56,59,28},f.Items().Skip(373).Take(4).Select(a=>a.ItemNetId));
        Assert.True(f.Store.TryReserveDropSlot(out var available));
        Assert.True(f.Store.TryReleaseDropReservation(in available));
    }

    [Fact]
    public void Moon_lord_terminal_pressure_defers_loot_without_consuming_rng_and_resumes_once()
    {
        var f=new Fixture(1458);
        var npc=f.Spawn(398);
        var update=new NpcStateUpdate(npc.Type,npc.NetId,npc.PositionX,npc.PositionY,0,0,0,new(2,600,0,0),npc.Simulation with {Life=0});
        Assert.True(f.Npcs.TryUpdate(npc.Handle,in update,out npc));
        var held=new List<WorldItemDropReservation>();
        for(int i=0;i<400;i++){Assert.True(f.Store.TryReserveDropSlot(out var slot));held.Add(slot);}
        uint cursor=f.RngCursor;
        f.Pipeline.NpcAiStateCommitted(in npc);
        Assert.Equal(cursor,f.RngCursor);
        Assert.True(f.Npcs.TryGet(npc.Handle,out var unchanged));
        Assert.Equal(npc,unchanged);
        Assert.Empty(f.Items());
        foreach(var slot in held)Assert.True(f.Store.TryReleaseDropReservation(in slot));
        f.Pipeline.NpcAiStateCommitted(in npc);
        Assert.Single(f.Items(),i=>i.ItemNetId==3544);
        int count=f.Items().Length;
        f.Pipeline.NpcAiStateCommitted(in npc);
        Assert.Equal(count,f.Items().Length);
    }

    [Fact]
    public void Accepted_eye_wall_pair_grants_hat_after_recovery_and_clears_transient_flags_once()
    {
        foreach(bool reverse in new[]{false,true})
        {
            var f=new Fixture(1458);
            var first=f.Spawn(reverse?113:4);
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed,f.Hit(first));
            Assert.DoesNotContain(f.Items(),i=>i.ItemNetId==5004);
            var last=f.Spawn(reverse?4:113);
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed,f.Hit(last));
            Assert.Single(f.Items(),i=>i.ItemNetId==5004);
            Assert.All(f.Items().SkipWhile(i=>i.ItemNetId!=5004).Skip(1), item=>Assert.True(VanillaCoinFacts.TryGetValue(new(item.ItemNetId),out _)));
            Assert.False(f.Daily.EyeKilled||f.Daily.WallKilled);
            Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected,f.Hit(last));
            Assert.Single(f.Items(),i=>i.ItemNetId==5004);
        }
    }

    private static object? Invoke(object instance,string method,params object[] args) =>
        typeof(RuntimeNpcNetworkCombatPipeline).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(instance,args);
    internal sealed class Fixture : IRuntimePlayerSlotSnapshotLookup
    {
        public RuntimeNpcStore Npcs {get;}=new();
        public RuntimeWorldItemStore Store {get;}=new();
        public uint RngCursor=>(uint)typeof(VanillaUnifiedRandom1458).GetField("inext",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Random)!;
        public VanillaUnifiedRandom1458 Random {get;}
        public RuntimeWorldClock Clock {get;}=new(0,true,0,0,1);
        public VanillaBossRecoveryDailyState1458 Daily=>Clock.BossRecoveryDailyState;
        public TerraRuntime.World.RuntimeWorldProgressionMutations Progression {get;}=new();
        public RuntimeNpcNetworkCombatPipeline Pipeline {get;}
        private readonly PlayerHandle player=new(new(0),new(1));
        public Fixture(int seed,int mode=0)
        {
            Random=new(seed);
            Pipeline=new(Npcs,Store,this,new PlayerAuthority(null,null),static()=>0,null,new(Store),null,Clock,Progression,false,false,planteraDownedBaseline:false,lootRandom:Random,seasonalItemContext:()=>new((mode&1)!=0,(mode&2)!=0,(mode&4)!=0));
        }
        public NpcSnapshot Spawn(int type,int width=0,int height=0)
        {
            var simulation=NpcSimulationState.Initial;
            if(width!=0)simulation=simulation with {HitboxOverride=new(width,height)};
            Assert.True(Npcs.TrySpawnVanilla(new(type,checked((short)type),1000.75f,1000.25f,0,0,0,default,simulation),out var npc));
            return npc;
        }
        public RuntimeProjectileNpcDamageResult Hit(in NpcSnapshot npc)=>Pipeline.TryStrikeServerPlayerMelee(player,npc.Handle,100_000,0,false,0,1);
        public WorldItemSnapshot[] Items()
        {
            var result=new WorldItemSnapshot[Store.Capacity];
            return result[..Store.CopyActive(result)];
        }
        public bool TryGetPlayer(PlayerSlotId slot,out PlayerStateSnapshot snapshot)
        {
            snapshot=default(PlayerStateSnapshot) with {Player=player,Revision=new(1),PositionX=1000,PositionY=1000,HasHealth=true,Life=400,MaxLife=400};
            return slot==player.Slot;
        }
    }
}
