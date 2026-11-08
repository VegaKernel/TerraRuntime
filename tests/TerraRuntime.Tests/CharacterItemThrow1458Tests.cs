using System.Reflection;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class CharacterItemThrow1458Tests
{
    private static ConnectionHandle Actor(byte slot = 0, ulong generation = 1) =>
        new(GameCommandSourceId.FromConnection(12300 + slot), new(new(slot), new(generation)));
    private static RuntimePlayerInventoryItem Item(int type, short count, int prefix = 0) => new(new(type), count, new(prefix), 0);
    private static void Set(RuntimePlayerInventoryStore store, ConnectionHandle actor, short slot, RuntimePlayerInventoryItem item) =>
        Assert.True(store.TrySet(actor, item.ToCommitRequest(actor.Player.Slot, slot)));

    [Theory]
    [InlineData(2, 20, 0)] [InlineData(24, 1, 81)] [InlineData(97, 30, 0)] [InlineData(72, 2, 0)]
    public void Initial_import_pays_one_drop_without_creating_private_funding(int type, short count, int prefix)
    {
        var store = new RuntimePlayerInventoryStore(); var actor = Actor(); var item = Item(type, count, prefix);
        Set(store, actor, 0, item); store.SealCharacterDropIntake(actor);
        Assert.True(store.TryGetFundedQuantity(actor, 0, out var funded)); Assert.Equal(0, funded);
        Assert.True(store.TryPrepareFundedDrop(actor, 0, out var drop)); Assert.NotNull(drop);
        Assert.True(drop!.TryAdoptUnpublished()); Assert.False(drop.TryAdoptUnpublished());
        Set(store, actor, 0, item); store.SealCharacterDropIntake(actor);
        Assert.False(store.TryPrepareFundedDrop(actor, 0, out _));
    }

    [Fact]
    public void Identity_prefix_actor_and_generation_do_not_share_import_allowances()
    {
        var store = new RuntimePlayerInventoryStore(); var actor = Actor(); var peer = Actor(1);
        Set(store, actor, 0, Item(24, 1, 81)); store.SealCharacterDropIntake(actor);
        Set(store, actor, 0, Item(24, 1, 0)); Assert.False(store.TryPrepareFundedDrop(actor, 0, out _));
        Set(store, peer, 0, Item(24, 1, 81)); store.SealCharacterDropIntake(peer);
        Set(store, actor, 0, Item(24, 1, 81));
        Assert.True(store.TryPrepareFundedDrop(peer, 0, out var other)); Assert.True(other!.TryAdoptUnpublished());
        Assert.True(store.TryPrepareFundedDrop(actor, 0, out var stale));
        store.Clear(actor); var replacement = Actor(generation: 2);
        Set(store, replacement, 0, Item(24, 1, 81));
        Assert.False(stale!.TryAdoptUnpublished()); Assert.False(store.TryPrepareFundedDrop(actor, 0, out _));
        Assert.False(store.TryPrepareFundedDrop(replacement, 0, out _));
        store.SealCharacterDropIntake(replacement); Assert.True(store.TryPrepareFundedDrop(replacement, 0, out _));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Actual_owned_consumption_burns_only_unfunded_import_units(bool mixedFunding)
    {
        var store = new RuntimePlayerInventoryStore(); var actor = Actor();
        Set(store, actor, 0, Item(97, 2)); store.SealCharacterDropIntake(actor);
        if (mixedFunding)
        {
            Set(store, actor, 0, Item(97, 3)); var funding = new short[59]; funding[0] = 1;
            store.AdoptFundedQuantities(actor, funding);
        }
        Assert.True(store.TryGet(actor, 0, out var before));
        var write = new RuntimePlayerInventoryMutation(0, before with { Stack = (short)(before.Stack - 1) });
        Assert.True(store.TryApplyProjectileConsumption(actor, in write, store.Serial));
        short remaining = mixedFunding ? (short)2 : (short)1;
        Set(store, actor, 0, Item(97, (short)(remaining + 1)));
        Assert.False(store.TryPrepareFundedDrop(actor, 0, out _));
        Set(store, actor, 0, Item(97, remaining));
        Assert.True(store.TryPrepareFundedDrop(actor, 0, out var drop)); Assert.True(drop!.TryAdoptUnpublished());
    }

    [Fact]
    public void Coin_conversion_conserves_import_copper_and_cannot_inflate_it()
    {
        var store = new RuntimePlayerInventoryStore(); var actor = Actor();
        Set(store, actor, 0, Item(72, 2)); store.SealCharacterDropIntake(actor);
        Set(store, actor, 0, Item(73, 1)); Assert.False(store.TryPrepareFundedDrop(actor, 0, out _));
        Set(store, actor, 0, Item(71, 200));
        Assert.True(store.TryPrepareFundedDrop(actor, 0, out var drop)); Assert.True(drop!.TryAdoptUnpublished());
        Set(store, actor, 0, Item(72, 1)); Assert.False(store.TryPrepareFundedDrop(actor, 0, out _));
    }

    [Fact]
    public void Transfer_preserves_remaining_allowances_and_rejects_invalid_payloads()
    {
        var source = new RuntimePlayerInventoryStore(); var actor = Actor();
        Set(source, actor, 0, Item(2, 10)); source.SealCharacterDropIntake(actor);
        var values = source.CaptureCharacterDropAllowances(actor, out long copper, out bool sealedIntake);
        Assert.True(RuntimePlayerInventoryStore.AreValidCharacterDropAllowances(values, copper, sealedIntake));
        Assert.False(RuntimePlayerInventoryStore.AreValidCharacterDropAllowances([values[0], values[0]], copper, sealedIntake));
        Assert.False(RuntimePlayerInventoryStore.AreValidCharacterDropAllowances(values, copper, false));
        var target = new RuntimePlayerInventoryStore(); var next = Actor(generation: 2);
        Set(target, next, 3, Item(2, 10)); target.AdoptCharacterDropAllowances(next, values, copper, sealedIntake);
        target.SealCharacterDropIntake(next);
        Assert.True(target.TryPrepareFundedDrop(next, 3, out var drop)); Assert.True(drop!.TryAdoptUnpublished());
        Set(target, next, 3, Item(2, 10)); target.SealCharacterDropIntake(next);
        Assert.False(target.TryPrepareFundedDrop(next, 3, out _));
    }

    [Theory]
    [InlineData(2, 20, 0)] [InlineData(24, 1, 81)] [InlineData(72, 2, 0)] [InlineData(267, 1, 0)]
    public async Task Actual_ordinary_throw_debits_initial_inventory_and_rejects_forged_replay(int type, short count, byte prefix)
    {
        using var f = new Fixture((short)type, count, prefix);
        Assert.Equal(prefix, f.Slot(0).Prefix.Value);
        var proposal = f.Proposal((short)type, count, prefix);
        Assert.True(f.Players.TryPrepareFundedDrop(f.Connection, in proposal, out _), "Owned inventory drop preparation rejected the imported image.");
        if (prefix != 0)
        {
            Assert.NotNull(f.Players.PickupWindowsArithmetic);
            Assert.Equal(VanillaItemPrefixNormalizationStatus1458.Resolved,
                VanillaItemPrefixNormalization1458.Resolve(new(type), new(prefix), f.Random.SourceRandom.Clone().Next,
                    f.Players.PickupWindowsArithmetic!.Value, out _, f.Players.PickupWorld));
        }
        var wrong = proposal with { Stack = (short)(count + 1) };
        Assert.Null(await f.Drop(wrong)); Assert.Equal(count, f.Slot(0).Stack);
        Assert.True(f.Players.TryPrepareFundedDrop(f.Connection, in proposal, out _), "Rejected proposal invalidated the legitimate inventory preparation.");
        Assert.True(VanillaWorldItemOwner1458.TryGetSourceCreationNoGravity(new(type), out _));
        var accepted = await f.Drop(proposal);
        Assert.NotNull(accepted); Assert.Equal(type, accepted.Value.ItemNetId); Assert.Equal(prefix, accepted.Value.Prefix);
        Assert.True(f.Slot(0).IsEmpty);
        Assert.True(f.Items.TryGetFundedQuantity(accepted.Value, out int quantity)); Assert.Equal(count, quantity);
        var after = f.Random.SourceRandom.Clone();
        Assert.Null(await f.Drop(proposal)); Assert.True(after.HasSameState(f.Random.SourceRandom));
        f.Set(0, (short)type, count, prefix); // A later report cannot reseed the already spent import.
        Assert.Null(await f.Drop(proposal));
        Assert.True(after.HasSameState(f.Random.SourceRandom));
        Assert.Equal(1, f.Items.ActiveCount);
    }

    [Fact]
    public async Task Partial_stack_uses_the_real_paired_cursor_move_then_one_world_allocation()
    {
        using var f = new Fixture(2, 20);
        f.Set(0, 2, 17); f.Set(58, 2, 3);
        Assert.Equal(17, f.Slot(0).Stack); Assert.Equal(3, f.Slot(58).Stack);
        Assert.NotNull(await f.Drop(f.Proposal(2, 3))); Assert.True(f.Slot(58).IsEmpty);
        for (int tick = 0; tick < 10; tick++) f.State.Tick();
        f.Set(0, 2, 0); f.Set(58, 2, 17);
        Assert.NotNull(await f.Drop(f.Proposal(2, 17)));
        f.Set(58, 2, 1); Assert.Null(await f.Drop(f.Proposal(2, 1)));
    }

    [Fact]
    public async Task Imported_selected_stacks_match_independent_original_DropSelectedItem_observations()
    {
        using var stream = typeof(CharacterItemThrow1458Tests).Assembly.GetManifestResourceStream("SourceHumanDrop1458");
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream!, CompressionMode.Decompress);
        using var original = JsonDocument.Parse(gzip);
        int compared = 0;
        foreach (string platform in new[] { "linux", "windowsClr4X86" })
        foreach (var row in original.RootElement.GetProperty(platform).GetProperty("rows").EnumerateArray())
        {
            int id = row.GetProperty("id").GetInt32();
            string profile = row.GetProperty("profile").GetString()!;
            if (id is not (2 or 3 or 24 or 109 or 110 or 219) || profile is not ("selected0" or "last1" or "leftmoving")) continue;
            var ground = row.GetProperty("ground");
            var item = ground.GetProperty("item");
            short count = item.GetProperty("stack").GetInt16(); byte prefix = item.GetProperty("prefix").GetByte();
            using var f = new Fixture((short)id, count, prefix);
            Assert.True(f.Players.TryGet(f.Connection, out var member));
            if (profile == "leftmoving") { member.ControlFlags &= 191; member.VelocityX = 1.25f; }
            var position = ground.GetProperty("position"); var velocity = ground.GetProperty("velocity");
            var proposal = new WorldItemDropStateUpdate(position.GetProperty("x").GetSingle() + 1200,
                position.GetProperty("y").GetSingle() + 1206, velocity.GetProperty("x").GetSingle(), velocity.GetProperty("y").GetSingle(),
                count, prefix, WorldItemOwnershipMode.GrabDelayForLocalPlayer, (short)id, false, 0, ground.GetProperty("enemyGrabDelay").GetByte());
            Assert.NotNull(await f.Drop(proposal)); Assert.True(f.Slot(0).IsEmpty);
            Assert.Equal(row.GetProperty("afterClocks").GetProperty("animation").GetInt32(), member.ItemAnimation);
            compared++;
        }
        Assert.Equal(36, compared);
    }

    [Fact]
    public async Task Held_world_reservation_refuses_without_inventory_or_rng_and_retry_pays_once()
    {
        using var f = new Fixture(2, 10);
        Assert.True(f.Items.TryReserveDropSlot(out var reservation)); var before = f.Random.SourceRandom.Clone();
        Assert.Null(await f.Drop(f.Proposal(2, 10))); Assert.Equal(10, f.Slot(0).Stack);
        Assert.True(before.HasSameState(f.Random.SourceRandom));
        Assert.True(f.Items.TryReleaseDropReservation(in reservation));
        Assert.NotNull(await f.Drop(f.Proposal(2, 10))); Assert.True(f.Slot(0).IsEmpty);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly SystemWorldItemSpawnRandom Random = new(1458);
        internal readonly ServerRuntimeState State;
        internal readonly PlayerAuthority Players;
        internal readonly ConnectionHandle Connection;
        private readonly PlayerJoinSession session;
        internal Fixture(short type, short count, byte prefix = 0)
        {
            var tiles = new WorldTileStore(new(400, 300));
            for (int x = 0; x < 400; x++) tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var projectiles = new RuntimeProjectileStore();
            State = new(worldItems: Items, worldItemSpawnRandom: Random, worldTiles: tiles, projectiles: projectiles, playerUpdateRandomSeed: new(0));
            var runtime = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!;
            Players = runtime.Players;
            Players.SetPlayerUpdateWorldFacts(new RuntimePlayerUpdateWorld1458(400, 300, 80, false, false));
            Players.SetRemotePlayerEnvironment(new(false, false), projectiles);
            var pool = new PlayerSlotPool(1); Assert.True(pool.TryAcquireConnection(out var lease));
            session = new(lease!); session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(12301), session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            State.Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 50, 200)));
            Set(0, type, count, prefix);
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerMovementRuntimeCommand(Connection, new(session.Slot, 64, 16, 0, 0, 0, 1600, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            State.Apply(new PlayerBuffTypesRuntimeCommand(Connection, new(session.Slot, Array.Empty<BuffTypeId>())));
            State.Tick();
        }
        internal void Set(short slot, short type, short count, byte prefix = 0) => State.Apply(new PlayerEquipmentRuntimeCommand(Connection, new(Connection.Player.Slot, slot, count, prefix, type, 0)));
        internal RuntimePlayerInventoryItem Slot(short slot) { Assert.True(Players.TryGetInventoryItem(Connection, slot, out var item)); return item; }
        internal WorldItemDropStateUpdate Proposal(short type, short count, byte prefix = 0)
        {
            Assert.True(Players.TryGet(Connection, out var member)); var state = member.CaptureSnapshot();
            return new(state.PositionX + 2, state.PositionY + 13, ((state.ControlFlags & 64) != 0 ? 4 : -4) + state.VelocityX,
                -2, count, prefix, type == 267 ? WorldItemOwnershipMode.GrabDelayForAllPlayers : WorldItemOwnershipMode.GrabDelayForLocalPlayer,
                type, false, 0, type == 267 ? (byte)0 : (byte)100);
        }
        internal async Task<WorldItemSnapshot?> Drop(WorldItemDropStateUpdate proposal)
        {
            var completion = new TaskCompletionSource<WorldItemSnapshot?>();
            State.Apply(new WorldItemAllocateRuntimeCommand(Connection, proposal, completion)); return await completion.Task;
        }
        public void Dispose() => session.Dispose();
    }
}
