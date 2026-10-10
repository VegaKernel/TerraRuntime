using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;
namespace TerraRuntime.Tests;
public sealed class ArcaneDerivedManaEquipment1458Tests
{
    [Fact]
    public void Original_effective_equipment_slot_and_inheritance_grants_match_complete_player_phases()
        => Compare(row => !new[] { "dead", "ghost", "outside", "mixed-two-actors" }.Contains(row.GetProperty("spec").GetProperty("name").GetString()), 3136);

    [Fact]
    public void Original_early_branches_and_mixed_census_preserve_only_owned_mana_and_shared_random()
        => Compare(row => new[] { "dead", "ghost", "outside", "mixed-two-actors" }.Contains(row.GetProperty("spec").GetProperty("name").GetString()), 256);

    private static void Compare(Func<JsonElement,bool> include,int expectedCount)
    {
        using var source=Read();int compared=0;
        foreach(string platform in new[]{"linux","windows"})
        foreach(var row in source.RootElement.GetProperty(platform).EnumerateArray().Concat(ReadSupplementRows(platform)).Where(include))
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
    public void Late_arcane_profile_and_mana_report_mutations_refuse_whole_census_without_overwrite()
    {
        using var source=Read();var row=source.RootElement.GetProperty("linux").EnumerateArray().Single(r=>r.GetProperty("spec").GetProperty("name").GetString()=="shield");
        // Imported-record safety: the protocol's canonical42 maximum is a short.
        // This deliberately malformed int field is not an original source phase claim;
        // retain the existing upper clamp without overflowing the new equipment sum.
        using(var imported=new Fixture(row,false))
        {
            imported.Member.ItemPhase=imported.Member.ItemPhase!.Value with { BaseManaMaximum=int.MaxValue };
            Assert.True(imported.Players.TryTickRemotePlayerPhase());
            Assert.Equal(400,imported.Member.ItemPhase!.Value.Mana.Maximum);
            Assert.Equal(int.MaxValue,imported.Member.ItemPhase.Value.BaseManaMaximum);
        }
        foreach(var rejected in new[]{(Type:156,Prefix:(byte)66),(Type:3212,Prefix:(byte)66),(Type:1613,Prefix:(byte)0)})
        {
            using var f=new Fixture(row,false);f.Equipment(72,(short)rejected.Type,1,rejected.Prefix);var item=f.Member.ItemPhase;var random=f.Random.Clone();
            Assert.False(f.Players.TryTickRemotePlayerPhase());Assert.Equal(item,f.Member.ItemPhase);Assert.True(f.Random.HasSameState(random));
        }
        foreach(bool report in new[]{false,true})
        {
            using var f=new Fixture(row,false);var step=row.GetProperty("steps")[0];f.PrepareStep(step);
            var before=f.Member.ItemPhase;var random=f.Random.Clone();bool once=false;
            f.Players.SetNpcHealthWorldFacts(()=>{if(!once){once=true;if(report)f.State.Apply(new PlayerManaRuntimeCommand(f.Connection,new(f.Connection.Player.Slot,17,300)));else f.Equipment(62,156,1,65);}return new(false,false);});
            Assert.False(f.Players.TryTickRemotePlayerPhase());Assert.True(once);Assert.True(f.Random.HasSameState(random));
            if(report){Assert.Equal(17,f.Member.Mana);Assert.Equal(300,f.Member.ItemPhase!.Value.BaseManaMaximum);}
            else Assert.Equal(before,f.Member.ItemPhase);
            Assert.Equal(0,f.Projectiles.ActiveCount);
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
        WaterWalk = player.GetProperty("waterWalk").GetBoolean()
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

    private static IEnumerable<JsonElement> ReadSupplementRows(string platform)
    {
        using var stream=typeof(ArcaneDerivedManaEquipment1458Tests).Assembly.GetManifestResourceStream("ArcaneVanityBlockerSource1458")!;
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);using var document=JsonDocument.Parse(gzip);
        foreach(var row in document.RootElement.GetProperty(platform).EnumerateArray())yield return row.Clone();
    }

    private static JsonDocument Read()
    {
        using var stream = typeof(ArcaneDerivedManaEquipment1458Tests).Assembly.GetManifestResourceStream("ArcaneDerivedManaSource1458")!;
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
            Equipment(54, 97, 50);
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
            if(tick==8&&spec.TryGetProperty("action",out var action)){if(action.GetString()=="add")Equipment(62,156,1,66);else Equipment(62,0,0);}
            if(secondSession is not null)MoveSecond(tick>0&&tick!=5,step.GetProperty("beforeSecond").GetProperty("lastItemUseAttemptSuccess").GetBoolean());
            bool use = branch == "living" && tick > 0 && tick != 5;
            Move(use, (byte)(tick >= 3 && branch == "living" ? 1 : 0),
                step.GetProperty("beforePlayer").GetProperty("lastItemUseAttemptSuccess").GetBoolean());
            if (branch == "ghost" && tick > 0) Member.MovementFlags |= 64;
        }

        public void Dispose() {session.Dispose();secondSession?.Dispose();gap?.Dispose();}
    }
}
