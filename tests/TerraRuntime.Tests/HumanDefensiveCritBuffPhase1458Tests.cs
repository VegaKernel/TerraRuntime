using TerraRuntime.Gameplay.Projectiles;
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
using TerraRuntime.Network;
using TerraRuntime.Core.Worlds;
namespace TerraRuntime.Tests;
public sealed class HumanDefensiveCritBuffPhase1458Tests
{
    [Fact]
    public void Original_living_indexed_defense_endurance_and_crit_writers_match_complete_phases()
        => Compare(early:false, expectedCount:1344);

    [Fact]
    public void Original_early_branches_and_mixed_census_preserve_exact_unclamped_Endurance_and_owned_fields()
        => Compare(early:true, expectedCount:704);

    private static void Compare(bool early,int expectedCount)
    {
        using var source=Read();int compared=0;
        foreach(string platform in new[]{"linux","windows"})
        foreach(var row in source.RootElement.GetProperty(platform).EnumerateArray())
        {
            string name=row.GetProperty("spec").GetProperty("name").GetString()!;
            bool isEarly=name.StartsWith("dead-")||name.StartsWith("ghost-")||name.StartsWith("outside-")||name.StartsWith("mixed-");
            if(isEarly!=early)continue;
            using var f=new Fixture(row,platform=="windows");
            foreach(var step in row.GetProperty("steps").EnumerateArray())
            {
                f.PrepareStep(step);AssertRandom(step.GetProperty("before"),f.Random);
                f.State.Tick();Assert.True(f.Member.ItemPhase is not null,$"Retired: {platform}/{name}/{I(step,"tick")}");
                AssertPlayer(step.GetProperty("player"),f);
                if(step.GetProperty("second").ValueKind!=JsonValueKind.Null)AssertPlayer(step.GetProperty("second"),f,true);
                AssertRandom(step.GetProperty("after"),f.Random);Assert.Equal(0,f.Projectiles.ActiveCount);
                foreach(var item in step.GetProperty("inventory").EnumerateArray())
                {
                    Assert.True(f.Players.TryGetInventoryItem(f.Connection,(short)I(item,"index"),out var actual));
                    Assert.Equal(I(item,"type"),actual.ItemType.Value);Assert.Equal(I(item,"stack"),actual.Stack);Assert.Equal(I(item,"prefix"),actual.Prefix.Value);
                }
                compared++;
            }
        }
        Assert.Equal(expectedCount,compared);
    }

