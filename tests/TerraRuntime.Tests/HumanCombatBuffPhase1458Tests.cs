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
namespace TerraRuntime.Tests;
public sealed class HumanCombatBuffPhase1458Tests
{
    [Fact]
    public void Original_indexed_Archery_and_Wrath_refresh_complete_living_player_phases()
        => Compare(row => !new[] { "dead-both", "ghost-both", "outside-both", "mixed-both-neutral" }.Contains(row.GetProperty("spec").GetProperty("name").GetString()), 1280);

    [Fact]
    public void Original_combat_buff_early_branches_and_mixed_census_match_retained_fields_and_random()
        => Compare(row => new[] { "dead-both", "ghost-both", "outside-both", "mixed-both-neutral" }.Contains(row.GetProperty("spec").GetProperty("name").GetString()), 256);

    private static void Compare(Func<JsonElement,bool> include,int expectedCount)
    {
        using var source=Read();int compared=0;
        foreach(string platform in new[]{"linux","windows"})
        foreach(var row in source.RootElement.GetProperty(platform).EnumerateArray().Where(include))
        {
            using var f=new Fixture(row,platform=="windows");
            foreach(var step in row.GetProperty("steps").EnumerateArray())
            {
                f.PrepareStep(step);AssertRandom(step.GetProperty("before"),f.Random);
                f.State.Tick();Assert.True(f.Member.ItemPhase is not null, $"Phase retired: {platform}/{row.GetProperty("spec").GetProperty("name").GetString()}/{I(step,"tick")}");AssertPlayer(step.GetProperty("player"),f);
                if(step.GetProperty("second").ValueKind!=JsonValueKind.Null)AssertPlayer(step.GetProperty("second"),f,true);
                AssertRandom(step.GetProperty("after"),f.Random);Assert.Equal(0,f.Projectiles.ActiveCount);
                foreach(var item in step.GetProperty("inventory").EnumerateArray())
                {
                    Assert.True(f.Players.TryGetInventoryItem(f.Connection,(short)I(item,"index"),out var actual));
                    Assert.Equal(I(item,"type"),actual.ItemType.Value);Assert.Equal(I(item,"stack"),actual.Stack);
                    Assert.Equal(I(item,"prefix"),actual.Prefix.Value);
                }
                compared++;
            }
        }
        Assert.Equal(expectedCount,compared);
    }

