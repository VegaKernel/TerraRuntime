using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RemoteMetalAndPrefixItemPhase1458Tests
{
    [Fact]
    public void Original_equipment_ingress_preserves_metal_armor_and_full_remote_phase()
    {
        using var source = Read("NeutralMetalPlayerPhase1458");
        var rows = source.RootElement.EnumerateArray().ToArray();
        Assert.Equal(80, rows.Length);
        Assert.Equal(52, rows.Count(row => row.GetProperty("mode").GetString()!.StartsWith("piece-", StringComparison.Ordinal)));
        Assert.Equal(20, rows.Count(row => row.GetProperty("mode").GetString()!.StartsWith("set-", StringComparison.Ordinal)));
        foreach (var row in rows)
        {
            int equippedCount = row.GetProperty("armor").EnumerateArray().Count(item => item.GetInt32() > 0);
            Assert.Equal(equippedCount,row.GetProperty("equipmentCommands").GetArrayLength());
            Assert.Equal(equippedCount,row.GetProperty("equipmentRelayFrames").GetArrayLength());
            for (int index = 0; index < 3; index++)
            {
                Assert.Equal(row.GetProperty("armor")[index].GetInt32(), row.GetProperty("worn")[index].GetProperty("type").GetInt32());
                Assert.Equal(0, row.GetProperty("worn")[index].GetProperty("prefix").GetInt32());
            }
            Assert.All(row.GetProperty("local255Armor").EnumerateArray(), item => Assert.Equal(0, I(item, "type")));
            if (row.GetProperty("armor").EnumerateArray().Any(item => item.GetInt32() > 0))
                Assert.True(I(row.GetProperty("steps")[0].GetProperty("player"),"defense") > 0);
            CompareSequence(row);
        }
    }

    [Fact]
    public void Supported_prefixed_bullet_weapons_match_original_use_clocks_and_shared_rng()
    {
        using var source = Read("RemotePrefixedBulletItemPhase1458");
        var rows = source.RootElement.EnumerateArray().ToArray();
        Assert.Equal(84, rows.Length);
        Assert.Equal(8, rows.Select(row => I(row, "weapon")).Distinct().Count());
        foreach (var row in rows) CompareSequence(row);
    }

    [Fact]
    public void Original_prefix_requests_separate_valid_represented_clock_effects_from_unowned_prefixes()
    {
        using var source = Read("RemotePrefixedBulletDefaults1458");
        var rows = source.RootElement.EnumerateArray().ToArray();
        Assert.Equal(288, rows.Length);
        int[] represented = [0,16,17,18,19,20,21,22,23,24,25,82];
        int oldClockProfiles = 0, sourceValidProfiles = 0;
        foreach (var row in rows)
        {
            bool expected = row.GetProperty("exact").GetBoolean();
            if (expected) sourceValidProfiles++;
            // Preserve the earlier 84-profile slice while the same unchanged source requests
            // now prove the expanded clock-only mask; launch/combat admission remains separate.
            if (expected && represented.Contains(I(row,"requested"))) oldClockProfiles++;
            Assert.Equal(expected, VanillaRemoteBulletItemCheck1458.IsSupported(new(I(row, "weapon")), new(I(row, "requested"))));
        }
        Assert.Equal(84,oldClockProfiles);
        Assert.Equal(256,sourceValidProfiles);
    }

    [Fact]
    public void Unknown_wrong_slot_prefixed_and_non_neutral_armor_refuse_before_any_phase_adoption()
    {
        using var source = Read("NeutralMetalPlayerPhase1458");
        var row = source.RootElement.EnumerateArray().First(row => row.GetProperty("mode").GetString() == "baseline" && I(row, "weapon") == 0);
        foreach (var entry in new[] { (59,1,0), (59,80,0), (59,89,62), (59,2757,0), (62,490,0), (900,1,0) })
        {
            using var f = new Fixture(row);
            f.Equipment((short)entry.Item1,(short)entry.Item2,1,(byte)entry.Item3);
            var item = f.Member.ItemPhase;
            var physics = f.Member.PhysicsPhase;
            var health = f.Member.NpcHealth;
            var random = f.Random.Clone();
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.Equal(item,f.Member.ItemPhase);
            Assert.Equal(physics,f.Member.PhysicsPhase);
            Assert.Equal(health,f.Member.NpcHealth);
            Assert.True(f.Random.HasSameState(random));
            Assert.Equal(0,f.Projectiles.ActiveCount);
        }
    }

    [Fact]
    public void Windows_framework_prefix_arithmetic_preserves_original_full_remote_clocks()
    {
        using var source = Read("RemotePrefixedBulletWindowsPhase1458");
        using var linux = Read("RemotePrefixedBulletItemPhase1458");
        var rows = source.RootElement.EnumerateArray().ToArray();
        Assert.Equal(84,rows.Length);
        foreach (var row in rows)
        {
            Assert.Equal(2,I(row,"netMode"));
            Assert.Equal(255,I(row,"myPlayer"));
            var inputs=linux.RootElement.EnumerateArray().First(candidate => I(candidate,"weapon")==I(row,"weapon") &&
                I(candidate,"prefix")==I(row,"prefix") && I(candidate,"seed")==I(row,"seed"));
            using var f=new Fixture(inputs,windowsArithmetic:true);
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                f.Move(step.GetProperty("use").GetBoolean(),0);
                AssertRandom(step.GetProperty("before"),f.Random);
                f.Events.Count=0;
                f.State.Tick();
                Assert.True(f.Member.ItemPhase.HasValue);
                AssertCapturedPlayer(step.GetProperty("player"),f.Member);
                AssertRandom(step.GetProperty("after"),f.Random);
                Assert.Equal(0,f.Events.Count);
                Assert.Equal(0,f.Projectiles.ActiveCount);
                AssertInventory(inputs.GetProperty("afterInventory"),f);
            }
            Assert.Equal(50,I(row,"ammoStack"));
            Assert.Equal(0,I(row,"projectileCount"));
            Assert.True(row.GetProperty("restoredOutside").GetBoolean());
        }
    }

    [Fact]
    public void Inherited_metal_and_matching_vanity_preserve_two_player_census_and_first_favorite_break()
    {
        foreach (string resource in new[] { "InheritedMetalWholePlayer1458", "InheritedMetalBlockerWholePlayer1458", "MetalVanityWholePlayer1458" })
        {
            using var document=Read(resource);
            foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
            {
                using var f=new CensusFixture(row);
                if (row.GetProperty("localVanity").GetBoolean())
                {
                    // The original nonempty local255 reference cannot be inferred for the host's empty owner.
                    typeof(PlayerAuthority).GetField("combatEquipmentLocalVanityArmor",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(f.Players,null);
                    var before=f.Members.Select(m=>m.CaptureSnapshot()).ToArray();
                    Assert.False(f.Players.TryTickRemotePlayerPhase());
                    Assert.Equal(before,f.Members.Select(m=>m.CaptureSnapshot()));
                    AssertRandom(row.GetProperty("steps")[0].GetProperty("before"),f.Random);
                    continue;
                }
                var inventory=f.CaptureInventory();
                foreach (var step in row.GetProperty("steps").EnumerateArray())
                {
                    AssertRandom(step.GetProperty("before"),f.Random);
                    f.Events.Count=0;
                    f.State.Tick();
                    AssertRandom(step.GetProperty("after"),f.Random);
                    Assert.Equal(0,f.Events.Count);
                    Assert.Equal(0,f.Projectiles.ActiveCount);
                    foreach (var expected in step.GetProperty("players").EnumerateArray().Where(p=>I(p,"slot")<255))
                    {
                        int slot=I(expected,"slot"), index=slot/2;
                        var member=f.Members[index]; var item=member.ItemPhase!.Value;
                        Assert.Equal(I(expected,"life"),member.CaptureSnapshot().NpcLife);
                        Assert.Equal(I(expected,"lifeMax"),member.DerivedLifeMax);
                        Assert.Equal(I(expected,"lifeCount"),member.NpcHealth!.Value.RegenCount);
                        Assert.Equal(expected.GetProperty("lifeTime").GetSingle(),member.NpcHealth.Value.RegenTime);
                        Assert.Equal(50,member.Life);
                        Assert.Equal(I(expected,"mana"),member.Mana);
                        Assert.Equal(I(expected,"maximum"),item.Mana.Maximum);
                        Assert.Equal(I(expected,"count"),item.Mana.Count);
                        Assert.Equal(expected.GetProperty("delay").GetSingle(),item.Mana.Delay);
                        Assert.Equal(expected.GetProperty("heat").GetSingle(),item.ManaHeat);
                        Assert.Equal(I(expected,"animation"),item.Selected.Animation);
                        Assert.Equal(I(expected,"animationMax"),item.Selected.AnimationMax);
                        Assert.Equal(I(expected,"itemTime"),item.Selected.ItemTime);
                        Assert.Equal(I(expected,"itemTimeMax"),item.Selected.ItemTimeMax);
                        Assert.Equal(expected.GetProperty("release").GetBoolean(),item.Selected.ReleaseUseItem);
                        Assert.Equal(F(expected,"position","X"),member.PositionX);
                        Assert.Equal(F(expected,"position","Y"),member.PositionY);
                        Assert.Equal(F(expected,"velocity","X"),member.VelocityX);
                        Assert.Equal(F(expected,"velocity","Y"),member.VelocityY);
                        Assert.Equal(I(expected,"jump"),member.PhysicsPhase!.Value.Jump.RemainingTicks);
                        Assert.Equal(I(expected,"wingTime"),member.PhysicsPhase.Value.Jump.WingTime);
                        Assert.Equal(expected.GetProperty("releaseJump").GetBoolean(),member.PhysicsPhase.Value.Jump.ReleaseReady);
                        Assert.True(f.Players.TryCaptureCombatSnapshot(f.Connections[index],out var combat));
                        Assert.Equal(I(expected,"defense"),combat.Defense);
                        f.Buffs(index).CaptureSlots(out var types,out var times);
                        Assert.Equal(expected.GetProperty("buffTypes").EnumerateArray().Select(v=>v.GetInt32()),types.Select(v=>v.Value));
                        Assert.Equal(expected.GetProperty("buffTimes").EnumerateArray().Select(v=>v.GetInt32()),times);
                    }
                    Assert.Equal(inventory,f.CaptureInventory());
                }
            }
        }
    }

    private static void AssertCapturedPlayer(JsonElement expected,RuntimePlayerMember member)
    {
        var item=member.ItemPhase!.Value;
        Assert.Equal(I(expected,"life"),member.CaptureSnapshot().NpcLife);
        Assert.Equal(I(expected,"lifeMaximum"),member.DerivedLifeMax);
        Assert.Equal(I(expected,"mana"),member.Mana);
        Assert.Equal(I(expected,"maximum"),item.Mana.Maximum);
        Assert.Equal(I(expected,"count"),item.Mana.Count);
        Assert.Equal(expected.GetProperty("delay").GetSingle(),item.Mana.Delay);
        Assert.Equal(expected.GetProperty("heat").GetSingle(),item.ManaHeat);
        Assert.Equal(I(expected,"animation"),item.Selected.Animation);
        Assert.Equal(I(expected,"animationMax"),item.Selected.AnimationMax);
        Assert.Equal(I(expected,"itemTime"),item.Selected.ItemTime);
        Assert.Equal(I(expected,"itemTimeMax"),item.Selected.ItemTimeMax);
        Assert.Equal(expected.GetProperty("release").GetBoolean(),item.Selected.ReleaseUseItem);
        Assert.Equal(expected.GetProperty("pendingReuse").GetBoolean(),item.PendingItemReuse);
        Assert.Equal(I(expected,"crit"),item.Selected.RevolverCritBonus);
        Assert.Equal(expected.GetProperty("itemRotation").GetSingle(),member.ItemRotation);
        Assert.Equal(I(expected,"jump"),member.PhysicsPhase!.Value.Jump.RemainingTicks);
        Assert.Equal(I(expected,"wingTime"),member.PhysicsPhase.Value.Jump.WingTime);
        Assert.Equal(F(expected,"position","X"),member.PositionX);
        Assert.Equal(F(expected,"position","Y"),member.PositionY);
        Assert.Equal(F(expected,"velocity","X"),member.VelocityX);
        Assert.Equal(F(expected,"velocity","Y"),member.VelocityY);
    }

    private static void CompareSequence(JsonElement row)
    {
        string identity = $"{I(row, "weapon")}/{I(row, "ammo")}/{I(row, "seed")}/{row.GetProperty("mode").GetString()}";
        Assert.Equal(2, I(row, "netMode"));
        Assert.Equal(255, I(row, "myPlayer"));
        Assert.True(row.GetProperty("solid1").GetBoolean());
        Assert.True(row.GetProperty("inventoryUnchanged").GetBoolean());
        Assert.Equal(0, row.GetProperty("beforeProjectiles").GetArrayLength());
        Assert.Equal(0, row.GetProperty("afterProjectiles").GetArrayLength());
        Assert.True(row.GetProperty("steps").GetArrayLength() is 16 or 64);
        using var f = new Fixture(row);
        Assert.Equal(row.GetProperty("initial").GetProperty("release").GetBoolean(), f.Member.ItemPhase!.Value.Selected.ReleaseUseItem);
        AssertInventory(row.GetProperty("beforeInventory"), f);
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            int tick = I(step, "tick");
            if (step.GetProperty("command").ValueKind == JsonValueKind.String)
            {
                byte[] command = Convert.FromHexString(step.GetProperty("command").GetString()!);
                if (command[0] == 41)
                {
                    f.Apply(new PlayerItemAnimationRuntimeCommand(f.Connection, BitConverter.ToSingle(command, 2), BitConverter.ToInt16(command, 6)));
                    Assert.Equal(I(step.GetProperty("beforePlayer"), "animation"), f.Member.ItemAnimation);
                }
                else Assert.Equal(13, command[0]);
            }
            var before = step.GetProperty("beforePlayer");
            f.Move(step.GetProperty("use").GetBoolean(), checked((byte)I(before, "selected")));
            AssertRandom(step.GetProperty("before"), f.Random);
            f.Events.Count = 0;
            f.State.Tick();
            Assert.True(f.Member.ItemPhase.HasValue, $"{identity}/{tick}");
            Assert.NotNull(f.Member.PhysicsPhase);
            Assert.Equal(0, f.Events.Count);
            Assert.Equal(0, step.GetProperty("frames").GetArrayLength());
            Assert.Equal(0, f.Projectiles.ActiveCount);
            AssertRandom(step.GetProperty("after"), f.Random);
            var expected = step.GetProperty("player");
            var item = f.Member.ItemPhase!.Value;
            Assert.Equal(I(expected, "life"), f.Member.CaptureSnapshot().NpcLife);
            Assert.Equal(I(expected, "lifeMaximum"), f.Member.DerivedLifeMax);
            Assert.Equal(400, f.Member.Life);
            Assert.Equal(I(expected, "mana"), f.Member.Mana);
            Assert.Equal(200, f.Member.MaxMana);
            Assert.Equal(I(expected, "maximum"), item.Mana.Maximum);
            Assert.Equal(I(expected, "count"), item.Mana.Count);
            Assert.Equal(expected.GetProperty("delay").GetSingle(), item.Mana.Delay);
            Assert.Equal(expected.GetProperty("heat").GetSingle(), item.ManaHeat);
            Assert.Equal(I(expected, "animation"), item.Selected.Animation);
            Assert.Equal(I(expected, "animationMax"), item.Selected.AnimationMax);
            Assert.Equal(I(expected, "itemTime"), item.Selected.ItemTime);
            Assert.Equal(I(expected, "itemTimeMax"), item.Selected.ItemTimeMax);
            Assert.Equal(expected.GetProperty("release").GetBoolean(), item.Selected.ReleaseUseItem);
            Assert.Equal(expected.GetProperty("pendingReuse").GetBoolean(), item.PendingItemReuse);
            Assert.Equal(I(expected, "crit"), item.Selected.RevolverCritBonus);
            Assert.Equal(I(expected, "selected"), f.Member.SelectedItem);
            Assert.Equal(F(expected, "position", "X"), f.Member.PositionX);
            Assert.Equal(F(expected, "position", "Y"), f.Member.PositionY);
            Assert.Equal(F(expected, "velocity", "X"), f.Member.VelocityX);
            Assert.Equal(F(expected, "velocity", "Y"), f.Member.VelocityY);
            Assert.Equal(I(expected, "jump"), f.Member.PhysicsPhase!.Value.Jump.RemainingTicks);
            Assert.Equal(I(expected, "wingTime"), f.Member.PhysicsPhase.Value.Jump.WingTime);
            Assert.Equal(expected.GetProperty("releaseJump").GetBoolean(), f.Member.PhysicsPhase.Value.Jump.ReleaseReady);
            Assert.Equal(expected.GetProperty("itemRotation").GetSingle(), f.Member.ItemRotation);
            if (expected.TryGetProperty("defense", out var defense))
            {
                Assert.True(f.Players.TryCaptureCombatSnapshot(f.Connection, out var combat));
                Assert.Equal(defense.GetInt32(),combat.Defense);
                Assert.Equal(VanillaPlayerCombatSnapshot.Baseline,combat with { Defense = 0 });
                Assert.Equal(0,I(expected,"lifeRegen"));
                Assert.Equal(0,I(expected,"activeDust"));
            }
            f.Buffs.CaptureSlots(out var types, out var times);
            Assert.Equal(expected.GetProperty("buffTypes").EnumerateArray().Select(x => x.GetInt32()), types.Select(x => x.Value));
            Assert.Equal(expected.GetProperty("buffTimes").EnumerateArray().Select(x => x.GetInt32()), times);
            AssertInventory(row.GetProperty("afterInventory"), f);
        }
        Assert.True(row.GetProperty("restoredOutside").GetBoolean(), identity);
    }

    private static void AssertInventory(JsonElement expected, Fixture f)
    {
        foreach (var slot in expected.EnumerateArray())
        {
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, I(slot, "slot"), out var actual));
            Assert.Equal(I(slot, "type"), actual.ItemType.Value);
            Assert.Equal(I(slot, "stack"), actual.Stack);
            Assert.Equal(I(slot, "prefix"), actual.Prefix.Value);
        }
    }

    private static JsonDocument Read(string resource)
    {
        using var stream = typeof(RemoteMetalAndPrefixItemPhase1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    private static int I(JsonElement row, string name) => row.GetProperty(name).GetInt32();
    private static float F(JsonElement row, string name, string component) => row.GetProperty(name).GetProperty(component).GetSingle();
    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(), typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random));
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly ServerRuntimeState State;
        internal readonly PlayerAuthority Players;
        internal readonly RuntimeProjectileStore Projectiles = new();
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly ConnectionHandle Connection;
        internal readonly EventSink Events = new();
        private readonly PlayerJoinSession session;
        internal RuntimePlayerMember Member { get { Assert.True(Players.TryGet(0, out var member)); return member; } }
        internal PlayerBuffState Buffs => ((RuntimePlayerTransferProfileStore)typeof(PlayerAuthority)
            .GetField("transferProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!).CaptureBuffState(Connection)!;
        internal Fixture(JsonElement source,bool windowsArithmetic=false)
        {
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            for (int x = 0; x < 400; x++) tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            State = new(playerEvents: Events, worldTiles: tiles, projectiles: Projectiles,
                playerUpdateRandomSeed: new(I(source, "seed")));
            Players = ((ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(State)!).Players;
            Random = (VanillaUnifiedRandom1458)typeof(PlayerAuthority).GetField("playerUpdateRandom", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;
            Players.SetPlayerUpdateWorldFacts(new RuntimePlayerUpdateWorld1458(400, 300, 80, false, false)
                { WindowsItemPrefixArithmetic = windowsArithmetic });
            Players.SetRemotePlayerEnvironment(new(false, false), Projectiles);
            var pool = new PlayerSlotPool(1);
            Assert.True(pool.TryAcquireConnection(out var lease));
            session = new(lease!); session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(99800), session.Handle);
            Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 10, 200)));
            foreach (var item in source.GetProperty("beforeInventory").EnumerateArray())
                if (I(item, "type") > 0) Equipment(checked((short)I(item, "slot")), checked((short)I(item, "type")), checked((short)I(item, "stack")), checked((byte)I(item,"prefix")));
            if (source.TryGetProperty("armor", out var armor))
                for (int index=0;index<3;index++)
                    if (armor[index].GetInt32()>0) Equipment((short)(VanillaPlayerItemSlotCatalog.ArmorStart+index),(short)armor[index].GetInt32(),1);
            Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            Apply(new PlayerBuffTypesRuntimeCommand(Connection, new(session.Slot,
                source.GetProperty("buffs").EnumerateArray().Select(x => new BuffTypeId(x.GetInt32())).ToArray())));
            Move(false, 0);
        }
        internal void ConsumeAmmoOwned()
        {
            // Exercise the existing callback-free inventory transaction, independently of reports.
            var inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority)
                .GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;
            Assert.True(inventory.TryGet(Connection, 54, out var before));
            Assert.True(inventory.TryApplyAtomic(Connection,
                new RuntimePlayerInventoryMutation[] { new(54, before with { Stack = checked((short)(before.Stack - 1)) }) }, inventory.Serial));
        }
        internal void Equipment(short slot, short type, short stack, byte prefix=0) =>
            Apply(new PlayerEquipmentRuntimeCommand(Connection, new(new(0), slot, stack, prefix, type, 0)));
        internal void Apply(RuntimeCommand command) => State.Apply(command);
        internal void Move(bool use, byte selected) => Apply(new PlayerMovementRuntimeCommand(Connection,
            new(new(0), (byte)(64 | (use ? 32 : 0)), 16, 0, 64, selected, 1600, 1606,
                false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        public void Dispose() => session.Dispose();
    }

    private sealed class CensusFixture : IDisposable
    {
        internal readonly ServerRuntimeState State;
        internal readonly PlayerAuthority Players;
        internal readonly RuntimeProjectileStore Projectiles=new();
        internal readonly EventSink Events=new();
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly ConnectionHandle[] Connections=new ConnectionHandle[2];
        private readonly PlayerJoinSession[] sessions=new PlayerJoinSession[3];
        internal RuntimePlayerMember[] Members => new[] { Member(0),Member(2) };
        private RuntimePlayerMember Member(int slot) { Assert.True(Players.TryGet(Connections[slot/2],out var m)); return m; }
        internal CensusFixture(JsonElement row)
        {
            var tiles=new WorldTileStore(new WorldDimensions(400,300));
            for(int x=0;x<400;x++) tiles.Set(x,103,new WorldTile { Type=1,Flags=WorldTileFlags.Active });
            State=new(playerEvents:Events,worldTiles:tiles,projectiles:Projectiles,playerUpdateRandomSeed:new(0));
            Players=((ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(State)!).Players;
            Random=(VanillaUnifiedRandom1458)typeof(PlayerAuthority).GetField("playerUpdateRandom",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Players)!;
            Players.SetPlayerUpdateWorldFacts(new(400,300,80,false,false));
            Players.SetRemotePlayerEnvironment(new(false,false),Projectiles);
            var pool=new PlayerSlotPool(3);
            for(int i=0;i<3;i++) { Assert.True(pool.TryAcquireConnection(out var lease)); sessions[i]=new(lease!); }
            for(int index=0;index<2;index++)
            {
                int slot=index*2; var session=sessions[slot];
                session.ObserveWorldRequest(); session.ObserveSectionRequest();
                var c=Connections[index]=new(GameCommandSourceId.FromConnection(99800+slot),session.Handle);
                State.Apply(new PlayerHealthRuntimeCommand(c,new(session.Slot,50,400)));
                State.Apply(new PlayerManaRuntimeCommand(c,new(session.Slot,10,200)));
                if(index==0)
                {
                    foreach(var item in row.GetProperty("items").EnumerateArray())
                        State.Apply(new PlayerEquipmentRuntimeCommand(c,new(session.Slot,(short)I(item,"slot"),1,0,(short)I(item,"type"),item.GetProperty("favorite").GetBoolean()?(byte)1:(byte)0)));
                    if(row.TryGetProperty("weapon",out var weapon) && weapon.GetInt32()>0)
                    {
                        State.Apply(new PlayerEquipmentRuntimeCommand(c,new(session.Slot,0,1,0,(short)weapon.GetInt32(),0)));
                        State.Apply(new PlayerEquipmentRuntimeCommand(c,new(session.Slot,54,50,0,97,0)));
                    }
                }
                State.Apply(new PlayerSpawnRuntimeCommand(c,session,new(session.Slot,(short)(100+index*5),103,0,0,0,0,0)));
                State.Apply(new PlayerHealthRuntimeCommand(c,new(session.Slot,50,400)));
                State.Apply(new PlayerBuffTypesRuntimeCommand(c,new(session.Slot,Array.Empty<BuffTypeId>())));
                State.Apply(new PlayerMovementRuntimeCommand(c,new(session.Slot,64,16,0,64,0,1600+index*80,1606,false,0,0,false,0,false,0,0,0,0,false,0,0)));
            }
        }
        internal PlayerBuffState Buffs(int index) => ((RuntimePlayerTransferProfileStore)typeof(PlayerAuthority).GetField("transferProfiles",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Players)!).CaptureBuffState(Connections[index])!;
        internal string CaptureInventory() => string.Join(";",Connections.Select(c=>string.Join(",",Enumerable.Range(0,59).Select(slot=> { Assert.True(Players.TryGetInventoryItem(c,slot,out var item)); return item.ToString(); }))));
        public void Dispose() { foreach(var session in sessions) session.Dispose(); }
    }

    private sealed class EventSink : IRuntimePlayerEventSink
    {
        internal int Count;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) => Count++;
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r) => Count++;
        public void PlayerHealthUpdated(ConnectionHandle c, in PlayerHealthCommitRequest r) => Count++;
        public void PlayerManaUpdated(ConnectionHandle c, in PlayerManaCommitRequest r) => Count++;
        public void PlayerItemAnimationUpdated(ConnectionHandle c, float rotation, short animation) => Count++;
        public void PlayerBuffTypesUpdated(ConnectionHandle c, in PlayerBuffTypesCommitRequest r) => Count++;
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) => Count++;
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) => Count++;
        public void PlayerDisconnected(ConnectionHandle c) => Count++;
    }
}
