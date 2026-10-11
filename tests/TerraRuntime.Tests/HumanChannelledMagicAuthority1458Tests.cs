using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;
using TerraRuntime.Protocol;

using Xunit;
namespace TerraRuntime.Tests;
public sealed class HumanChannelledMagicAuthority1458Tests
{
// The original remote corpus covers both arithmetic platforms. These runtime
// State.Tick comparisons use its Linux rows and managed-only full random-state
// diagnostics. Local caller rows are a separate Linux component proof; their
// configured shot positions are translated into the admitted runtime world.
[Fact]
public void Both_packet_orders_pay_once_and_admit_normal_and_shortage_births()
{
using var phases=ReadSource("HumanChannelledMagicPhase1458");
using var local=ReadSource("HumanChannelledMagicLocalCaller1458");
int scenarios=0;
foreach(int weapon in new[]{113,218,495})
foreach(bool phaseFirst in new[]{false,true})
foreach(string manaCase in new[]{"above","equal","below","zero"})
{
    int cost=weapon==113?14:21;
    int mana=manaCase switch {"above"=>100,"equal"=>cost,"below"=>cost-1,_=>0};
    bool shortage=mana<cost;
    string name=$"{weapon}-{manaCase}-success";
    var profile=phases.RootElement.EnumerateArray().First(x=>x.GetProperty("spec").GetProperty("name").GetString()==name);
    using var f=new Harness.Fixture(profile,false);
    f.Move(false,0,false,1600,1606);f.State.Tick();
    f.Move(true,0,true,1600,1606);
    if(phaseFirst)f.State.Tick();
    var l=local.RootElement.EnumerateArray().First(x=>x.GetProperty("weapon").GetInt32()==weapon&&x.GetProperty("initialMana").GetInt32()==mana&&x.GetProperty("aimDirection").GetInt32()==1);
    var shot=l.GetProperty("shots")[0];
    var pos=shot.GetProperty("position");var vel=shot.GetProperty("velocity");
    var packet=new TerrariaProjectileUpdateState(new(0,701,1),shot.GetProperty("type").GetInt32(),
        pos.GetProperty("X").GetSingle()+1200,pos.GetProperty("Y").GetSingle()+1206,
        vel.GetProperty("X").GetSingle(),vel.GetProperty("Y").GetSingle(),0,0,0,0,
        shot.GetProperty("damage").GetInt16(),shot.GetProperty("knockBack").GetSingle(),0);
    int after=Math.Max(0,mana-(weapon==113?14:21));
    f.Players.TryGetInventoryItem(f.Connection,0,out var selected);
    VanillaProjectileWeaponCombatCatalog.TryGetChanneledMagicWeapon(new(weapon),out var definition);
    f.State.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection,packet));
    Assert.Equal(1,f.Projectiles.ActiveCount);
    var body=new TerraRuntime.Contracts.Runtime.ProjectileSnapshot[f.Projectiles.Capacity];
    Assert.Equal(1,f.Projectiles.CopyActive(body));
    Assert.True(f.Projectiles.IsCombatTrusted(body[0].Handle));
    Assert.Equal(packet.Damage,body[0].Damage);
    Assert.Equal(packet.VelocityX,body[0].VelocityX);
    Assert.Equal(after,(int)f.Member.Mana);
    Assert.True(f.Member.ItemPhase!.Value.MagicPayment!.Value.BirthConsumed);
    // Actual reported animation and same-epoch42 are compatibility inputs, not an additional grant.
    f.State.Apply(new PlayerItemAnimationRuntimeCommand(f.Connection,0,(short)(shortage?(weapon==113?55:weapon==218?75:62):(weapon==113?22:weapon==218?30:25))));
    f.State.Apply(new PlayerManaRuntimeCommand(f.Connection,new(f.Connection.Player.Slot,(short)after,200)));
    f.State.Tick();
    Assert.True(f.Member.ItemPhase is not null);
    Assert.Equal(after,(int)f.Member.Mana);
    f.State.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection,packet with{Key=new(0,702,1)}));
    Assert.Equal(1,f.Projectiles.ActiveCount);
    f.State.Apply(new PlayerManaRuntimeCommand(f.Connection,new(f.Connection.Player.Slot,100,220)));
    Assert.Equal(200,f.Member.ItemPhase!.Value.BaseManaMaximum);
    scenarios++;
}
Assert.Equal(24,scenarios);
}
[Fact]
public void Original_remote_phases_preserve_mana_clocks_channel_and_full_random_state()
{
using var phases=ReadSource("HumanChannelledMagicPhase1458");
using var local=ReadSource("HumanChannelledMagicLocalCaller1458");
int phasesCompared=0;
foreach(var row in phases.RootElement.EnumerateArray())
{
    var spec=row.GetProperty("spec");
    if(spec.GetProperty("armor").GetArrayLength()!=0) continue;
    using var f=new Harness.Fixture(row,false);
    foreach(var step in row.GetProperty("steps").EnumerateArray())
    {
        int tick=step.GetProperty("tick").GetInt32();
        if(spec.GetProperty("branch").GetString()=="living")
            f.Move(tick>0&&tick!=5,(byte)(tick>=30?1:0),spec.GetProperty("lastSuccess").GetBoolean());
        else f.PrepareStep(step);
        if(spec.TryGetProperty("mixed",out var mixed)&&mixed.GetBoolean())
            f.State.Apply(new PlayerMovementRuntimeCommand(f.SecondConnection,
                new(f.SecondConnection.Player.Slot,(byte)(64|((tick>0&&tick!=5)?32:0)),16,0,0,0,1700,1606,false,0,0,false,0,false,0,0,0,0,false,0,0)));
        f.State.Tick();
        Assert.True(f.Member.ItemPhase is not null,$"{spec.GetProperty("name")}/{tick}");
        var actual=f.Member.ItemPhase!.Value;var e=step.GetProperty("player");
        Assert.Equal(e.GetProperty("statMana").GetInt32(),(int)f.Member.Mana);
        Assert.Equal(e.GetProperty("manaRegenCount").GetInt32(),actual.Mana.Count);
        Assert.Equal(e.GetProperty("manaRegenDelay").GetSingle(),actual.Mana.Delay);
        Assert.Equal(e.GetProperty("itemAnimation").GetInt32(),actual.Selected.Animation);
        Assert.Equal(e.GetProperty("itemAnimationMax").GetInt32(),actual.Selected.AnimationMax);
        Assert.Equal(e.GetProperty("itemTime").GetInt32(),actual.Selected.ItemTime);
        Assert.Equal(e.GetProperty("releaseUseItem").GetBoolean(),actual.Selected.ReleaseUseItem);
        Assert.Equal(e.GetProperty("channel").GetBoolean(),actual.Channel);
        Assert.Equal(e.GetProperty("slowMagicUse").GetBoolean(),actual.SlowMagicUse);
        Assert.Equal(e.GetProperty("pendingItemReuse").GetBoolean(),actual.PendingItemReuse);
        var rng=step.GetProperty("after");
        Assert.Equal(rng.GetProperty("cursor").GetUInt32(),(uint)typeof(TerraRuntime.Core.VanillaUnifiedRandom1458).GetField("inext",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Random)!);
        Assert.Equal(rng.GetProperty("state").EnumerateArray().Select(x=>x.GetInt32()),(int[])typeof(TerraRuntime.Core.VanillaUnifiedRandom1458).GetField("seedArray",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Random)!);
    phasesCompared++;
    }
}
Assert.Equal(1120,phasesCompared);
}
[Fact]
public async Task Failed_attempts_reports_channel_transfer_and_currentness_keep_owned_payment()
{
using var phases=ReadSource("HumanChannelledMagicPhase1458");
using var local=ReadSource("HumanChannelledMagicLocalCaller1458");
foreach(int weapon in new[]{113,218,495})
{
    int cost=weapon==113?14:21;
    var row=phases.RootElement.EnumerateArray().First(x=>x.GetProperty("spec").GetProperty("name").GetString()==$"{weapon}-above");
    using(var failed=new Harness.Fixture(row,false))
    {
        failed.Move(false,0,false);failed.State.Tick();
        failed.Move(true,0,false);failed.State.Tick();
        Assert.Equal(100-cost,(int)failed.Member.Mana);
        Assert.Equal(0,failed.Member.ItemPhase!.Value.Selected.Animation);
        Assert.False(failed.Member.ItemPhase.Value.MagicPayment!.Value.EligibleBirth);
        ulong firstAttempt=failed.Member.ItemPhase.Value.MagicPayment!.Value.Attempt;
        failed.State.Tick();Assert.Equal(100-cost,(int)failed.Member.Mana);
        Assert.Equal(firstAttempt,failed.Member.ItemPhase.Value.MagicPayment!.Value.Attempt);
        failed.Move(false,0,false);failed.State.Tick();
        failed.Move(true,0,false);failed.State.Tick();
        Assert.Equal(100-cost*2,(int)failed.Member.Mana);
        Assert.Equal(firstAttempt+1,failed.Member.ItemPhase.Value.MagicPayment!.Value.Attempt);
    }
    var successful=phases.RootElement.EnumerateArray().First(x=>x.GetProperty("spec").GetProperty("name").GetString()==$"{weapon}-above-success");
    using var regen=new Harness.Fixture(successful,false);
    regen.Move(false,0,false);regen.State.Tick();regen.Move(true,0,true);regen.State.Tick();
    int paid=regen.Member.Mana;
    for(int i=0;i<80;i++)regen.State.Tick();
    Assert.True(regen.Member.Mana>paid);
    var owned=regen.Member.ItemPhase!.Value;
    regen.State.Apply(new PlayerManaRuntimeCommand(regen.Connection,new(regen.Connection.Player.Slot,(short)paid,200)));
    Assert.Equal(owned,regen.Member.ItemPhase!.Value);
    Assert.Equal(owned.Mana.Mana,(int)regen.Member.Mana);
    regen.State.Apply(new PlayerManaRuntimeCommand(regen.Connection,new(regen.Connection.Player.Slot,200,200)));
    Assert.Equal(owned,regen.Member.ItemPhase!.Value);
}
var currentProfile=phases.RootElement.EnumerateArray().First(x=>x.GetProperty("spec").GetProperty("name").GetString()=="113-above-success");
// Runtime-policy counterexamples: an otherwise valid original shot cannot bypass
// current source no-use inputs in either ordering. The original numeric rows are unchanged.
foreach(string branch in new[]{"cursed","ghost","outside"})
foreach(bool phaseFirst in new[]{false,true})
{
    using var denied=new Harness.Fixture(currentProfile,false);
    denied.Move(false,0,false);denied.State.Tick();
    if(branch=="cursed")SetCursed(denied);
    denied.Move(true,0,true);
    if(branch=="ghost")denied.Member.MovementFlags|=64;
    if(branch=="outside"){denied.Member.PositionX=0;denied.Member.PositionY=0;}
    if(phaseFirst)denied.State.Tick();
    var before=denied.Member.ItemPhase;var mana=denied.Member.Mana;
    var hp=denied.Member.NpcHealth;var random=denied.Random.Clone();
    var packet=new TerrariaProjectileUpdateState(new(0,991,1),16,
        denied.Member.PositionX-6,denied.Member.PositionY+5,6,0,0,0,0,0,35,7.5f,0);
    denied.State.Apply(new ClientProjectileUpdateRuntimeCommand(denied.Connection,packet));
    Assert.Equal(0,denied.Projectiles.ActiveCount);Assert.Equal(mana,denied.Member.Mana);
    Assert.Equal(before,denied.Member.ItemPhase);Assert.Equal(hp,denied.Member.NpcHealth);
    Assert.True(denied.Random.HasSameState(random));
}
foreach(string branch in new[]{"cursed","ghost","outside"})
{
    using var late=new Harness.Fixture(currentProfile,false);
    late.Move(false,0,false);late.State.Tick();late.Move(true,0,true);late.State.Tick();
    var replication=new RuntimeProjectileReplicationRegistry();
    var store=new TerraRuntime.Core.Projectiles.RuntimeProjectileStore(commitSink:replication);
    bool mutate=true;
    var authority=new ProjectileAuthority(store,late.Players,new TerraRuntime.Core.Npcs.RuntimeNpcStore(),
        new RuntimePlayerSnapshotLookup(late.Players,null),null,replication,()=>
        {
            if(mutate)
            {
                mutate=false;
                if(branch=="cursed")SetCursed(late);
                if(branch=="ghost")late.Member.MovementFlags|=64;
                if(branch=="outside"){late.Member.PositionX=0;late.Member.PositionY=0;}
            }
            return 0;
        });
    var before=late.Member.ItemPhase;var mana=late.Member.Mana;var hp=late.Member.NpcHealth;
    var random=late.Random.Clone();
    var packet=new TerrariaProjectileUpdateState(new(0,992,1),16,1594,1611,6,0,0,0,0,0,35,7.5f,0);
    Assert.True(authority.TryApply(new ClientProjectileUpdateRuntimeCommand(late.Connection,packet)));
    Assert.Equal(0,store.ActiveCount);Assert.False(mutate);
    Assert.Equal(mana,late.Member.Mana);Assert.Equal(before,late.Member.ItemPhase);
    Assert.Equal(hp,late.Member.NpcHealth);Assert.True(late.Random.HasSameState(random));
    if(branch=="cursed")
    {
        Assert.True(late.Players.TryCaptureProjectileUse(late.Connection,out var capture));
        Assert.Equal(new[]{23},capture!.Buffs!.Select(x=>x.Value));
    }
    if(branch=="ghost")Assert.NotEqual(0,late.Member.MovementFlags&64);
    if(branch=="outside")Assert.Equal(0f,late.Member.PositionX);
}
using(var changed=new Harness.Fixture(currentProfile,false))
{
    changed.Move(false,0,false);changed.State.Tick();changed.Move(true,0,true);changed.State.Tick();
    var replication=new RuntimeProjectileReplicationRegistry();
    var store=new TerraRuntime.Core.Projectiles.RuntimeProjectileStore(commitSink:replication);
    bool mutate=true;
    var authority=new ProjectileAuthority(store,changed.Players,new TerraRuntime.Core.Npcs.RuntimeNpcStore(),
        new RuntimePlayerSnapshotLookup(changed.Players,null),null,replication,()=>
        {
            if(mutate) {mutate=false;changed.Member.ItemPhase=changed.Member.ItemPhase!.Value with{ManaHeat=.75f};}
            return 0;
        });
    var packet=new TerrariaProjectileUpdateState(new(0,800,1),16,1594,1611,6,0,0,0,0,0,35,7.5f,0);
    Assert.True(authority.TryApply(new ClientProjectileUpdateRuntimeCommand(changed.Connection,packet)));
    Assert.Equal(0,store.ActiveCount);
    Assert.Equal(86,(int)changed.Member.Mana);
    Assert.False(changed.Member.ItemPhase!.Value.MagicPayment!.Value.BirthConsumed);
    // Final captured-phase guard must reject the first plan before birth, mana or cadence adoption.
    Assert.True(authority.TryApply(new ClientProjectileUpdateRuntimeCommand(changed.Connection,packet)));
    Assert.Equal(1,store.ActiveCount);
    Assert.Equal(86,(int)changed.Member.Mana);
}
foreach(int weapon in new[]{113,218,495})
{
    var profile=phases.RootElement.EnumerateArray().First(x=>x.GetProperty("spec").GetProperty("name").GetString()==$"{weapon}-above-success");
    using var f=new Harness.Fixture(profile,false);
    f.Move(false,0,false);f.State.Tick();f.Move(true,0,true);f.State.Tick();
    var l=local.RootElement.EnumerateArray().First(x=>x.GetProperty("weapon").GetInt32()==weapon&&x.GetProperty("initialMana").GetInt32()==100&&x.GetProperty("aimDirection").GetInt32()==1);
    var shot=l.GetProperty("shots")[0];
    var packet=new TerrariaProjectileUpdateState(new(0,901,1),shot.GetProperty("type").GetInt32(),
        shot.GetProperty("position").GetProperty("X").GetSingle()+1200,
        shot.GetProperty("position").GetProperty("Y").GetSingle()+1206,6,0,0,0,0,0,
        shot.GetProperty("damage").GetInt16(),shot.GetProperty("knockBack").GetSingle(),0);
    f.State.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection,packet));
    Assert.Equal(1,f.Projectiles.ActiveCount);
    f.Move(false,0,true);
    f.State.Apply(new PlayerItemAnimationRuntimeCommand(f.Connection,0,20));
    Assert.True(f.Member.ItemPhase!.Value.Channel);
    f.State.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection,packet with{Ai0=1700,Ai1=1600}));
    var body=new TerraRuntime.Contracts.Runtime.ProjectileSnapshot[f.Projectiles.Capacity];
    Assert.Equal(1,f.Projectiles.CopyActive(body));
    Assert.Equal(1700f,body[0].Ai.Ai0);
    Assert.Equal(1600f,body[0].Ai.Ai1);
    // Unknown retained custody cannot manufacture a release or accept continued steering.
    f.Member.ItemPhase=null;
    f.State.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection,packet with{Ai0=1800,Ai1=1600}));
    Assert.Equal(1,f.Projectiles.CopyActive(body));
    Assert.Equal(1700f,body[0].Ai.Ai0);
}
foreach(int weapon in new[]{113,218,495})
{
    var profile=phases.RootElement.EnumerateArray().First(x=>x.GetProperty("spec").GetProperty("name").GetString()==$"{weapon}-above-success");
    using var f=new Harness.Fixture(profile,false);
    f.Move(false,0,false);f.State.Tick();f.Move(true,0,true);
    var l=local.RootElement.EnumerateArray().First(x=>x.GetProperty("weapon").GetInt32()==weapon&&x.GetProperty("initialMana").GetInt32()==100&&x.GetProperty("aimDirection").GetInt32()==1);
    var shot=l.GetProperty("shots")[0];
    var packet=new TerrariaProjectileUpdateState(new(0,951,1),shot.GetProperty("type").GetInt32(),
        shot.GetProperty("position").GetProperty("X").GetSingle()+1200,
        shot.GetProperty("position").GetProperty("Y").GetSingle()+1206,6,0,0,0,0,0,
        shot.GetProperty("damage").GetInt16(),shot.GetProperty("knockBack").GetSingle(),0);
    f.State.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection,packet));
    Assert.Equal(1,f.Projectiles.ActiveCount);
    Assert.True(f.Member.ItemPhase!.Value.Channel);
    int paid=f.Member.Mana;
    // A phase between27 and a delayed41 consumes the same prepaid attempt, never pays twice.
    f.State.Tick();Assert.Equal(paid,(int)f.Member.Mana);
    Assert.False(f.Member.ItemPhase!.Value.MagicPayment!.Value.AwaitingPhase);
    Assert.Equal((weapon==113?22:weapon==218?30:25)-1,f.Member.ItemPhase.Value.Selected.Animation);
}
using(var transfer=new Harness.Fixture(currentProfile,false))
{
    transfer.Move(false,0,false);transfer.State.Tick();transfer.Move(true,0,true);transfer.State.Tick();
    int mana=transfer.Member.Mana;
    var detach=new TaskCompletionSource<RuntimePlayerTransferState?>();
    transfer.State.Apply(new PlayerTransferDetachRuntimeCommand(transfer.Connection,detach));
    Assert.True(detach.Task.IsCompletedSuccessfully);Assert.NotNull((await detach.Task));
    var attach=new TaskCompletionSource<bool>();
    transfer.State.Apply(new PlayerTransferAttachRuntimeCommand(transfer.Connection,(await detach.Task)!,100,103,true,false,attach));
    Assert.True(attach.Task.IsCompletedSuccessfully);Assert.True((await attach.Task));
    Assert.Equal(mana,(int)transfer.Member.Mana);
    Assert.False(transfer.Member.ItemPhase!.Value.MagicPayment!.Value.EligibleBirth);
    Assert.True(transfer.Member.ItemPhase.Value.MagicPayment!.Value.BirthConsumed);
    Assert.False(transfer.Member.ItemPhase.Value.MagicPayment!.Value.AwaitingPhase);
}
using(var malformed=new Harness.Fixture(currentProfile,false))
{
    malformed.Move(false,0,false);malformed.State.Tick();malformed.Move(true,0,true);malformed.State.Tick();
    Assert.True(malformed.Players.TryCaptureProjectileUse(malformed.Connection,out var capture));
    var payment=malformed.Member.ItemPhase!.Value.MagicPayment!.Value;
    Assert.False(malformed.Players.CanCommitProjectileUse(capture!,null,0,payment with{Attempt=0}));
    Assert.Equal(86,(int)malformed.Member.Mana);
}
using(var wave=new Harness.Fixture(currentProfile,false))
{
    wave.Move(false,0,false);wave.State.Tick();wave.Move(true,0,true);wave.State.Tick();
    // An unobserved release/press cannot borrow an older unconsumed birth receipt.
    wave.Move(false,0,true);wave.Move(true,0,true);
    var packet=new TerrariaProjectileUpdateState(new(0,971,1),16,1594,1611,6,0,0,0,0,0,35,7.5f,0);
    wave.State.Apply(new ClientProjectileUpdateRuntimeCommand(wave.Connection,packet));
    Assert.Equal(0,wave.Projectiles.ActiveCount);Assert.Equal(86,(int)wave.Member.Mana);
    Assert.False(wave.Member.ItemPhase!.Value.MagicPayment!.Value.EligibleBirth);
}
}
private static JsonDocument ReadSource(string name)
{
 using var stream=typeof(HumanChannelledMagicAuthority1458Tests).Assembly.GetManifestResourceStream(name)!;
 using var gzip=new GZipStream(stream,CompressionMode.Decompress);
 return JsonDocument.Parse(gzip);
}
private static void SetCursed(Harness.Fixture fixture)
{
    var types=new BuffTypeId[44];var times=new int[44];types[0]=new(23);times[0]=60;
    fixture.Profiles.RestoreBuffState(fixture.Connection,PlayerBuffState.FromSlots(types,times));
}
internal sealed class Harness
{
    private static int I(JsonElement e,string n)=>e.GetProperty(n).GetInt32();
    private static float S(JsonElement e,string n)=>e.GetProperty(n).GetSingle();
    internal sealed class Fixture : IDisposable
    {
        private readonly PlayerJoinSession session;
        private readonly string branch;
        private readonly JsonElement spec;
        private readonly PlayerSlotPool pool=new(3);
        private PlayerSlotPool.PlayerSlotLease? gap;
        private PlayerJoinSession? secondSession;
        internal ConnectionHandle SecondConnection;
        internal RuntimePlayerMember SecondMember { get { Assert.True(Players.TryGet(SecondConnection,out var member));return member; } }

