using TerraRuntime.Core.Worlds;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.IO.Compression;
using TerraRuntime.Contracts.Gameplay;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests.WorldChestCrafting;

sealed class Fixture : IDisposable
{
    public RuntimeChestStore Store { get; }
    public RuntimeChestReplicationRegistry Replication { get; } = new();
    public EventRelay Events { get; }
    public RuntimeChestCommandProcessor Processor { get; }
    public ServerRuntimeState State { get; }
    public PlayerBootstrapFrameSink Bootstrap { get; }
    public CapturingCommands Commands { get; } = new();
    public TerrariaConnectionOutboundQueue Requester { get; } = Outbound();
    public TerrariaConnectionOutboundQueue Observer { get; } = Outbound();
    public WorldTileStore Tiles { get; }
    public ConnectionHandle Connection { get; }
    private readonly ForwardSink forward = new();
    public PlayerAuthority Players => Runtime(State).Players;
    public RuntimePlayerInventoryStore ActorInventory => Inventory(Players);
    public RuntimePlayerMember Member { get { if (!Membership(Players).TryGet(Connection, out var member)) throw new Exception("Missing member"); return member; } }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "membership")]
    private static extern ref RuntimePlayerMembership Membership(PlayerAuthority players);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "fundedQuantities")]
    public static extern ref short[] Funding(RuntimePlayerInventoryStore inventory);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "coinFundedValues")]
    public static extern ref long[] Copper(RuntimePlayerInventoryStore inventory);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "shopBankFundedQuantities")]
    public static extern ref short[] BankFunding(RuntimePlayerInventoryStore inventory);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "consumed")]
    public static extern ref bool CostConsumed(PlayerAuthority.RemoteCraftCostCertificate certificate);
    public int ShopSteps => Runtime(State).Players.LastShopWorkSteps;

    public Fixture(JsonElement row, WorldChest[]? customChests = null, WorldDimensions? dimensions = null)
    {
        Events = new(Replication);
        Tiles = new(dimensions ?? new WorldDimensions(100, 100));
        for (int floorX = 0; floorX < Tiles.Dimensions.WidthTiles; floorX++) Tiles.Set(floorX, 23, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        string name = row.TryGetProperty("name", out var originalName) ? originalName.GetString()! : row.GetProperty("mode").GetString()!;
        bool largeSchema = row.TryGetProperty("mode", out _);
        int x = name == "far-chest" ? 80 : 20;
        int y = x;
        WorldChest[] chests;
        if (largeSchema)
        {
            chests = row.GetProperty("before").EnumerateArray().Where(chest => chest.GetProperty("index").GetInt32() >= 0)
                .Select(chest =>
                {
                    var item = chest.GetProperty("first");
                    var stock = new WorldChestItem[chest.GetProperty("maxItems").GetInt32()];
                    stock[0] = new(item.GetProperty("stack").GetInt32(), item.GetProperty("type").GetInt32(), item.GetProperty("prefix").GetByte());
                    return new WorldChest((short)chest.GetProperty("index").GetInt32(), chest.GetProperty("x").GetInt32(), chest.GetProperty("y").GetInt32(), "", stock);
                }).ToArray();
        }
        else
        {
            WorldChestItem[] items = row.GetProperty("before").EnumerateArray()
                .Select(item => new WorldChestItem(item.GetProperty("stack").GetInt32(),
                    item.GetProperty("type").GetInt32(), item.GetProperty("prefix").GetByte())).ToArray();
            chests = [new WorldChest(0, x, y, "", items)];
        }
        chests = customChests ?? chests;
        Store = new(chests, ownsLoadedWorldStock: true);
        foreach (WorldChest chest in chests)
            Tiles.Set(chest.X, chest.Y, new WorldTile { Type = 21, FrameX = (short)(name == "locked" ? 72 : 0), Flags = WorldTileFlags.Active });
        ServerRuntimeState? state = null;
        Processor = new(Store, Replication, Tiles, connection => state?.IsCurrentPlayerConnection(connection) == true);
        State = state = new(playerEvents: Events, worldTiles: Tiles, projectiles: new TerraRuntime.Core.Projectiles.RuntimeProjectileStore(), chestCommands: Processor, worldItems: new RuntimeWorldItemStore(), playerUpdateRandomSeed: new(0), townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 });
        var source = GameCommandSourceId.FromConnection(1);
        Bootstrap = new(new PlayerSlotPool(1), Requester,
            PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 }, Array.Empty<ReadOnlyMemory<byte>>(), new byte[] { 3, 0, 49 }), source,
            new SpawnIngress(State), appearanceIngress: null, equipmentIngress: new RuntimePlayerEquipmentIngress(Commands), movementIngress: null, inner: forward);
        Replication.TryRegister(source, Requester);
        forward.Target = new ChestInteractionFrameSink(source, Bootstrap, new ContinueSink(), new RuntimeChestNetworkIngress(Commands));
        var hello = Frame(TerrariaMessageId.Hello, [11, 84, 101, 114, 114, 97, 114, 105, 97, 51, 50, 54]);
        Bootstrap.OnFrame(in hello);
        var world = Frame(TerrariaMessageId.RequestWorldData, []);
        Bootstrap.OnFrame(in world);
        var section = Frame(TerrariaMessageId.SpawnTileData, new byte[9]);
        Bootstrap.OnFrame(in section);
        byte[] spawnBody = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
        BinaryPrimitives.WriteInt16LittleEndian(spawnBody.AsSpan(1), 20);
        BinaryPrimitives.WriteInt16LittleEndian(spawnBody.AsSpan(3), 20);
        var spawn = Frame(TerrariaMessageId.PlayerSpawn, spawnBody);
        Bootstrap.OnFrame(in spawn);
        if (Bootstrap.JoinState != PlayerJoinState.Playing) throw new InvalidOperationException("Fixture not playing");
        Connection = new(source, Bootstrap.AssignedPlayerHandle!.Value);
        var observer = new ConnectionHandle(GameCommandSourceId.FromConnection(2),
            new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)));
        Replication.TryRegister(observer.Source, Observer);
        var observerSpawn = new PlayerSpawnCommitRequest(observer.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        Replication.PlayerSpawned(observer, in observerSpawn);
        if (largeSchema)
        {
            foreach (var bank in row.GetProperty("before").EnumerateArray().Where(chest => chest.GetProperty("index").GetInt32() < 0))
            {
                int bankIndex = -bank.GetProperty("index").GetInt32() - 2;
                short[] starts = [99, 299, 500, 700];
                var item = bank.GetProperty("first");
                var stock = new RuntimePlayerInventoryItem(new ItemTypeId(item.GetProperty("type").GetInt32()),
                    (short)item.GetProperty("stack").GetInt32(), new PrefixId(item.GetProperty("prefix").GetByte()), 0);
                if (!Inventory(Runtime(State).Players).TrySetShopBankReport(Connection, starts[bankIndex], in stock))
                    throw new InvalidOperationException("Could not configure source bank image");
            }
        }
        if (name == "owner-open") Store.TryOpen(Connection, (short)x, (short)y, out _);
        if (name == "other-user-open") Store.TryOpen(observer, (short)x, (short)y, out _);
        Drain(Requester);
        Drain(Observer);
    }

    public string[] Drain(TerrariaConnectionOutboundQueue endpoint)
    {
        var frames = new List<string>();
        while (Queue(endpoint).TryRead(out var frame)) frames.Add(Convert.ToHexString(frame.Bytes.Span));
        return frames.ToArray();
    }

    public void UseIngress(IGameCommandIngress<RuntimeCommand> ingress) =>
        forward.Target = new ChestInteractionFrameSink(Connection.Source, Bootstrap, new ContinueSink(), new RuntimeChestNetworkIngress(ingress));

    public (RuntimePlayerInventoryItem[] Items, short[] Quantity, long[] Copper, short[] Caps) CapturePlayer()
    {
        if (!Inventory(Runtime(State).Players).TryCaptureShopJournal(Connection, out var capture) || capture is null)
            throw new InvalidOperationException("Could not capture full219");
        return (capture.Before, capture.Quantity, capture.Copper, capture.Caps);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    public static extern ref ServerRuntimeComposition Runtime(ServerRuntimeState state);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    public static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority players);

    public void Dispose() => Bootstrap.Dispose();

    public static TerrariaFrame Decode(byte[] encoded)
    {
        var source = new ReadOnlySequence<byte>(encoded);
        if (TerrariaFrameDecoder.TryRead(ref source, out var result) != TerrariaFrameReadResult.Frame || !source.IsEmpty)
            throw new InvalidOperationException("Malformed source fixture");
        return result;
    }

    private static TerrariaFrame Frame(TerrariaMessageId message, byte[] body) =>
        new((ushort)(body.Length + 3), (byte)message, ReadOnlySequence<byte>.Empty, new(body));

    private static TerrariaConnectionOutboundQueue Outbound() => new(new OutboundQueueOptions(256, 65536, 65536));

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_queue")]
    private static extern ref BoundedOutboundQueue Queue(TerrariaConnectionOutboundQueue endpoint);
}