    [Fact]
    public void Original_local_bow_consumer_cap_and_late_indexed_buff_currentness_remain_owned()
    {
        using var launchSource=ReadNamed("HumanCombatBuffShoot1458");int launches=0;
        foreach(string platform in new[]{"linux","windows"})
        foreach(var row in launchSource.RootElement.GetProperty(platform).EnumerateArray())
        {
            if(I(row,"weapon")!=39)continue;
            var combat=VanillaPlayerCombatSnapshot.Baseline with { ArrowDamage=S(row,"arrowDamage"),
                RangedDamage=S(row,"rangedDamage"),MagicDamage=S(row,"magicDamage"),
                MinionDamage=S(row,"minionDamage"),Archery=row.GetProperty("archery").GetBoolean() };
            Assert.True(VanillaOrdinaryBowLaunch1458.TryResolve(new(39),new(0),new(40),in combat,out var launch,
                platform=="windows"?VanillaBulletSourceArithmetic1458.WindowsClr4X86:VanillaBulletSourceArithmetic1458.CoreClrSingle));
            var shot=row.GetProperty("shots")[0];
            Assert.Equal(I(shot,"damage"),launch.Damage);Assert.Equal(S(shot,"knockBack"),launch.KnockBack);
            Assert.Equal(MathF.Abs(F(shot,"velocity","X")),launch.Speed);launches++;
        }
        using(var phaseSource=Read())
        foreach(string platform in new[]{"linux","windows"})
        foreach(var row in launchSource.RootElement.GetProperty(platform).EnumerateArray())
        {
            string suffix=I(row,"buffProfile") switch {0=>"neutral",1=>"archery",2=>"wrath",_=>"both"};
            string name=I(row,"buffProfile")==0?"none-neutral":I(row,"weapon")+"-"+suffix;
            var phase=phaseSource.RootElement.GetProperty(platform).EnumerateArray().Single(r=>r.GetProperty("spec").GetProperty("name").GetString()==name);
            using var f=new Fixture(phase,platform=="windows");
            f.Equipment(0,(short)I(row,"weapon"),1);f.Equipment(54,(short)I(row,"ammo"),20);
            f.PrepareStep(phase.GetProperty("steps")[0]);f.State.Tick();
            f.Move(true,0,true);
            var shot=row.GetProperty("shots")[0];var random=new VanillaUnifiedRandom1458(0);
            var lookup=new RuntimePlayerSnapshotLookup(f.Players,null);
            var replication=new RuntimeProjectileReplicationRegistry();
            var store=new RuntimeProjectileStore(commitSink:replication);
            var authority=new ProjectileAuthority(store,f.Players,new RuntimeNpcStore(),lookup,null,replication,()=>0,projectileRandom:random);
            var packet=new TerrariaProjectileUpdateState(new(0,0,0),(short)I(shot,"type"),f.Member.PositionX+F(shot,"position","X")-400f,f.Member.PositionY+F(shot,"position","Y")-400f,F(shot,"velocity","X"),F(shot,"velocity","Y"),0,0,0,0,(short)I(shot,"damage"),S(shot,"knockBack"),0);
            Assert.True(f.Players.TryCaptureProjectileCombatSnapshot(f.Connection.Player,out var ownedCombat));
            Assert.True(f.Players.TryCaptureProjectileUse(f.Connection,out var captured));Assert.True(f.Players.IsCurrentPlainBowProjectilePose(captured!));

            Assert.True(authority.TryApply(new ClientProjectileUpdateRuntimeCommand(f.Connection,packet)));
            Assert.True(authority.PromotedClientProjectileSpawns==1,$"Connected source: {platform}/{I(row,"weapon")}/{suffix}/{I(row,"aimDirection")}: rejected={authority.RejectedClientProjectileProvenance}, client={authority.RejectedClientUpdates}, spawns={authority.AppliedSpawns}");
            var bodies=new ProjectileSnapshot[store.Capacity];Assert.Equal(1,store.CopyActive(bodies));Assert.True(store.IsCombatTrusted(bodies[0].Handle));
            Assert.Equal(packet.Damage,bodies[0].Damage);Assert.Equal(packet.VelocityX,bodies[0].VelocityX);Assert.Equal(packet.VelocityY,bodies[0].VelocityY);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection,54,out var remaining));Assert.Equal(I(row.GetProperty("ammoAfter"),"stack"),remaining.Stack);
            AssertRandom(row.GetProperty("after"),random);Assert.Equal(I(row,"nextAfter"),random.Clone().Next());
            var after=random.Clone();Assert.True(authority.TryApply(new ClientProjectileUpdateRuntimeCommand(f.Connection,packet with { Key=new(0,1,0) })));
            Assert.Equal(1,authority.PromotedClientProjectileSpawns);Assert.True(random.HasSameState(after));
        }
        Assert.Equal(16,launches);
        using var cap=ReadNamed("ArcheryPickAmmoCap1458");int caps=0;
        foreach(string platform in new[]{"linux","windows"})
        foreach(var row in cap.RootElement.GetProperty(platform).EnumerateArray())
        {
            float before=S(row,"inputSpeed")+S(row.GetProperty("ammoAfter"),"shootSpeed");
            Assert.Equal(S(row,"outputSpeed"),VanillaOrdinaryBowLaunch1458.ResolveArcherySpeed(before,row.GetProperty("actualArchery").GetBoolean()));caps++;
        }
        Assert.Equal(60,caps);
        using var source=Read();var profile=source.RootElement.GetProperty("linux").EnumerateArray().Single(r=>r.GetProperty("spec").GetProperty("name").GetString()=="none-both");
        foreach(bool mutateDuration in new[]{false,true})
        {
            using var f=new Fixture(profile,false);f.PrepareStep(profile.GetProperty("steps")[0]);
            var before=f.Member.ItemPhase;var random=f.Random.Clone();bool once=false;
            f.Players.SetNpcHealthWorldFacts(()=>{
                if(!once){once=true;var types=new BuffTypeId[44];var times=new int[44];types[0]=new(16);types[1]=new(117);times[0]=mutateDuration?59:60;times[1]=60;
                    if(!mutateDuration)types[0]=new(0);f.Profiles.RestoreBuffState(f.Connection,PlayerBuffState.FromSlots(types,times));}
                return new(false,false);
            });
            Assert.False(f.Players.TryTickRemotePlayerPhase());Assert.True(once);Assert.Equal(before,f.Member.ItemPhase);Assert.True(f.Random.HasSameState(random));
        }
        // Imported-record safety; this is not a canonical original normal-profile case.
        using(var overflow=new Fixture(profile,false))
        {
            overflow.Member.ItemPhase=overflow.Member.ItemPhase!.Value with { DerivedCombat=VanillaPlayerCombatSnapshot.Baseline with { RangedDamage=float.MaxValue } };
            overflow.Member.PositionX=overflow.Member.PositionY=0;var before=overflow.Member.ItemPhase;var random=overflow.Random.Clone();
            Assert.False(overflow.Players.TryTickRemotePlayerPhase());Assert.Equal(before,overflow.Member.ItemPhase);Assert.True(overflow.Random.HasSameState(random));
        }
        // The existing indexed owner exposes only positive-duration active slots to the effect body.
        using(var inactive=new Fixture(profile,false))
        {
            var types=new BuffTypeId[44];types[0]=new(16);types[1]=new(117);
            inactive.Profiles.RestoreBuffState(inactive.Connection,PlayerBuffState.FromSlots(types,new int[44]));
            Assert.True(inactive.Players.TryTickRemotePlayerPhase());var combat=inactive.Member.ItemPhase!.Value.DerivedCombat!.Value;
            Assert.False(combat.Archery);Assert.Equal(1f,combat.ArrowDamage);Assert.Equal(1f,combat.RangedDamage);Assert.Equal(1f,combat.MinionDamage);
        }
        using(var unknown=new Fixture(profile,false))
        {
            unknown.Member.ItemPhase=unknown.Member.ItemPhase!.Value with { DerivedCombat=null };
            unknown.Member.PositionX=unknown.Member.PositionY=0;
            var random=unknown.Random.Clone();Assert.False(unknown.Players.TryTickRemotePlayerPhase());Assert.True(unknown.Random.HasSameState(random));
        }
    }

    private static JsonDocument ReadNamed(string name)
    {
        using var stream=typeof(HumanCombatBuffPhase1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);return JsonDocument.Parse(gzip);
    }

    private static VanillaPlayerCombatSnapshot Combat(JsonElement player) => new(
        I(player, "statDefense"), S(player, "endurance"), S(player, "meleeDamage"),
        S(player, "rangedDamage"), S(player, "magicDamage"), S(player, "rangedMultDamage"),
        S(player, "arrowDamage"), S(player, "arrowDamageAdditiveStack"), S(player, "bulletDamage"),
        I(player, "meleeCrit"), I(player, "rangedCrit"), I(player, "magicCrit"), S(player, "meleeSpeed"),
        I(player, "armorPenetration"), I(player, "meleeArmorPenetration"),
        player.GetProperty("noKnockback").GetBoolean(), player.GetProperty("magicQuiver").GetBoolean())
    {
        LavaProtectionTicks = I(player, "lavaMax"),
        LavaRose = player.GetProperty("lavaRose").GetBoolean(),
        WaterWalk = player.GetProperty("waterWalk").GetBoolean(),
        Archery = player.GetProperty("archery").GetBoolean(),
        MinionDamage = S(player,"minionDamage")
    };

    private static void AssertPlayer(JsonElement expected, Fixture f, bool second=false)
    {
        var member = second ? f.SecondMember : f.Member;
        var item = member.ItemPhase!.Value;
        Assert.Equal(Combat(expected), item.DerivedCombat);
        Assert.Equal(I(expected, "statLife"), member.CaptureSnapshot().NpcLife);
        Assert.Equal(I(expected, "statLifeMax2"), member.DerivedLifeMax);
        Assert.Equal(I(expected, "lifeRegenCount"), member.NpcHealth!.Value.RegenCount);
        Assert.Equal(S(expected, "lifeRegenTime"), member.NpcHealth.Value.RegenTime);
        Assert.Equal(I(expected, "statMana"), member.Mana);
        Assert.Equal(I(expected, "statManaMax2"), item.Mana.Maximum);
        Assert.Equal(I(expected, "manaRegenCount"), item.Mana.Count);
        Assert.Equal(S(expected, "manaRegenDelay"), item.Mana.Delay);
        Assert.Equal(S(expected, "manaHeat"), item.ManaHeat);
        Assert.Equal(I(expected, "itemAnimation"), item.Selected.Animation);
        Assert.Equal(I(expected, "itemAnimationMax"), item.Selected.AnimationMax);
        Assert.Equal(I(expected, "itemTime"), item.Selected.ItemTime);
        Assert.Equal(I(expected, "itemTimeMax"), item.Selected.ItemTimeMax);
        Assert.Equal(I(expected, "potionDelay"), item.Selected.PotionDelay);
        Assert.Equal(I(expected, "manaPotionDelay"), item.ManaPotionDelay);
        Assert.Equal(I(expected, "toolTime"), item.ToolTime);
        Assert.Equal(I(expected, "attackCD"), item.AttackCD);
        Assert.Equal(I(expected, "deadTime"), item.DeadTime);
        Assert.Equal(I(expected, "respawnTimer"), item.RespawnTimer);
        Assert.Equal(expected.GetProperty("releaseUseItem").GetBoolean(), item.Selected.ReleaseUseItem);
        Assert.Equal(expected.GetProperty("pendingItemReuse").GetBoolean(), item.PendingItemReuse);
        Assert.Equal(I(expected, "selectedItem"), member.SelectedItem);
        Assert.Equal(S(expected, "itemRotation"), member.ItemRotation);
        Assert.Equal(F(expected, "position", "X"), member.PositionX);
        Assert.Equal(F(expected, "position", "Y"), member.PositionY);
        Assert.Equal(F(expected, "velocity", "X"), member.VelocityX);
        Assert.Equal(F(expected, "velocity", "Y"), member.VelocityY);
        Assert.Equal(I(expected, "jump"), member.PhysicsPhase!.Value.Jump.RemainingTicks);
        Assert.Equal(I(expected, "wingTime"), member.PhysicsPhase.Value.Jump.WingTime);
        Assert.Equal(expected.GetProperty("releaseJump").GetBoolean(), member.PhysicsPhase.Value.Jump.ReleaseReady);
        f.Profiles.CaptureBuffState(second ? f.SecondConnection : f.Connection)!.CaptureSlots(out var types, out var times);
        Assert.Equal(expected.GetProperty("buffType").EnumerateArray().Select(x => x.GetInt32()), types.Select(x => x.Value));
        Assert.Equal(expected.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()), times);
    }

    private static JsonDocument Read()
    {
        using var stream = typeof(HumanCombatBuffPhase1458Tests).Assembly.GetManifestResourceStream("HumanCombatBuffPhase1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static int I(JsonElement row, string field) => row.GetProperty(field).GetInt32();
    private static float S(JsonElement row, string field) => row.GetProperty(field).GetSingle();
    private static float F(JsonElement row, string field, string component) => row.GetProperty(field).GetProperty(component).GetSingle();

    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),
            typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random));
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
    }

    private sealed class Fixture : IDisposable
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
        internal readonly RuntimeProjectileStore Projectiles = new();
        internal RuntimePlayerMember Member
        {
            get { Assert.True(Players.TryGet(Connection, out var member)); return member; }
        }
        internal RuntimePlayerTransferProfileStore Profiles =>
            (RuntimePlayerTransferProfileStore)typeof(PlayerAuthority)
                .GetField("transferProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;

        internal Fixture(JsonElement row, bool windows)
        {
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            for (int x = 0; x < 400; x++)
                tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            State = new(worldTiles: tiles, projectiles: Projectiles, playerUpdateRandomSeed: new(0));
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
            Equipment(0, (short)I(spec, "weapon"), row.GetProperty("initial").GetProperty("lastItemUseAttemptSuccess").GetBoolean() ? (short)20 : (short)1,
                (byte)I(spec, "prefix"));
            Equipment(1, (short)I(spec, "weapon"), 1);
            Equipment(54, (short)(I(spec,"weapon")==39?40:97), 50);
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
