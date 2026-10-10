using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class HumanDirectMeleePhase1458Tests
{
    [Fact]
    public void Bound_melee_uses_original_retained_class_crit_once_and_fixed_pvp_ten()
    {
        using var source=Read("HumanDirectMelee1458");using var phase=Read("HumanDefensiveCritBuffPhase1458");
        foreach(string platform in new[]{"linux","windows"})
        foreach(var row in source.RootElement.GetProperty(platform).EnumerateArray().Where(x=>x.GetProperty("route").GetString()=="npc"))
        {
            using var f=New(phase,platform=="windows");Prepare(f,row);
            var rollRandom=new Offers(I(row,"firstCritOffer"));
            var calc=new AuthoritativeCombatCalculator(f.Players,rollRandom);
            NpcSnapshot target=default;
            Assert.Equal(CombatIntegrityReason.None,calc.TryCalculate(f.Connection,in target,1,out var roll));
            Assert.Equal(I(row,"weaponDamage"),roll.Request.BaseDamage);
            Assert.Equal(I(row,"weaponCrit"),roll.CritChance);
            Assert.Equal(I(row,"firstCritOffer")<=I(row,"weaponCrit"),roll.Request.Critical);
            Assert.Equal(2,rollRandom.Calls);
            using var targetSession=Target(f);
            var wire=Wire(targetSession.Handle.Slot.Value,f.Connection.Player.Slot.Value);
            var pvpRandom=new Offers(I(row,"firstCritOffer"));var pvp=new RuntimePvpCombatIntegrity(f.Players,pvpRandom);
            Assert.Equal(PvpCombatResolveResult.Accepted,pvp.ResolveClientItemHit(1,f.Connection,in wire,out var hit));
            Assert.Equal(I(row,"weaponDamage"),hit.Damage);
            Assert.Equal(I(row,"firstCritOffer")<=10,hit.Critical);
            Assert.Equal(2,pvpRandom.Calls);
        }
    }

    [Fact]
    public void Bound_retired_owner_refuses_before_rng_and_never_bound_component_keeps_equipment_policy()
    {
        using var phase=Read("HumanDefensiveCritBuffPhase1458");using var f=New(phase,false);
        f.Equipment(0,24,1);f.State.Tick();f.Member.ItemPhase=null;
        var random=new Offers(5);var calc=new AuthoritativeCombatCalculator(f.Players,random);NpcSnapshot npc=default;
        Assert.Equal(CombatIntegrityReason.UnownedPhase,calc.TryCalculate(f.Connection,in npc,1,out _));Assert.Equal(0,random.Calls);
        using var target=Target(f);var wire=Wire(target.Handle.Slot.Value,f.Connection.Player.Slot.Value);
        Assert.Equal(PvpCombatResolveResult.Rejected,new RuntimePvpCombatIntegrity(f.Players,random).ResolveClientItemHit(1,f.Connection,in wire,out _));Assert.Equal(0,random.Calls);
        // Existing standalone component has never owned a whole UpdatePlayers cursor.
        var unbound=new PlayerAuthority(null,null);using var slots=new Sessions(unbound);var owner=slots.Add(0,24);
        Assert.Equal(CombatIntegrityReason.None,new AuthoritativeCombatCalculator(unbound,random).TryCalculate(owner,in npc,1,out var result));
        Assert.Equal(4,result.CritChance);Assert.Equal(7,result.Request.BaseDamage);
    }

    [Fact]
    public void Real_owner_mutation_during_each_rng_offer_rejects_without_consuming_pvp_cadence()
    {
        using var phase=Read("HumanDefensiveCritBuffPhase1458");
        foreach(bool pvp in new[]{false,true})foreach(int draw in new[]{1,2})foreach(bool phaseChange in new[]{false,true})
        {
            using var f=New(phase,false);f.Equipment(0,24,1);f.State.Tick();
            f.Players.TryApply(new PlayerPvpToggleRuntimeCommand(f.Connection,true));
            var retainedPhase=f.Member.ItemPhase;using var target=Target(f);
            var random=new Offers(5,draw,()=>{
                if(phaseChange)f.Member.ItemPhase=f.Member.ItemPhase!.Value with{DerivedCombat=null};
                else f.Equipment(0,24,1);
            });
            if(!pvp){NpcSnapshot npc=default;Assert.Equal(CombatIntegrityReason.OwnerChanged,new AuthoritativeCombatCalculator(f.Players,random).TryCalculate(f.Connection,in npc,1,out _));}
            else{
                var wire=Wire(target.Handle.Slot.Value,f.Connection.Player.Slot.Value);var integrity=new RuntimePvpCombatIntegrity(f.Players,random);
                Assert.Equal(PvpCombatResolveResult.Rejected,integrity.ResolveClientItemHit(1,f.Connection,in wire,out _));
                // Restore the deliberately corrupted component input; no new whole census is asserted here.
                f.Equipment(0,24,1);f.Member.ItemPhase=retainedPhase;
                Assert.Equal(PvpCombatResolveResult.Accepted,integrity.ResolveClientItemHit(1,f.Connection,in wire,out _));
            }
            Assert.True(random.Calls>=2);
        }
        using(var f=New(phase,false))
        {
            f.Equipment(0,24,1);f.State.Tick();
            var npcs=new RuntimeNpcStore(2);var items=new RuntimeWorldItemStore();
            var body=new NpcStateUpdate(1,1,1612,1606,0,0,0,default,
                NpcSimulationState.Initial with{Life=1000,LifeMax=1000,MoneyValue=0});
            Assert.True(npcs.TrySpawn(0,in body,out var actor));bool move=true;
            var pipeline=new RuntimeNpcNetworkCombatPipeline(npcs,items,new RuntimePlayerSnapshotLookup(f.Players,null),f.Players,
                ()=>{if(move){move=false;var shifted=body with{PositionX=1616};Assert.True(npcs.TryUpdate(actor.Handle,in shifted,out _));}return 100;},
                null,new(items),null,new(1000,false,default,0,0),new(),false,false);
            var ledger=(RuntimeNpcPlayerInteractionLedger)typeof(RuntimeNpcNetworkCombatPipeline)
                .GetField("interactions",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(pipeline)!;
            var wire=new TerrariaNpcDamageState(0,1,1,0,2,0);
            Assert.Equal(RuntimeNpcNetworkDamageResult.Rejected,pipeline.TryApply(f.Connection,in wire));
            Assert.True(npcs.TryGet(actor.Handle,out var moved));Assert.Equal(1000,moved.Simulation.Life);
            Assert.False(ledger.HasAnyInteraction(actor.Handle));
            Assert.Equal(RuntimeNpcNetworkDamageResult.Committed,pipeline.TryApply(f.Connection,in wire));
            Assert.True(npcs.TryGet(actor.Handle,out var hit));Assert.True(hit.Simulation.Life<1000);
            Assert.True(ledger.HasInteraction(actor.Handle,f.Connection.Player.Slot));

            Assert.True(npcs.TrySpawn(1,in body,out var second));
            var random=new Offers(5,1,()=>{var shifted=body with{PositionX=1616};Assert.True(npcs.TryUpdate(second.Handle,in shifted,out _));});
            var integrity=new RuntimeCombatIntegrity(f.Players,npcs,random);
            var secondWire=new TerrariaNpcDamageState(1,1,1,0,2,0);
            Assert.Equal(CombatIntegrityResolveResult.Rejected,integrity.ResolveClientNpcHit(100,f.Connection,in second,in secondWire,out _,out _));
            Assert.True(npcs.TryGet(second.Handle,out var current));Assert.Equal(1000,current.Simulation.Life);
            int beforeStaleOffer=random.Calls;
            Assert.Equal(CombatIntegrityResolveResult.Rejected,integrity.ResolveClientNpcHit(100,f.Connection,in second,in secondWire,out _,out _));
            Assert.Equal(beforeStaleOffer,random.Calls);
            // A failed target offer must not adopt cadence or DPS before recovery at this same tick.
            Assert.Equal(CombatIntegrityResolveResult.Accepted,integrity.ResolveClientNpcHit(100,f.Connection,in current,in secondWire,out _,out _));
        }
    }

    private static HumanDefensiveCritBuffPhase1458Tests.Fixture New(JsonDocument phase,bool windows)=>new(
        phase.RootElement.GetProperty("linux").EnumerateArray().First(x=>x.GetProperty("spec").GetProperty("branch").GetString()=="living"&&x.GetProperty("spec").GetProperty("buffs").GetArrayLength()==0),windows);
    private static void Prepare(HumanDefensiveCritBuffPhase1458Tests.Fixture f,JsonElement row)
    {
        var spec=row.GetProperty("spec");f.Equipment(0,(short)I(spec,"Weapon"),1,(byte)I(spec,"Prefix"));f.Equipment(1,24,1);
        SetBuffs(f,spec.GetProperty("Buffs"));f.Move(false,0,false);f.State.Tick();
        if(spec.GetProperty("After").ValueKind!=JsonValueKind.Null)SetBuffs(f,spec.GetProperty("After"));
        if(spec.GetProperty("Switch").GetBoolean())f.Move(false,1,false);
        if(spec.GetProperty("Refresh").GetBoolean())f.State.Tick();
        f.Players.TryApply(new PlayerPvpToggleRuntimeCommand(f.Connection,true));
    }
    private static void SetBuffs(HumanDefensiveCritBuffPhase1458Tests.Fixture f,JsonElement ids)
    {var types=new BuffTypeId[44];var times=new int[44];int i=0;foreach(var id in ids.EnumerateArray()){types[i]=new(id.GetInt32());times[i++]=60;}f.Profiles.RestoreBuffState(f.Connection,PlayerBuffState.FromSlots(types,times));}
    private static PlayerJoinSession Target(HumanDefensiveCritBuffPhase1458Tests.Fixture f)
    {
        var pool=new PlayerSlotPool(2);Assert.True(pool.TryAcquireConnection(out var dummy));Assert.True(pool.TryAcquireConnection(out var lease));dummy!.Dispose();
        var session=new PlayerJoinSession(lease!);session.ObserveWorldRequest();session.ObserveSectionRequest();
        var c=new ConnectionHandle(GameCommandSourceId.FromConnection(99819),session.Handle);
        f.Players.TryApply(new PlayerSpawnRuntimeCommand(c,session,new(session.Slot,100,103,0,0,0,0,0)));
        f.Players.TryApply(new PlayerHealthRuntimeCommand(c,new(session.Slot,1000,1000)));
        f.Players.TryApply(new PlayerPvpToggleRuntimeCommand(c,true));
        f.Players.TryApply(new PlayerMovementRuntimeCommand(c,new(session.Slot,64,16,0,0,0,1608,1606,false,0,0,false,0,false,0,0,0,0,false,0,0)));
        return session;
    }
    private static TerrariaPlayerHurtState Wire(byte target,byte owner)=>new(target,new(owner,-1,-1,-1,0,0,0,null),1,2,2,-1);
    private static int I(JsonElement x,string name)=>x.GetProperty(name).GetInt32();
    private static JsonDocument Read(string name){using var input=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;using var gzip=new GZipStream(input,CompressionMode.Decompress);return JsonDocument.Parse(gzip);}
    private sealed class Offers(int crit,int callbackDraw=0,Action? callback=null):Random
    {public int Calls;public override int Next(int min,int max){if(++Calls==callbackDraw)callback!();return min<0?0:crit;}}
    private sealed class Sessions(PlayerAuthority players):IDisposable
    {
        private readonly PlayerSlotPool pool=new(2);private readonly List<PlayerJoinSession> sessions=[];
        public ConnectionHandle Add(int unused,int weapon){Assert.True(pool.TryAcquireConnection(out var lease));var s=new PlayerJoinSession(lease!);sessions.Add(s);s.ObserveWorldRequest();s.ObserveSectionRequest();var c=new ConnectionHandle(GameCommandSourceId.FromConnection(99829),s.Handle);players.TryApply(new PlayerSpawnRuntimeCommand(c,s,new(s.Slot,100,103,0,0,0,0,0)));players.TryApply(new PlayerEquipmentRuntimeCommand(c,new(s.Slot,0,1,0,(short)weapon,0)));return c;}
        public void Dispose(){foreach(var s in sessions)s.Dispose();}
    }
}