    [Fact]
    public void Indexed_duration_callback_mutation_and_imported_integer_overflow_refuse_without_owner_adoption()
    {
        CompareOriginalHits();
        CompareOriginalContinuations();
        using var source=Read();var row=source.RootElement.GetProperty("linux").EnumerateArray().Single(x=>x.GetProperty("spec").GetProperty("name").GetString()=="all");
        foreach(bool durationOnly in new[]{false,true})
        {
            using var f=new Fixture(row,false);f.PrepareStep(row.GetProperty("steps")[0]);
            var phase=f.Member.ItemPhase;var random=f.Random.Clone();bool once=false;
            f.Players.SetNpcHealthWorldFacts(()=>{
                if(!once){once=true;var types=new BuffTypeId[44];var times=new int[44];int[] ids={5,114,115,321};
                    for(int j=0;j<ids.Length;j++){types[j]=new(ids[j]);times[j]=60;}
                    if(durationOnly)times[0]=59;else types[0]=new(0);
                    f.Profiles.RestoreBuffState(f.Connection,PlayerBuffState.FromSlots(types,times));}
                return new(false,false);
            });
            Assert.False(f.Players.TryTickRemotePlayerPhase());Assert.True(once);Assert.Equal(phase,f.Member.ItemPhase);Assert.True(f.Random.HasSameState(random));
        }
        // Imported-record overflow is a bounded runtime policy, not a source packet claim.
        foreach(bool defense in new[]{false,true})
        {
            using var f=new Fixture(row,false);f.Member.PositionX=f.Member.PositionY=0;
            f.Member.ItemPhase=f.Member.ItemPhase!.Value with {DerivedCombat=VanillaPlayerCombatSnapshot.Baseline with {Defense=defense?int.MaxValue:0,MeleeCrit=defense?4:int.MaxValue}};
            var phase=f.Member.ItemPhase;var random=f.Random.Clone();Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.Equal(phase,f.Member.ItemPhase);Assert.True(f.Random.HasSameState(random));
        }
        using(var f=new Fixture(row,false))
        {
            var types=new BuffTypeId[44];int[] ids={5,114,115,321};for(int j=0;j<ids.Length;j++)types[j]=new(ids[j]);
            f.Profiles.RestoreBuffState(f.Connection,PlayerBuffState.FromSlots(types,new int[44]));
            Assert.True(f.Players.TryTickRemotePlayerPhase());Assert.Equal(VanillaPlayerCombatSnapshot.Baseline,f.Member.ItemPhase!.Value.DerivedCombat);
        }
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
        using var stream = typeof(HumanDefensiveCritBuffPhase1458Tests).Assembly.GetManifestResourceStream("HumanDefensiveCritBuffPhase1458")!;
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
                if(tick==8){int[] ids={5,114,115,321};for(int j=0;j<ids.Length;j++){bt[j]=new(ids[j]);duration[j]=60;}}
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
    private static void CompareOriginalHits()
    {
        using var stream=typeof(HumanDefensiveCritBuffPhase1458Tests).Assembly.GetManifestResourceStream("HumanDefensiveCritBuffHit1458")!;
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);using var hits=JsonDocument.Parse(gzip);
        using var phases=Read();var neutral=phases.RootElement.GetProperty("linux").EnumerateArray().Single(x=>x.GetProperty("spec").GetProperty("name").GetString()=="neutral");int compared=0;
        foreach(string platform in new[]{"linux","windows"})
        foreach(var row in hits.RootElement.GetProperty(platform).EnumerateArray())
        {
            using var f=new Fixture(neutral,platform=="windows");var spec=row.GetProperty("spec");
            int weapon=spec.TryGetProperty("WeaponType",out var wt)?wt.GetInt32():219;int ammo=spec.TryGetProperty("AmmoType",out var at)?at.GetInt32():97;
            f.Equipment(0,(short)weapon,1,(byte)I(spec,"Prefix"));f.Equipment(1,(short)weapon,1);f.Equipment(54,(short)ammo,20);
            Restore(spec.GetProperty("Buffs"));f.State.Tick();AssertCrit(row.GetProperty("phaseCrit"));
            if(spec.GetProperty("AfterBuffs").ValueKind!=JsonValueKind.Null)Restore(spec.GetProperty("AfterBuffs"));
            if(spec.GetProperty("Switch").GetBoolean())f.Move(false,1,false);
            if(spec.GetProperty("Refresh").GetBoolean())f.State.Tick();AssertCrit(row.GetProperty("beforeHitCrit"));
            using var arena=new Arena(f,I(row,"hitSeed"),row.GetProperty("launch"),I(row,"npcTypeActual"));arena.Pass.Tick();
            Assert.True(arena.Pass.CommittedHits==1,$"Source hit refused: {platform}/{row.GetProperty("mode").GetString()}/{I(row,"hitSeed")}");Assert.True(arena.Npcs.TryGet(arena.Actor.Handle,out var actor));
            Assert.Equal(I(row,"npcLife"),actor.Simulation.Life);AssertRandom(row.GetProperty("hitRng"),arena.Random);
            var sourceRandom=row.GetProperty("hitRng");Assert.Equal(sourceRandom.TryGetProperty("next",out var next)?next.GetInt32():I(sourceRandom,"publicNext"),arena.Random.Clone().Next());
            Assert.Equal(row.GetProperty("keep").GetBoolean(),f.Projectiles.TryGet(arena.Shot.Handle,out _));
            // The Windows source supplement does not capture literal transport frames.
            if(platform=="linux")Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(x=>x.GetString()),arena.Drain().Where(x=>x[2]==28).Select(Convert.ToHexString));
            compared++;
            void Restore(JsonElement ids){var types=new BuffTypeId[44];var times=new int[44];int j=0;foreach(var id in ids.EnumerateArray()){types[j]=new(id.GetInt32());times[j++]=60;}f.Profiles.RestoreBuffState(f.Connection,PlayerBuffState.FromSlots(types,times));}
            void AssertCrit(JsonElement expected){var combat=f.Member.ItemPhase!.Value.DerivedCombat!.Value;Assert.Equal(I(expected,"melee"),combat.MeleeCrit);Assert.Equal(I(expected,"ranged"),combat.RangedCrit);Assert.Equal(I(expected,"magic"),combat.MagicCrit);Assert.Equal(S(expected,"minionDamage"),combat.MinionDamage);}
        }
        Assert.Equal(72,compared);
    }

    // Remote phase owns the combat projection; the configured local Update reference is
    // a separate component invocation, not a full client/server frame or Shoot producer.
    private static void CompareOriginalContinuations()
    {
        using var stream=typeof(HumanDefensiveCritBuffPhase1458Tests).Assembly.GetManifestResourceStream("HumanBuffedArrowUpdate1458")!;
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);using var source=JsonDocument.Parse(gzip);int compared=0;
        foreach(var row in source.RootElement.GetProperty("rows").EnumerateArray())
        {
            using var f=new HumanBuffedArrowUpdateSupport1458.Fixture(I(row,"seed"),I(row,"shotType"),row);
            Assert.True(f.Players.TryApply(new PlayerAppearanceRuntimeCommand(f.Connection,new(f.Connection.Player.Slot,0,0,0f,0,"Arrow",0,0,0,default,default,default,default,default,default,default,0,0,0))));
            f.Players.SetPlayerUpdateRandom(new VanillaUnifiedRandom1458(1458));
            f.Players.SetPlayerUpdateWorldFacts(new RuntimePlayerUpdateWorld1458(400,300,80,false,false));
            f.Players.SetRemotePlayerEnvironment(new(false,false),f.Shots);
            var profiles=(RuntimePlayerTransferProfileStore)typeof(PlayerAuthority).GetField("transferProfiles",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(f.Players)!;
            var types=new BuffTypeId[44];var times=new int[44];int index=0;
            foreach(var id in row.GetProperty("buffProfile").GetProperty("Ids").EnumerateArray()){types[index]=new(id.GetInt32());times[index++]=60;}
            profiles.RestoreBuffState(f.Connection,PlayerBuffState.FromSlots(types,times));
            Assert.True(f.Players.TryGet(f.Connection,out var member));
            Assert.True(f.Players.TryTickRemotePlayerPhase());
            var phase=row.GetProperty("phase");var combat=member.ItemPhase!.Value.DerivedCombat!.Value;
            Assert.Equal(I(phase,"rangedCrit"),combat.RangedCrit);Assert.Equal(I(phase,"meleeCrit"),combat.MeleeCrit);Assert.Equal(I(phase,"magicCrit"),combat.MagicCrit);
            Assert.Equal(I(phase,"defense"),combat.Defense);Assert.Equal(S(phase,"endurance"),combat.Endurance);Assert.Equal(S(phase,"minionDamage"),combat.MinionDamage);Assert.Equal(phase.GetProperty("archery").GetBoolean(),combat.Archery);
            AssertRandom(row.GetProperty("beforeRandom"),f.Random);
            var authority=new ProjectileAuthority(f.Shots,f.Players,f.Npcs,new RuntimePlayerSnapshotLookup(f.Players,null),new VanillaProjectileWorldStateStepper(f.Tiles),f.ProjectileRegistry,()=>100,worldTiles:f.Tiles,projectileRandom:f.Random,npcReplication:f.NpcRegistry);
            Assert.True(authority.TryTickState(f.Pass));CompareContinuation(f,row,$"{I(row,"shotType")}/{row.GetProperty("buffProfile").GetProperty("Name").GetString()}/{I(row,"seed")}");
            // Source configured shots have key generation0; managed runtime uses its own
            // genuine generation1. Literal NPC28 is independent of that projectile key.
            Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(x=>x.GetString()).Where(x=>Convert.FromHexString(x!)[2]==28),f.Drain().Where(x=>x[2]==28).Select(Convert.ToHexString));
            var random=f.Random.Clone();f.Pass.Tick();Assert.True(f.Random.HasSameState(random));Assert.Empty(f.Drain());compared++;
        }
        Assert.Equal(12,compared);
    }

    private static void CompareContinuation(HumanBuffedArrowUpdateSupport1458.Fixture f, JsonElement row, string label)
    {
        Assert.True(f.Shots.TryGet(f.Shot.Handle, out var actual), label + "/active");
        var expected = row.GetProperty("after");
        Assert.True(actual.PositionX == expected.GetProperty("position").GetProperty("X").GetSingle(), label + "/x");
        Assert.True(actual.PositionY == expected.GetProperty("position").GetProperty("Y").GetSingle(), label + "/y");
        Assert.True(actual.VelocityX == expected.GetProperty("velocity").GetProperty("X").GetSingle(), label + "/vx");
        Assert.True(actual.VelocityY == expected.GetProperty("velocity").GetProperty("Y").GetSingle(), label + "/vy");
        Assert.True(actual.Damage == expected.GetProperty("damage").GetInt32(), label + "/damage");
        for (int i = 0; i < 3; i++)
        {
            float a = i == 0 ? actual.Ai.Ai0 : i == 1 ? actual.Ai.Ai1 : actual.Ai.Ai2;
            Assert.True(BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(expected.GetProperty("ai")[i].GetSingle()), label + "/ai" + i);
        }
        Assert.True(f.Shots.TryGetLifecycle(actual.Handle, out var lifecycle), label + "/lifecycle");
        Assert.True(lifecycle.TimeLeft == expected.GetProperty("timeLeft").GetInt32(), label + "/time");
        Assert.True(lifecycle.OldVelocityX == expected.GetProperty("oldVelocity").GetProperty("X").GetSingle() &&
            lifecycle.OldVelocityY == expected.GetProperty("oldVelocity").GetProperty("Y").GetSingle(), label + "/oldVelocity");
        for (int i = 0; i < 3; i++)
        {
            float value = i == 0 ? lifecycle.LocalAi.Ai0 : i == 1 ? lifecycle.LocalAi.Ai1 : lifecycle.LocalAi.Ai2;
            Assert.True(BitConverter.SingleToInt32Bits(value) == BitConverter.SingleToInt32Bits(expected.GetProperty("localAi")[i].GetSingle()), label + "/localAi" + i);
        }
        Assert.True(VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(actual.Type, out var penetration), label + "/penMetadata");
        Assert.True((lifecycle.PenetrateOverride ?? penetration) == expected.GetProperty("penetration").GetInt32(), label + "/pen");
        var buffer = new NpcSnapshot[f.Npcs.Capacity];
        int count = f.Npcs.CopyActive(buffer);
        Assert.True(buffer.Take(count).Select(n => n.Simulation.Life).SequenceEqual(row.GetProperty("targetsAfter").EnumerateArray().Select(t => t.GetProperty("life").GetInt32())), label + "/HP");
        AssertRandom(row.GetProperty("afterRandom"),f.Random);
        Assert.Equal(I(row.GetProperty("afterRandom"),"next"),f.Random.Clone().Next());
    }

    private sealed class Arena : IDisposable
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly NpcSnapshot Actor;
        internal readonly ProjectileSnapshot Shot;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeNpcNetworkCombatPipeline Combat;
        internal readonly RuntimeProjectileNpcCombatPass Pass;
        internal Action? BeforeTick { get; set; }
        internal Action? DuringLoot { get; set; }
        private readonly TerrariaConnectionOutboundQueue outbound = new(new OutboundQueueOptions(128, 131072, 1024));

        internal Arena(Fixture f, int seed, JsonElement launch, int npcType = 1,
            int life = 1000)
        {
            Random = new(seed);
            var registry = new RuntimeNpcReplicationRegistry();
            Npcs = new(capacity: 8, commitSink: registry);
            var actor = new NpcStateUpdate(npcType, (short)npcType, 1800, 1606, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = life, LifeMax = 1000, MoneyValue = 0,
                    ExtraMoneyValue = 0, Immortal = false, ShimmerTransparency = 0 });
            Assert.True(Npcs.TrySpawn(0, actor, out Actor));
            var items = new RuntimeWorldItemStore();
            var lookup = new RuntimePlayerSnapshotLookup(f.Players, null);
            Combat = new(Npcs, items, lookup, f.Players, () => 100, registry, new(items), null,
                new(1000, false, default, 0, 0), new(), false, false, lootRandom: Random,
                seasonalItemContext: () => default,
                npcSpecificLowTiles: () => { DuringLoot?.Invoke(); return false; });
            var status = new RuntimeNpcBuffStatus1458(Npcs, registry.PublishNpcBuffs);
            registry.BindNpcBuffStatus(status);
            var velocity = launch.GetProperty("velocity");
            var shot = new ProjectileStateUpdate(new(launch.GetProperty("type").GetInt32()), 0,
                1800, 1606, velocity.GetProperty("X").GetSingle(), velocity.GetProperty("Y").GetSingle(),
                default, 0, checked((short)launch.GetProperty("damage").GetInt32()), launch.GetProperty("knockBack").GetSingle(),
                checked((short)launch.GetProperty("originalDamage").GetInt32()));
            Assert.True(f.Projectiles.TrySpawn(0, shot, out Shot));
            Assert.True(f.Projectiles.TryMarkCombatTrusted(Shot.Handle, f.Connection.Player));
            Pass = new(f.Projectiles, Npcs, Combat, f.Players,
                () => { BeforeTick?.Invoke(); return 100; }, status: status);
            Assert.True(registry.TryRegister(f.Connection.Source, outbound));
            registry.PlayerSpawned(f.Connection, new(f.Connection.Player.Slot, 100, 103, 0, 0, 0, 0, 0));
            _ = Drain();
        }

        internal byte[][] Drain()
        {
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetField("_queue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(outbound)!;
            var frames = new List<byte[]>();
            while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
            return frames.ToArray();
        }

        public void Dispose() { }
    }
}

