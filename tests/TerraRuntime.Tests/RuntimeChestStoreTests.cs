using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeChestStoreTests
{
    [Fact]
    public void Exact_session_exclusively_owns_open_chest_until_close()
    {
        var store = new RuntimeChestStore([Chest()]);
        ConnectionHandle first = Connection(1, 0, 1);
        ConnectionHandle second = Connection(2, 1, 1);

        Assert.True(store.TryOpen(first, 10, 20, out WorldChest opened));
        Assert.Equal((short)3, opened.SlotId);
        Assert.False(store.TryOpen(second, 10, 20, out _));

        Assert.True(store.TryClose(first, out short closed));
        Assert.Equal((short)3, closed);
        Assert.True(store.TryOpen(second, 10, 20, out _));
    }

    [Fact]
    public void Stale_generation_cannot_reuse_another_sessions_open_chest()
    {
        var store = new RuntimeChestStore([Chest()]);
        ConnectionHandle current = Connection(10, 0, 2);
        ConnectionHandle stale = Connection(9, 0, 1);

        Assert.True(store.TryOpen(current, 10, 20, out _));
        Assert.False(store.TrySetItem(
            stale,
            new TerrariaChestItemState(3, 0, 1, 0, 1),
            out _));
        Assert.False(store.TryClose(stale, out _));
        Assert.True(store.TryGetOpenChest(current, out _));
    }

    [Fact]
    public void Owner_can_update_item_and_empty_state_is_canonicalized()
    {
        var store = new RuntimeChestStore([Chest()]);
        ConnectionHandle owner = Connection(1, 0, 1);
        Assert.True(store.TryOpen(owner, 10, 20, out _));

        var update = new TerrariaChestItemState(3, 1, 37, 2, 1);
        Assert.True(store.TrySetItem(owner, in update, out TerrariaChestItemState committed));
        Assert.Equal(update, committed);
        Assert.True(store.TryGetOpenChest(owner, out WorldChest chest));
        Assert.Equal(37, chest.Items[1].Stack);
        Assert.Equal(1, chest.Items[1].ItemType);
        Assert.Equal((byte)2, chest.Items[1].Prefix);

        var empty = new TerrariaChestItemState(3, 1, 0, 99, 321);
        Assert.True(store.TrySetItem(owner, in empty, out TerrariaChestItemState canonical));
        Assert.Equal((short)0, canonical.Stack);
        Assert.Equal((byte)0, canonical.Prefix);
        Assert.Equal((short)0, canonical.ItemNetId);
        Assert.True(chest.Items[1].IsEmpty);
    }

    [Fact]
    public void Invalid_item_or_wrong_chest_is_rejected_without_mutation()
    {
        var store = new RuntimeChestStore([Chest()]);
        ConnectionHandle owner = Connection(1, 0, 1);
        Assert.True(store.TryOpen(owner, 10, 20, out WorldChest chest));
        WorldChestItem original = chest.Items[0];

        Assert.False(store.TrySetItem(
            owner,
            new TerrariaChestItemState(4, 0, 1, 0, 1),
            out _));
        Assert.False(store.TrySetItem(
            owner,
            new TerrariaChestItemState(3, 0, 1, 0, -1),
            out _));
        Assert.Equal(original, chest.Items[0]);
    }

    [Fact]
    public void Negative_active_state_releases_world_chest()
    {
        var store = new RuntimeChestStore([Chest()]);
        ConnectionHandle owner = Connection(1, 0, 1);
        Assert.True(store.TryOpen(owner, 10, 20, out _));

        var close = new TerrariaActiveChestState(-1, 0, 0, 0, string.Empty);
        Assert.True(store.TryApplyActiveState(
            owner,
            in close,
            out WorldChest? renamed,
            out bool closedWorldChest));

        Assert.Null(renamed);
        Assert.True(closedWorldChest);
        Assert.False(store.TryGetOpenChest(owner, out _));
    }

    [Fact]
    public void Current_owner_can_rename_matching_world_chest()
    {
        var store = new RuntimeChestStore([Chest()]);
        ConnectionHandle owner = Connection(1, 0, 1);
        Assert.True(store.TryOpen(owner, 10, 20, out _));

        var rename = new TerrariaActiveChestState(3, 10, 20, 4, "Loot");
        Assert.True(store.TryApplyActiveState(
            owner,
            in rename,
            out WorldChest? renamed,
            out bool closedWorldChest));

        Assert.False(closedWorldChest);
        Assert.NotNull(renamed);
        Assert.Equal("Loot", renamed!.Name);
        Assert.True(store.TryGetOpenChest(owner, out WorldChest current));
        Assert.Equal("Loot", current.Name);
        Assert.Same(renamed.Items, current.Items);
    }

    [Fact]
    public void Zero_name_marker_is_active_state_only_and_does_not_clear_name()
    {
        var store = new RuntimeChestStore([Chest()]);
        ConnectionHandle owner = Connection(1, 0, 1);
        Assert.True(store.TryOpen(owner, 10, 20, out _));

        var activeOnly = new TerrariaActiveChestState(3, 10, 20, 0, string.Empty);
        Assert.True(store.TryApplyActiveState(owner, in activeOnly, out WorldChest? renamed, out bool closed));

        Assert.Null(renamed);
        Assert.False(closed);
        Assert.True(store.TryGetOpenChest(owner, out WorldChest current));
        Assert.Equal("Base", current.Name);
    }

    [Fact]
    public void Invalid_name_sentinel_clears_existing_name()
    {
        var store = new RuntimeChestStore([Chest()]);
        ConnectionHandle owner = Connection(1, 0, 1);
        Assert.True(store.TryOpen(owner, 10, 20, out _));

        var clear = new TerrariaActiveChestState(
            3,
            10,
            20,
            global::Multiplicity.Packets.ChestOpen.InvalidNameLength,
            string.Empty);
        Assert.True(store.TryApplyActiveState(owner, in clear, out WorldChest? renamed, out bool closed));

        Assert.False(closed);
        Assert.NotNull(renamed);
        Assert.Empty(renamed!.Name);
        Assert.True(store.TryGetOpenChest(owner, out WorldChest current));
        Assert.Empty(current.Name);
    }

    [Fact]
    public void Rename_requires_exact_open_chest_coordinates_and_name_marker()
    {
        var store = new RuntimeChestStore([Chest()]);
        ConnectionHandle owner = Connection(1, 0, 1);
        Assert.True(store.TryOpen(owner, 10, 20, out _));

        var wrongCoordinates = new TerrariaActiveChestState(3, 11, 20, 4, "Loot");
        var wrongMarker = new TerrariaActiveChestState(3, 10, 20, 3, "Loot");

        Assert.False(store.TryApplyActiveState(owner, in wrongCoordinates, out _, out _));
        Assert.False(store.TryApplyActiveState(owner, in wrongMarker, out _, out _));
    }

    [Fact]
    public void Name_lookup_resolves_minus_one_by_coordinates_and_requires_exact_identity()
    {
        var store = new RuntimeChestStore([Chest()]);

        var byCoordinates = new TerrariaChestNameLookupRequest(-1, 10, 20);
        Assert.True(store.TryResolveNameLookup(in byCoordinates, out WorldChest resolved));
        Assert.Equal((short)3, resolved.SlotId);
        Assert.Equal("Base", resolved.Name);

        var byId = new TerrariaChestNameLookupRequest(3, 10, 20);
        Assert.True(store.TryResolveNameLookup(in byId, out resolved));
        Assert.Equal((short)3, resolved.SlotId);

        var wrongCoordinates = new TerrariaChestNameLookupRequest(3, 11, 20);
        var invalidNegative = new TerrariaChestNameLookupRequest(-2, 10, 20);
        var missing = new TerrariaChestNameLookupRequest(-1, 99, 99);
        Assert.False(store.TryResolveNameLookup(in wrongCoordinates, out _));
        Assert.False(store.TryResolveNameLookup(in invalidNegative, out _));
        Assert.False(store.TryResolveNameLookup(in missing, out _));
    }

    private static WorldChest Chest() =>
        new(
            SlotId: 3,
            X: 10,
            Y: 20,
            Name: "Base",
            Items:
            [
                new WorldChestItem(1, 1, 0),
                default
            ]);

    private static ConnectionHandle Connection(long connectionId, byte playerSlot, ulong generation) =>
        new(
            GameCommandSourceId.FromConnection(connectionId),
            new PlayerHandle(
                new PlayerSlotId(playerSlot),
                new PlayerSessionGeneration(generation)));
}
