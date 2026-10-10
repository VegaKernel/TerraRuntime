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

public sealed class RemoteStyle1AndRangedPrefixPhase1458Tests
{
    [Fact]
    public void Original_style1_tools_and_swords_preserve_remote_clocks_rng_and_inventory()
    {
        using var source=Read("RemoteStyle1PlayerPhaseLinux1458");
        Assert.Equal(128,source.RootElement.GetArrayLength());
        foreach(var row in source.RootElement.EnumerateArray()) CompareSequence(row);
    }

    [Fact]
    public void Source_valid_gun_prefix_clocks_are_independent_of_the_launch_combat_mask()
    {
        using var source=Read("RemoteAllRangedPrefixPhaseLinux1458");
        Assert.Equal(256,source.RootElement.GetArrayLength());
        foreach(var row in source.RootElement.EnumerateArray()) CompareSequence(row);
    }

    [Fact]
    public void Windows_framework_style1_phases_preserve_source_clocks_and_complete_shared_rng() =>
        CompareWindows("RemoteStyle1PlayerPhaseLinux1458","RemoteStyle1PlayerPhaseWindows1458",128);

    [Fact]
    public void Windows_framework_all_valid_gun_prefixes_preserve_source_clock_arithmetic() =>
        CompareWindows("RemoteAllRangedPrefixPhaseLinux1458","RemoteAllRangedPrefixPhaseWindows1458",256);