internal static class HumanBuffedArrowUpdateSupport1458
{
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Fixture setup failed"); }
    private static NpcStateUpdate Actor(int type = 3, int life = 45) => new(type, (short)type, 1818, 1600, 0, 0, 0, default,
        NpcSimulationState.Initial with
        {
            Life = life,
            LifeMax = life,
            Immortal = false,
            MoneyValue = 0,
            ExtraMoneyValue = 0,
            ShimmerTransparency = 0
        });
    private static ProjectileStateUpdate Shot(int type) => new(new(type), 0, 1808, 1600, 8, 0, default, 0, 40, 0, 0);
    private static PlayerMovementCommitRequest Movement(PlayerSlotId slot) => new(slot, 0, 0, 0, 0, 0,
        1800, 1600, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0);

    internal sealed class Fixture : IDisposable
    {
        private readonly PlayerJoinSession session;
        private readonly TerrariaConnectionOutboundQueue outbound = new(new OutboundQueueOptions(128, 131072, 1024));
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly WorldTileStore Tiles;
        internal readonly RuntimeNpcReplicationRegistry NpcRegistry;
        internal readonly RuntimeProjectileReplicationRegistry ProjectileRegistry;
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeProjectileStore Shots;
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly PlayerAuthority Players;
        internal readonly RuntimeNpcBuffStatus1458 Status;
        internal readonly RuntimeProjectileNpcCombatPass Pass;
        internal readonly RuntimeNpcNetworkCombatPipeline Combat;
        internal readonly NpcSnapshot Actor;
        internal readonly ProjectileSnapshot Shot;
        internal readonly ConnectionHandle Connection;
        internal Action? BeforeTick = null;
        internal Action? OnStatus = null;
        internal Action? OnNpcCommit = null;