        internal readonly ServerRuntimeState State;
        internal readonly PlayerAuthority Players;
        internal readonly ConnectionHandle Connection;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeProjectileReplicationRegistry Replication = new();
        internal readonly RuntimeProjectileStore Projectiles;
        internal RuntimePlayerMember Member
        {
            get { Assert.True(Players.TryGet(Connection, out var member)); return member; }
        }
        internal RuntimePlayerTransferProfileStore Profiles =>
            (RuntimePlayerTransferProfileStore)typeof(PlayerAuthority)
                .GetField("transferProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;

        internal Fixture(JsonElement row, bool windows, IRuntimePlayerEventSink? playerEvents = null)
        {
            Projectiles = new(commitSink: Replication);
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            for (int x = 0; x < 400; x++)
                tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            State = new(playerEvents: playerEvents, worldTiles: tiles, projectiles: Projectiles, projectileReplication: Replication, playerUpdateRandomSeed: new(0));
            Players = ((ServerRuntimeComposition)typeof(ServerRuntimeState)
                .GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(State)!).Players;
            Random = (VanillaUnifiedRandom1458)typeof(PlayerAuthority)
                .GetField("playerUpdateRandom", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;
            spec = row.GetProperty("spec");
            branch = spec.GetProperty("branch").GetString()!;
            Players.SetPlayerUpdateWorldFacts(new RuntimePlayerUpdateWorld1458(400, 300, 80, false, false)
                { WindowsItemPrefixArithmetic = windows });
            Players.SetRemotePlayerEnvironment(new(false, false), Projectiles);
            Assert.True(pool.TryAcquireConnection(out var lease));
            session = new(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(99809), session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            State.Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, (short)I(spec,"currentMana"), (short)I(spec,"baseMana"))));
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            Move(false, 0, false, 1600, 1606);
            int mode = I(spec, "mode");
            bool good = spec.GetProperty("good").GetBoolean();
            Players.SetCombatEquipmentWorldModes(mode > 0 || good, mode >= 2 || mode == 1 && good);
            byte heart = spec.GetProperty("heart").GetBoolean() ? (byte)4 : (byte)0;
            State.Apply(new PlayerAppearanceRuntimeCommand(Connection,
                new(Connection.Player.Slot, 0, 0, 0f, 0, "Defensive", 0, 0, 0,
                    default, default, default, default, default, default, default, heart, 0, 0)));
            State.Apply(new PlayerManaRuntimeCommand(Connection, new(Connection.Player.Slot, (short)I(spec,"currentMana"), (short)I(spec,"baseMana"))));
            Equipment(0, (short)I(spec, "weapon"), (short)1,
                (byte)I(spec, "prefix"));
            Equipment(1, (short)I(spec, "weapon"), 1);
            Equipment(54, 0, 0);
            foreach (var entry in spec.GetProperty("armor").EnumerateArray())
            {
                int loadout = I(entry, "loadout");
                short slot = checked((short)((loadout < 0 ? VanillaPlayerItemSlotCatalog.ArmorStart :
                    VanillaPlayerItemSlotCatalog.LoadoutArmorStart + loadout * VanillaPlayerItemSlotCatalog.LoadoutStride) + I(entry, "slot")));
                Equipment(slot, (short)I(entry, "type"), 1, (byte)I(entry, "prefix"),
                    entry.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0);
            }
            var initial = row.GetProperty("initial");
            var types = initial.GetProperty("buffType").EnumerateArray().Select(x => new BuffTypeId(x.GetInt32())).ToArray();
            var times = initial.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            Profiles.RestoreBuffState(Connection, PlayerBuffState.FromSlots(types, times));
            Member.ItemPhase = Member.ItemPhase!.Value with { ManaHeat = S(initial, "manaHeat"), Mana=Member.ItemPhase.Value.Mana with { Count=I(spec,"count"),Delay=S(spec,"delay") } };
            if(spec.TryGetProperty("mixed",out var mixed)&&mixed.GetBoolean())
            {
                Assert.True(pool.TryAcquireConnection(out gap));Assert.True(pool.TryAcquireConnection(out var secondLease));
                secondSession=new(secondLease!);secondSession.ObserveWorldRequest();secondSession.ObserveSectionRequest();
                SecondConnection=new(GameCommandSourceId.FromConnection(99810),secondSession.Handle);
                State.Apply(new PlayerHealthRuntimeCommand(SecondConnection,new(secondSession.Slot,400,400)));
                State.Apply(new PlayerManaRuntimeCommand(SecondConnection,new(secondSession.Slot,10,200)));
                State.Apply(new PlayerSpawnRuntimeCommand(SecondConnection,secondSession,new(secondSession.Slot,100,103,0,0,0,0,0)));
                MoveSecond(false,false);State.Apply(new PlayerEquipmentRuntimeCommand(SecondConnection,new(SecondConnection.Player.Slot,0,1,0,98,0)));
                State.Apply(new PlayerEquipmentRuntimeCommand(SecondConnection,new(SecondConnection.Player.Slot,54,50,0,97,0)));
            }
        }

