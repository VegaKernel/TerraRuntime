using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
using Xunit;
using RangedFixture = TerraRuntime.Tests.BotAmmoIndependentSource1458Tests.Fixture;

namespace TerraRuntime.Tests;

public sealed class BotMeleeAtomicInventory1458Tests
{
    [Fact]
    public void Prepared_callback_seams_adopt_all_owners_and_preserve_newer_state()
    {
        foreach (int type in new[] { 3, 6 })
        foreach (string mode in new[] { "baseline", "newheld", "newactor", "reentry", "damageThrow", "rewindClock", "throwMove", "mixedThrow" })
        {
        var (f, bot, npcs, target, combat, observation, sink) = Create(type);
        int moveCalls=0, damageCalls=0; object? firstMove=null; object? firstDamage=null;
        bool nested=false; RuntimeBotActionResult? nestedResult=null; string? exception=null;
        f.Events.Observe = kind => {
            if(kind!="move") return;
            moveCalls++;
            Assert.True(npcs.TryGet(target.Handle,out var adoptedTarget) && adoptedTarget.Simulation.Life<1000);
            Assert.Equal(18,bot.NextAttackTick);
            if(!nested) Assert.Equal(18,bot.UseItemUntilTick);
            Assert.True(npcs.TryGet(target.Handle,out var now));
            firstMove ??= new {life=now.Simulation.Life, attackTick=bot.NextAttackTick,useUntil=bot.UseItemUntilTick};
            if(nested) return;
            nested=true;
            if(mode=="newheld") Assert.True(f.Players.SetItem(f.Id,new(0,new(3),1,default,0)));
            if(mode=="newactor") {
                Assert.True(f.Players.Despawn(f.Id));
                Assert.True(f.Players.Create(f.Id,160,160).IsCreated);
                Assert.True(f.Players.SetVitals(f.Id,new(400,400,200,200)));
                Assert.True(f.Players.SetItem(f.Id,new(0,new(3),1,default,0)));
                Assert.True(f.Players.SetHeldItem(f.Id,0,useItem:true));
            }
            if(mode=="reentry") nestedResult=combat.Attack(observation);
            if(mode=="rewindClock") {bot.NextAttackTick=0;bot.UseItemUntilTick=999;nestedResult=combat.Attack(observation);}
            if(mode=="throwMove"||mode=="mixedThrow") throw new InvalidOperationException("actual move observer");
        };
        sink.Observe = (kind, npc) => {
            if(kind!=NpcStateCommitKind.Update) return;
            damageCalls++; Assert.Equal(mode=="rewindClock"?0:18,bot.NextAttackTick); firstDamage ??=new {life=npc.Simulation.Life,attackTick=bot.NextAttackTick,useUntil=bot.UseItemUntilTick};
            if(mode=="damageThrow"||mode=="mixedThrow") throw new InvalidOperationException("actual damage observer");
        };
        RuntimeBotActionResult? result=null;
        try {result=combat.Attack(observation);}catch(InvalidOperationException e){exception=e.Message;}
        Assert.True(npcs.TryGet(target.Handle,out var after));
        Assert.True(f.Players.TryGetItem(f.Id,0,out var finalHeld));
        Assert.True(f.Players.TryGetPlayer(f.Id,out var handle));
        Assert.True(f.Players.TryGet(handle,out var actor));
        Assert.NotNull(firstMove);
        Assert.Equal(mode=="rewindClock"?0:18,bot.NextAttackTick);
        if(mode=="baseline"||mode=="newheld"||mode=="reentry"||mode=="damageThrow") Assert.True(after.Simulation.Life<1000);
        if(mode=="newheld") Assert.Equal(new ItemTypeId(3),finalHeld.ItemType);
        if(mode=="reentry"||mode=="rewindClock") Assert.Equal(1,damageCalls);
        if(mode=="newactor") {Assert.Equal(96,actor.ControlFlags);Assert.True(after.Simulation.Life<1000);}
        if(mode=="rewindClock") {Assert.Equal(999,bot.UseItemUntilTick);Assert.NotEqual(RuntimeBotActionResult.Success,nestedResult);}
        if(mode=="throwMove"||mode=="mixedThrow") {Assert.Equal("actual move observer",exception);Assert.Equal(1,damageCalls);}
        if(mode=="baseline"||mode=="newheld"||mode=="newactor"||mode=="reentry"||mode=="rewindClock") Assert.Equal(RuntimeBotActionResult.Success,result);
        if(mode=="damageThrow") {Assert.Equal("actual damage observer",exception);Assert.Equal(18,bot.NextAttackTick);}
        }
    }
    [Fact]
    public void Prepared_census_and_target_refusal_never_fall_back_to_old_attack()
    {
        foreach(int type in new[] {3,6})
        {
            var (f,bot,npcs,target,combat,observation,sink)=Create(type);
            var moved=new NpcStateUpdate(target.Type,target.NetId,1800,target.PositionY,0,0,target.Target,target.Ai,target.Simulation);
            Assert.True(npcs.TryUpdate(target.Handle,moved,out var far));
            Assert.NotEqual(RuntimeBotActionResult.Success,combat.Attack(observation));
            Assert.True(npcs.TryGet(target.Handle,out var after)); Assert.Equal(far,after);
            Assert.Equal(0,bot.NextAttackTick); Assert.Equal(0,bot.UseItemUntilTick);
            Assert.DoesNotContain("move",f.Events.Seen); Assert.DoesNotContain("presentation",f.Events.Seen);
        }
        foreach (string change in new[] { "identity", "prefix", "otherSlot", "actor", "target", "configuration", "goal", "observation", "tick" })
        {
            var (f, bot, npcs, target, combat, observation, sink) = Create(3);
            int calls = 0;
            f.OnTick = () => {
                calls++;
                if(change=="identity") Assert.True(f.Players.SetItem(f.Id,new(0,new(3),1,default,0)));
                if(change=="prefix") Assert.True(f.Players.SetItem(f.Id,new(0,new(155),1,new(36),0)));
                if(change=="otherSlot") Assert.True(f.Players.SetItem(f.Id,new(1,new(98),1,default,0)));
                if(change=="actor") {
                    Assert.True(f.Players.Despawn(f.Id)); Assert.True(f.Players.Create(f.Id,160,160).IsCreated);
                    Assert.True(f.Players.SetVitals(f.Id,new(400,400,200,200)));
                    Assert.True(f.Players.SetItem(f.Id,new(0,new(155),1,default,0)));
                }
                if(change=="target") {
                    Assert.True(npcs.TryDespawn(target.Handle));
                    var next=new NpcStateUpdate(3,3,180,160,0,0,255,default,NpcSimulationState.Initial with {Life=777,LifeMax=1000,MoneyValue=0});
                    Assert.True(npcs.TrySpawnVanilla(next,out _));
                }
                if(change=="configuration") bot.Configuration=bot.Configuration with {WeaponPolicy=RuntimeBotWeaponPolicy.Gun};
                if(change=="goal") bot.GoalGeneration++;
                if(change=="observation") bot.ObservationRevision++;
                if(change=="tick") bot.CurrentTick++;
            };
            RuntimeBotActionResult? refused=null; Exception? unexpected=null;
            try {refused=combat.Attack(observation);}catch(Exception exception){unexpected=exception;}
            Assert.Null(unexpected);
            Assert.NotEqual(RuntimeBotActionResult.Success,refused);
            Assert.Equal(1,calls);
            Assert.Equal(0,bot.NextAttackTick); Assert.Equal(0,bot.UseItemUntilTick);
            Assert.DoesNotContain("move",f.Events.Seen); Assert.DoesNotContain("presentation",f.Events.Seen);
            if(change!="target") {Assert.True(npcs.TryGet(target.Handle,out var unchanged));Assert.Equal(target,unchanged);}
        }
    }

