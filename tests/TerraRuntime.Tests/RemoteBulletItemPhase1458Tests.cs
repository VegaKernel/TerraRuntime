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
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RemoteBulletItemPhase1458Tests
{
    [Fact]
    public void Eight_remote_bullet_weapons_match_original_ascending_player_phase_without_shooting_or_debit()
    {
        using var source = Read();
        var rows = source.RootElement.EnumerateArray().Where(row => row.GetProperty("mode").GetString()!.EndsWith("fresh", StringComparison.Ordinal)).ToArray();
        Assert.Equal(32, rows.Length);
        Assert.Equal(8, rows.Select(row => I(row, "weapon")).Distinct().Count());
        foreach (var row in rows) CompareSequence(row);
    }

    [Fact]
    public void Actual_animation_reports_and_potion_to_gun_selection_preserve_source_continued_clocks()
    {
        using var source = Read();
        var rows = source.RootElement.EnumerateArray().Where(row => !row.GetProperty("mode").GetString()!.EndsWith("fresh", StringComparison.Ordinal)).ToArray();
        Assert.Equal(9, rows.Length);
        Assert.Equal(2, rows.Count(row => row.GetProperty("mode").GetString() == "from-potion"));
        foreach (var row in rows) CompareSequence(row);
    }

    [Fact]
    public void Late_ammo_and_buff_reports_refuse_prepared_remote_clocks_without_partial_rng_or_actor_adoption()
    {
        using var source = Read();
        var row = source.RootElement.EnumerateArray().First(row => I(row, "weapon") == 98 &&
            I(row, "ammo") == 97 && row.GetProperty("mode").GetString()!.EndsWith("fresh", StringComparison.Ordinal));
        foreach (string mutation in new[] { "ammo-report", "buff-report", "owned-ammo" })
        {
            using var f = new Fixture(row);
            f.State.Tick(); // The genuine first release phase precedes the prepared use.
            Assert.True(f.Member.ItemPhase.HasValue);
            f.Move(true, 0);
            var beforeItem = f.Member.ItemPhase;
            var beforePhysics = f.Member.PhysicsPhase;
            var beforeLife = f.Member.NpcHealth;
            short beforeMana = f.Member.Mana;
            var beforeRandom = f.Random.Clone();
            bool called = false;
            f.Players.SetNpcHealthWorldFacts(() =>
            {
                if (!called)
                {
                    called = true;
                    if (mutation == "ammo-report") f.Equipment(54, 0, 0);
                    else if (mutation == "owned-ammo") f.ConsumeAmmoOwned();
                    else f.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection,
                        new(new(0), new BuffTypeId[] { VanillaBuffIds.Poisoned })));
                }
                return new(false, false);
            });
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.True(called);
            Assert.Equal(beforeItem, f.Member.ItemPhase);
            Assert.Equal(beforePhysics, f.Member.PhysicsPhase);
            Assert.Equal(beforeLife, f.Member.NpcHealth);
            Assert.Equal(beforeMana, f.Member.Mana);
            Assert.True(f.Random.HasSameState(beforeRandom));
            Assert.Equal(0, f.Projectiles.ActiveCount);
        }
    }

    [Fact]
    public void Empty_item_and_potion_animation_reports_preserve_original_pending_reuse_boundary()
    {
        using var source = Read("RemoteCommonPending1458");
        var rows = source.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(new[] { 0, 28 }, rows.Select(row => I(row, "weapon")).OrderBy(value => value));
        foreach (var row in rows)
        {
            Assert.True(row.GetProperty("steps")[6].GetProperty("player").GetProperty("pendingReuse").GetBoolean());
            Assert.False(row.GetProperty("steps")[7].GetProperty("player").GetProperty("pendingReuse").GetBoolean());
            CompareSequence(row);
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
        Assert.Equal(64, row.GetProperty("steps").GetArrayLength());
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

    private static JsonDocument Read(string resource = "RemoteBulletItemPhase1458")
    {
        using var stream = typeof(RemoteBulletItemPhase1458Tests).Assembly.GetManifestResourceStream(resource)!;
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
        internal Fixture(JsonElement source)
        {
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            for (int x = 0; x < 400; x++) tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            State = new(playerEvents: Events, worldTiles: tiles, projectiles: Projectiles,
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 },
                playerUpdateRandomSeed: new(I(source, "seed")));
            Players = ((ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(State)!).Players;
            Random = (VanillaUnifiedRandom1458)typeof(PlayerAuthority).GetField("playerUpdateRandom", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
            Players.SetRemotePlayerEnvironment(new(false, false), Projectiles);
            var pool = new PlayerSlotPool(1);
            Assert.True(pool.TryAcquireConnection(out var lease));
            session = new(lease!); session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(99800), session.Handle);
            Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 10, 200)));
            foreach (var item in source.GetProperty("beforeInventory").EnumerateArray())
                if (I(item, "type") > 0) Equipment(checked((short)I(item, "slot")), checked((short)I(item, "type")), checked((short)I(item, "stack")));
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
        internal void Equipment(short slot, short type, short stack) =>
            Apply(new PlayerEquipmentRuntimeCommand(Connection, new(new(0), slot, stack, 0, type, 0)));
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