        internal void Equipment(short slot, short type, short stack, byte prefix = 0, byte flags = 0) =>
            State.Apply(new PlayerEquipmentRuntimeCommand(Connection,
                new(Connection.Player.Slot, slot, stack, prefix, type, flags)));

        internal void Move(bool use, byte selected, bool success, float? x = null, float? y = null) => State.Apply(new PlayerMovementRuntimeCommand(Connection,
            new(Connection.Player.Slot, (byte)(64 | (use ? 32 : 0)), 16, 0, success ? (byte)64 : (byte)0, selected,
                x ?? Member.PositionX, y ?? Member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));

        private void MoveSecond(bool use,bool success)=>State.Apply(new PlayerMovementRuntimeCommand(SecondConnection,new(SecondConnection.Player.Slot,(byte)(64|(use?32:0)),16,0,success?(byte)64:(byte)0,0,1700,1606,false,0,0,false,0,false,0,0,0,0,false,0,0)));
        internal void PrepareStep(JsonElement step)
        {
            int tick = I(step, "tick");
            if (tick == 1 && branch != "living")
            {
                if (branch == "dead")
                {
                    Member.IsDead = true;
                    Member.NpcHealth = Member.NpcHealth!.Value with { Life = 0 };
                    Member.ItemPhase = Member.ItemPhase!.Value with { DeadTime = 17, RespawnTimer = 500 };
                }
                if (branch == "ghost") Member.MovementFlags |= 64;
                if (branch == "outside") { Member.PositionX = 0; Member.PositionY = 0; }
            }
            if (spec.GetProperty("name").GetString()=="reported-add-remove" && (tick==8 || tick==16))
            {
                var bt = new BuffTypeId[44];var duration = new int[44];
                if(tick==8){bt[0]=new(16);bt[1]=new(117);duration[0]=duration[1]=60;}
                Profiles.RestoreBuffState(Connection,PlayerBuffState.FromSlots(bt,duration));
            }
            if(secondSession is not null)MoveSecond(tick>0&&tick!=5,step.GetProperty("beforeSecond").GetProperty("lastItemUseAttemptSuccess").GetBoolean());
            bool use = branch == "living" && tick > 0 && tick != 5;
            Move(use, (byte)(tick >= 3 && branch == "living" ? 1 : 0),
                step.GetProperty("beforePlayer").GetProperty("lastItemUseAttemptSuccess").GetBoolean());
            if (branch == "ghost" && tick > 0) Member.MovementFlags |= 64;
        }

        public void Dispose() {session.Dispose();secondSession?.Dispose();gap?.Dispose();}
    }
}

}