    private static (RangedFixture F, BotState Bot, RuntimeNpcStore Npcs, NpcSnapshot Target,
        RuntimeBotCombat Combat, RuntimeBotObservationSnapshot Observation, Sink Sink) Create(int type)
    {
        int retained = 155;
        var loadout = RuntimePlayerBotLoadoutCatalog1458.Pick(new Random(42));
        var f = new RangedFixture(98, 20);
        var configuration = f.Bot.Configuration with { WeaponPolicy = RuntimeBotWeaponPolicy.Melee };
        var bot = new BotState(f.Bot.Id, f.Id, "melee", configuration, loadout,
            default, default, 0) { Player = f.Bot.Player, ObservationRevision = 1 };
        Assert.True(f.Players.SetItem(f.Id, new(0, new(retained), (short)(retained == 0 ? 0 : 1), default, 0)));
        var sink = new Sink();
        var npcs = new RuntimeNpcStore(commitSink: sink);
        var human = new PlayerAuthority(null, f.Tiles);
        var lookup = new RuntimePlayerSnapshotLookup(human, f.Players);
        var items = new RuntimeWorldItemStore();
        var authority = new NpcAuthority(lookup, () => 0, human, npcs, f.Shots, items,
            new SystemWorldItemSpawnRandom(0), new RuntimeWorldItemInstancedLeaseStore(items),
            f.Tiles, null, new RuntimeWorldProgressionMutations(), null, null, null,
            null, null, null, null, null, false, false, null, f.Players,
            null, null, null, null, false, false, false, true, false);
        var body = new NpcStateUpdate(type, (short)(type),
            180, 160, 0, 0, 255, default, NpcSimulationState.Initial with { Life = 1000, LifeMax = 1000, MoneyValue = 0 });
        Assert.True(npcs.TrySpawnVanilla(in body, out var target));
        var inventory = new RuntimeBotInventory(bot, f.Players,
            new WorldItemAuthority(human, items, new SystemWorldItemSpawnRandom(0), null), new(), f.World);
        var combat = new RuntimeBotCombat(bot, f.Players, authority, f.Projectiles,
            f.Tiles, inventory, [bot], f.World);
        Assert.True(f.Players.TryGet(bot.Player, out var before));
        f.Events.Seen.Clear();
        var observation = new RuntimeBotObservationSnapshot(bot.Id, f.World, 1, bot.GoalGeneration,
            0, before, configuration, null,
            new BotGuardTarget(target.Handle, default, 189, 180, 0, 0, 18, 40),
            null, false, null, null);

        return (f,bot,npcs,target,combat,observation,sink);
    }

    private sealed class Sink : INpcStateCommitSink
    {
        internal Action<NpcStateCommitKind,NpcSnapshot>? Observe;
        public void NpcStateCommitted(NpcStateCommitKind kind,in NpcSnapshot npc)=>Observe?.Invoke(kind,npc);
    }
}