sealed class SpawnIngress(ServerRuntimeState state) : IPlayerSpawnCommitIngress
{
    public bool TryPost(GameCommandSourceId source, PlayerJoinSession session, in PlayerSpawnCommitRequest request)
    {
        var connection = new ConnectionHandle(source, session.Handle);
        state.Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 100, 100)));
        state.Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 20, 20)));
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, request));
        return true;
    }
}

sealed class CapturingCommands : IGameCommandIngress<RuntimeCommand>
{
    public List<RuntimeCommand> Commands { get; } = [];
    public IGameCommandIngress<RuntimeCommand>? Forward;
    public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
    {
        if (Forward is not null) return Forward.TryPost(source, command);
        if (Commands.Count >= 128) return false;
        Commands.Add(command);
        return true;
    }
}

sealed class ForwardSink : ITerrariaFrameSink
{
    public ITerrariaFrameSink? Target;
    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => Target?.OnFrame(in frame) ?? TerrariaFrameSinkResult.Continue;
}

sealed class ContinueSink : ITerrariaFrameSink
{
    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue;
}

sealed class EventRelay(RuntimeChestReplicationRegistry replication) : IRuntimePlayerEventSink
{
    public Action? OnEquipment { get; set; }
    public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request) { OnEquipment?.Invoke(); replication.PlayerEquipmentUpdated(connection, in request); }
    public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) => replication.PlayerAppearanceUpdated(connection, in request);
    public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) => replication.PlayerSpawned(connection, in request);
    public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) => replication.PlayerMoved(connection, in request);
    public void PlayerDisconnected(ConnectionHandle connection) => replication.PlayerDisconnected(connection);
}