        internal Fixture(int seed, int projectileType, JsonElement row, int npcType = 3, int life = 1000)
        {
            Random = new(seed);
            var registry = NpcRegistry = new RuntimeNpcReplicationRegistry();
            var projectileRegistry = ProjectileRegistry = new RuntimeProjectileReplicationRegistry();
            Shots = new(commitSink: projectileRegistry);
            Npcs = new(commitSink: new HumanBuffedArrowObserverSink1458(registry, () => OnNpcCommit?.Invoke()));
            Tiles = new WorldTileStore(new WorldDimensions(400, 300));
            var tiles = Tiles;
            for (int x = 0; x < 400; x++) tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Players = new PlayerAuthority(null, tiles);
            var lookup = new RuntimePlayerSnapshotLookup(Players, null);
            var slots = new PlayerSlotPool(1);
            Require(slots.TryAcquireConnection(out var lease));
            session = new(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(7272), session.Handle);
            Players.TryApply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            Players.TryApply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 200, 200)));
            Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 51, 27, 0, 0, 0, 0, 0)));
            Players.TryApply(new PlayerMovementRuntimeCommand(Connection, Movement(session.Slot)));
            Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection,
                new(session.Slot, VanillaPlayerItemSlotCatalog.AmmoSlotStart, 5, 0, 97, 0)));
            // This fixture binds the whole remote phase before its first health update.
            // The old unbound component TickHealthContext retires health provenance.
            var first = row.GetProperty("targetsBefore")[0];
            var initial = HumanBuffedArrowUpdateSupport1458.Actor(npcType, life) with
            {
                PositionX = first.GetProperty("position").GetProperty("X").GetSingle(),
                PositionY = first.GetProperty("position").GetProperty("Y").GetSingle()
            };
            Require(Npcs.TrySpawn(0, initial, out Actor));
            if (row.GetProperty("targetsBefore").GetArrayLength() == 2)
            {
                var second = row.GetProperty("targetsBefore")[1];
                var next = initial with { PositionX = second.GetProperty("position").GetProperty("X").GetSingle() };
                Require(Npcs.TrySpawn(1, next, out _));
            }
            Status = new(Npcs, handle =>
            {
                registry.PublishNpcBuffs(handle);
                var callback = OnStatus;
                OnStatus = null;
                callback?.Invoke();
            });
            registry.BindNpcBuffStatus(Status);
            Combat = new RuntimeNpcNetworkCombatPipeline(Npcs, Items, lookup, Players,
                () => 100, registry, new(Items), null, new(1000, false, default, 0, 0), new(), false, false,
                lootRandom: Random, seasonalItemContext: () => default);
            Require(Shots.TrySpawn(0, HumanBuffedArrowUpdateSupport1458.Shot(projectileType), out Shot));
            Require(Shots.TryMarkCombatTrusted(Shot.Handle, session.Handle));
            Pass = new(Shots, Npcs, Combat, Players,
                () => { var callback = BeforeTick; BeforeTick = null; callback?.Invoke(); return 100; }, status: Status);
            Require(registry.TryRegister(Connection.Source, outbound));
            Require(projectileRegistry.TryRegister(Connection.Source, outbound));
            registry.PlayerSpawned(Connection, new(session.Slot, 51, 27, 0, 0, 0, 0, 0));
            projectileRegistry.PlayerSpawned(Connection, new(session.Slot, 51, 27, 0, 0, 0, 0, 0));
            _ = Drain();
        }

        internal byte[][] Drain()
        {
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
            var frames = new List<byte[]>();
            while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
            return frames.ToArray();
        }
        public void Dispose() => session.Dispose();
    }
}

internal sealed class HumanBuffedArrowObserverSink1458(INpcStateCommitSink target, Action observe) : INpcStateCommitSink
{
    public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
    {
        observe();
        target.NpcStateCommitted(kind, in snapshot);
    }
}