    [Fact]
    public async Task Unknown_or_malformed_source_clocks_refuse_without_adopting_any_player_phase()
    {
        using var source=Read("RemoteStyle1PlayerPhaseLinux1458");
        foreach(var clocks in new (int? Tool,int? Attack)[] { (null,0),(0,null),(-1,0),(0,-1) })
        {
            using var f=new Fixture(source.RootElement[0]);
            f.Member.ItemPhase=f.Member.ItemPhase!.Value with { ToolTime=clocks.Tool,AttackCD=clocks.Attack };
            var phase=f.Member.ItemPhase;var before=f.Member.CaptureSnapshot();var random=f.Random.Clone();
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.Equal(phase,f.Member.ItemPhase);Assert.Equal(before,f.Member.CaptureSnapshot());
            Assert.True(f.Random.HasSameState(random));
        }
        using var gunMetadata=Read("RemotePrefixedBulletDefaults1458");
        using var gunPhases=Read("RemoteAllRangedPrefixPhaseLinux1458");
        var rejected=gunMetadata.RootElement.EnumerateArray().Where(r=>!r.GetProperty("exact").GetBoolean()).ToArray();
        Assert.Equal(32,rejected.Length);
        foreach(var row in rejected)
        {
            var type=new ItemTypeId(I(row,"weapon"));var prefix=new PrefixId(I(row,"requested"));
            Assert.False(VanillaRemoteRangedItemCheck1458.IsSupported(type,prefix));
            var inputs=gunPhases.RootElement.EnumerateArray().First(r=>I(r,"weapon")==type.Value && I(r,"prefix")==0);
            using var f=new Fixture(inputs);
            // Consumer-negative imported state: bypass normalized GetData5 after ordinary receive.
            // The source-invalid retained prefix must still be refused by the phase owner.
            var inventory=(RuntimePlayerInventoryStore)typeof(PlayerAuthority)
                .GetField("inventory",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Players)!;
            var imported=new PlayerEquipmentCommitRequest(new(0),0,1,checked((byte)prefix.Value),checked((short)type.Value),0);
            Assert.True(inventory.TrySet(f.Connection,in imported));
            var before=f.Member.CaptureSnapshot();var phase=f.Member.ItemPhase;var random=f.Random.Clone();
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.Equal(before,f.Member.CaptureSnapshot());Assert.Equal(phase,f.Member.ItemPhase);
            Assert.True(f.Random.HasSameState(random));Assert.Equal(0,f.Projectiles.ActiveCount);
        }
        Assert.Equal(0,RuntimePlayerItemPhase1458.Constructor.ToolTime);
        Assert.Equal(0,RuntimePlayerItemPhase1458.Constructor.AttackCD);
        foreach(var clocks in new (int? Tool,int? Attack)[] { (0,0),(11,17),(null,null),(-1,0),(0,-1) })
        {
            using var f=new Fixture(source.RootElement[0]);
            f.Member.ItemPhase=f.Member.ItemPhase!.Value with { ToolTime=clocks.Tool,AttackCD=clocks.Attack };
            var expected=f.Member.ItemPhase;
            var detach=new TaskCompletionSource<RuntimePlayerTransferState?>();
            f.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection,detach));
            var transfer=Assert.IsType<RuntimePlayerTransferState>(await detach.Task);
            Assert.Equal(expected,transfer.ItemPhase);
            Assert.False(f.Players.TryGet(f.Connection,out _));
            var store=(RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Players)!;
            ulong serial=store.Serial;
            var attach=new TaskCompletionSource<bool>();
            f.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection,transfer,100,103,true,false,attach));
            bool valid=clocks.Tool is not < 0 && clocks.Attack is not < 0;
            Assert.Equal(valid,await attach.Task);
            if(valid)
            {
                Assert.Equal(clocks.Tool,f.Member.ItemPhase!.Value.ToolTime);
                Assert.Equal(clocks.Attack,f.Member.ItemPhase.Value.AttackCD);
            }
            else
            {
                Assert.False(f.Players.TryGet(f.Connection,out _));Assert.Equal(serial,store.Serial);
                Assert.False(f.Players.TryGetInventoryItem(f.Connection,0,out _));
            }
        }
    }

    private static void CompareWindows(string linuxResource,string windowsResource,int count)
    {
        using var linux=Read(linuxResource);using var windows=Read(windowsResource);
        Assert.Equal(count,windows.RootElement.GetArrayLength());
        foreach(var row in windows.RootElement.EnumerateArray())
        {
            var inputs=linux.RootElement.EnumerateArray().Single(r=>I(r,"weapon")==I(row,"weapon") && I(r,"prefix")==I(row,"prefix") && I(r,"seed")==I(row,"seed") &&
                (!row.TryGetProperty("mode",out var mode) || r.GetProperty("mode").GetString()==mode.GetString()));
            using var f=new Fixture(inputs,windowsArithmetic:true);
            foreach(var step in row.GetProperty("steps").EnumerateArray())
            {
                f.Move(step.GetProperty("use").GetBoolean(),0);AssertRandom(step.GetProperty("before"),f.Random);
                f.Events.Count=0;f.State.Tick();AssertRandom(step.GetProperty("after"),f.Random);
                Assert.NotNull(f.Member.ItemPhase);Assert.Equal(0,f.Events.Count);Assert.Equal(0,f.Projectiles.ActiveCount);
                var expected=step.GetProperty("player");var item=f.Member.ItemPhase!.Value;
                Assert.Equal(I(expected,"animation"),item.Selected.Animation);Assert.Equal(I(expected,"animationMax"),item.Selected.AnimationMax);
                Assert.Equal(I(expected,"itemTime"),item.Selected.ItemTime);Assert.Equal(I(expected,"itemTimeMax"),item.Selected.ItemTimeMax);
                Assert.Equal(expected.GetProperty("release").GetBoolean(),item.Selected.ReleaseUseItem);
                Assert.Equal(expected.GetProperty("pendingReuse").GetBoolean(),item.PendingItemReuse);
                Assert.Equal(I(expected,"crit"),item.Selected.RevolverCritBonus);
                if(expected.TryGetProperty("toolTime",out var tool)) Assert.Equal(tool.GetInt32(),item.ToolTime);
                if(expected.TryGetProperty("attackCD",out var attack)) Assert.Equal(attack.GetInt32(),item.AttackCD);
                Assert.Equal(I(expected,"life"),f.Member.CaptureSnapshot().NpcLife);Assert.Equal(I(expected,"lifeMaximum"),f.Member.DerivedLifeMax);
                Assert.Equal(I(expected,"mana"),f.Member.Mana);Assert.Equal(I(expected,"maximum"),item.Mana.Maximum);
                Assert.Equal(I(expected,"count"),item.Mana.Count);Assert.Equal(expected.GetProperty("delay").GetSingle(),item.Mana.Delay);
                Assert.Equal(expected.GetProperty("heat").GetSingle(),item.ManaHeat);
                Assert.Equal(F(expected,"position","X"),f.Member.PositionX);Assert.Equal(F(expected,"position","Y"),f.Member.PositionY);
                Assert.Equal(F(expected,"velocity","X"),f.Member.VelocityX);Assert.Equal(F(expected,"velocity","Y"),f.Member.VelocityY);
                Assert.Equal(I(expected,"jump"),f.Member.PhysicsPhase!.Value.Jump.RemainingTicks);Assert.Equal(I(expected,"wingTime"),f.Member.PhysicsPhase.Value.Jump.WingTime);
                Assert.Equal(expected.GetProperty("itemRotation").GetSingle(),f.Member.ItemRotation);
                AssertInventory(inputs.GetProperty("afterInventory"),f);
            }
        }
    }

    [Fact]
    public void Carried_source_clocks_follow_common_gun_potion_empty_and_early_return_boundaries()
    {
        foreach(string resource in new[] { "RemoteCrossFamilyClocksLinux1458", "RemoteStyle1EarlyClocksLinux1458" })
        {
            using var document=Read(resource);
            foreach(var row in document.RootElement.EnumerateArray())
            {
                string mode=row.GetProperty("mode").GetString()!;
                if(!mode.StartsWith("early-",StringComparison.Ordinal)) { CompareSequence(row);continue; }
                using var f=new Fixture(row);
                // Explicit independently captured initial state; these retained imports are not packet41 reconstruction.
                var initial=row.GetProperty("initial");
                f.Member.IsDead=initial.GetProperty("dead").GetBoolean();
                if(initial.GetProperty("ghost").GetBoolean()) f.Member.MovementFlags|=64;
                f.Member.PositionX=F(initial,"position","X");f.Member.PositionY=F(initial,"position","Y");
                f.Member.NpcHealth=f.Member.NpcHealth!.Value with { Life=I(initial,"life") };
                var inventory=f.Players.TryGetInventoryItem(f.Connection,0,out var beforeItem)?beforeItem:default;
                foreach(var step in row.GetProperty("steps").EnumerateArray())
                {
                    AssertRandom(step.GetProperty("before"),f.Random);f.Events.Count=0;
                    f.State.Tick();AssertRandom(step.GetProperty("after"),f.Random);
                    var expected=step.GetProperty("player");var item=f.Member.ItemPhase!.Value;
                    Assert.Equal(I(expected,"attackCD"),item.AttackCD);Assert.Equal(I(expected,"toolTime"),item.ToolTime);
                    Assert.Equal(I(expected,"animation"),item.Selected.Animation);Assert.Equal(I(expected,"animationMax"),item.Selected.AnimationMax);
                    Assert.Equal(I(expected,"itemTime"),item.Selected.ItemTime);Assert.Equal(I(expected,"itemTimeMax"),item.Selected.ItemTimeMax);
                    Assert.Equal(expected.GetProperty("release").GetBoolean(),item.Selected.ReleaseUseItem);
                    Assert.Equal(I(expected,"life"),f.Member.CaptureSnapshot().NpcLife);Assert.Equal(I(expected,"mana"),f.Member.Mana);
                    Assert.Equal(I(expected,"maximum"),item.Mana.Maximum);Assert.Equal(expected.GetProperty("heat").GetSingle(),item.ManaHeat);
                    Assert.Equal(0,f.Events.Count);Assert.True(f.Players.TryGetInventoryItem(f.Connection,0,out var currentItem));Assert.Equal(inventory,currentItem);
                }
            }
        }
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
        Assert.True(row.GetProperty("steps").GetArrayLength() is 4 or 8 or 16 or 64);
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
            if(expected.TryGetProperty("toolTime",out var tool)) Assert.Equal(tool.GetInt32(),item.ToolTime);
            if(expected.TryGetProperty("attackCD",out var attack)) Assert.Equal(attack.GetInt32(),item.AttackCD);
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
        using var stream = typeof(RemoteStyle1AndRangedPrefixPhase1458Tests).Assembly.GetManifestResourceStream(resource)!;
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
            var initial=source.GetProperty("initial");
            Member.ItemAnimation=I(initial,"animation");
            var phase=Member.ItemPhase!.Value;
            Member.ItemPhase=phase with
            {
                ToolTime=initial.TryGetProperty("toolTime",out var tool)?tool.GetInt32():0,
                AttackCD=initial.TryGetProperty("attackCD",out var attack)?attack.GetInt32():0,
                Selected=phase.Selected with { Animation=I(initial,"animation"),AnimationMax=I(initial,"animationMax"),
                    ItemTime=I(initial,"itemTime"),ItemTimeMax=I(initial,"itemTimeMax"),ReleaseUseItem=initial.GetProperty("release").GetBoolean() }
            };
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
