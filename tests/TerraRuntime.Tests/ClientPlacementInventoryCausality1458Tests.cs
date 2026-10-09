using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Players;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ClientPlacementInventoryCausality1458Tests
{
    [Fact]
    public void Original_client_placements_preserve_server_stock_until_the_later_coalesced_equipment_report()
    {
        using Stream resource = typeof(ClientPlacementInventoryCausality1458Tests).Assembly
            .GetManifestResourceStream("ClientPlacementInventoryCausality1458")!;
        Assert.NotNull(resource);
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using JsonDocument source = JsonDocument.Parse(gzip);
        int profiles = 0, placements = 0, reports = 0, coalesced = 0, empty = 0;
        foreach (JsonElement row in source.RootElement.EnumerateArray())
        {
            // Replacement action22 is a distinct producer, outside this simple tile/wall component.
            if (I(row, "type") == 30 && row.GetProperty("mode").GetString() == "blocked")
                continue;
            profiles++;
            JsonElement server = row.GetProperty("serverReceive");
            Assert.Equal(2, I(server, "netMode"));
            Assert.Equal(255, I(server, "myPlayer"));
            var tiles = new WorldTileStore(new WorldDimensions(200, 150));
            var state = new ServerRuntimeState(worldTiles: tiles);
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
            using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
            Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
            var owner = new ConnectionHandle(GameCommandSourceId.FromConnection(14581700 + profiles), session.Handle);
            state.Apply(new PlayerSpawnRuntimeCommand(owner, session,
                new PlayerSpawnCommitRequest(session.Slot, 100, 100, 0, 0, 0, 0, 0)));
            Assert.Equal(PlayerSpawnCommitResult.Committed, state.LastSpawnCommitResult);
            JsonElement initial = server.GetProperty("beforeInventory")[0];
            Equip(state, owner, (short)I(initial, "type"), (short)I(initial, "stack"), (byte)I(initial, "prefix"));
            JsonElement beforeCell = row.GetProperty("beforeCell");
            var existing = new WorldTile
            {
                Type = (ushort)I(beforeCell, "type"), Wall = (ushort)I(beforeCell, "wall"),
                Flags = beforeCell.GetProperty("active").GetBoolean() ? WorldTileFlags.Active : WorldTileFlags.None
            };
            tiles.Set(I(beforeCell, "x"), I(beforeCell, "y"), in existing);
            int rowPlacements = 0;
            foreach (JsonElement frame in row.GetProperty("checkFrames").EnumerateArray())
            {
                byte[] bytes = Convert.FromHexString(frame.GetString()!);
                Assert.Equal(11, bytes.Length);
                Assert.Equal(17, bytes[2]);
                Assert.True(bytes[3] is 1 or 3);
                var request = new TerrariaTileManipulationState(bytes[3], Short(bytes, 4), Short(bytes, 6), Short(bytes, 8), bytes[10]);
                state.Apply(new ClientTileManipulationRuntimeCommand(owner, request));
                placements++; rowPlacements++;
                Assert.Equal(rowPlacements, state.AppliedClientTileManipulations);
                AssertInventory(state, owner, server.GetProperty("after17Inventory"));
                WorldTile committed = tiles.Get(request.TileX, request.TileY);
                if (request.Action == 1)
                {
                    Assert.True(committed.IsActive);
                    Assert.Equal(request.Data, committed.TileType.Value);
                }
                else Assert.Equal((int)request.Data, committed.Wall);

                // A repeated placement into the occupied target must not invent another consumption.
                state.Apply(new ClientTileManipulationRuntimeCommand(owner, request));
                Assert.Equal(rowPlacements, state.AppliedClientTileManipulations);
                Assert.Equal(committed, tiles.Get(request.TileX, request.TileY));
                AssertInventory(state, owner, server.GetProperty("after17Inventory"));
            }
            if (rowPlacements == 2) coalesced++;
            int rowReports = 0;
            foreach (JsonElement frame in row.GetProperty("syncFrames").EnumerateArray())
            {
                byte[] bytes = Convert.FromHexString(frame.GetString()!);
                if (bytes[2] != 5) continue; // Original packet138 is separate acknowledgement plumbing.
                Assert.Equal(12, bytes.Length);
                Assert.Equal(0, Short(bytes, 4));
                Equip(state, owner, Short(bytes, 9), Short(bytes, 6), bytes[8], bytes[11]);
                reports++; rowReports++;
                if (Short(bytes, 9) == 0) empty++;
                AssertInventory(state, owner, server.GetProperty("after5Inventory"));
                // Duplicate reports replace the same complete item; they cannot debit it again.
                Equip(state, owner, Short(bytes, 9), Short(bytes, 6), bytes[8], bytes[11]);
                AssertInventory(state, owner, server.GetProperty("after5Inventory"));
            }
            Assert.Equal(rowPlacements == 0 ? 0 : 1, rowReports);
            AssertInventory(state, owner, server.GetProperty("after5Inventory"));
            Assert.Equal(0, state.RejectedPlayerEquipmentUpdates);
        }
        Assert.Equal(12, profiles);
        Assert.Equal(10, placements);
        Assert.Equal(9, reports);
        Assert.Equal(1, coalesced);
        Assert.Equal(2, empty);
    }

    private static void Equip(ServerRuntimeState state, ConnectionHandle owner, short type, short stack, byte prefix, byte flags = 0) =>
        state.Apply(new PlayerEquipmentRuntimeCommand(owner,
            new PlayerEquipmentCommitRequest(owner.Player.Slot, 0, stack, prefix, type, flags)));

    private static void AssertInventory(ServerRuntimeState state, ConnectionHandle owner, JsonElement expected)
    {
        foreach (JsonElement slot in expected.EnumerateArray())
        {
            Assert.True(state.TryCapturePlayerInventoryItem(owner.Player, (short)I(slot, "slot"), out RuntimePlayerInventoryItem actual));
            Assert.Equal(I(slot, "type"), actual.ItemType.Value);
            Assert.Equal(I(slot, "stack"), actual.Stack);
            Assert.Equal(I(slot, "prefix"), actual.Prefix.Value);
            Assert.Equal(0, actual.ItemFlags);
        }
    }

    private static short Short(byte[] bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static int I(JsonElement element, string property) => element.GetProperty(property).GetInt32();
}
