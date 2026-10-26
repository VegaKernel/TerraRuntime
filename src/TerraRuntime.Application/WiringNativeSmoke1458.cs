using System.Buffers;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>Exercises the production command, clock and tile owners in the shipping native smoke.</summary>
internal static class WiringNativeSmoke1458
{
    public static bool Run()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        Put(tiles, 20, 20, 136); Put(tiles, 21, 20, 4); Put(tiles, 22, 20, 1, actuator: true);
        var state = new ServerRuntimeState(worldTiles: tiles);
        var slots = new PlayerSlotPool(1);
        if (!slots.TryAcquireConnection(out var lease) || lease is null) return false;
        using var session = new PlayerJoinSession(lease);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var owner = new ConnectionHandle(GameCommandSourceId.FromConnection(145859), session.Handle);
        var spawn = new PlayerSpawnCommitRequest(owner.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        state.Apply(new PlayerSpawnRuntimeCommand(owner, session, spawn));
        if (state.LastSpawnCommitResult != PlayerSpawnCommitResult.Committed) return false;
        var sequence = new ReadOnlySequence<byte>(new byte[] { 7, 0, 59, 20, 0, 20, 0 });
        if (TerrariaFrameDecoder.TryRead(ref sequence, out var frame) != TerrariaFrameReadResult.Frame ||
            !TerrariaHitSwitchCodec1458.TryDecode(in frame, out var press)) return false;
        state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, press));
        if (tiles.Get(21, 20).FrameX != 66 || !tiles.Get(22, 20).IsActuated) return false;
        Put(tiles, 20, 20, 144);
        state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, press));
        for (int tick = 0; tick < 59; tick++) state.Tick();
        if (tiles.Get(21, 20).FrameX != 66) return false;
        state.Tick();
        if (tiles.Get(21, 20).FrameX != 0 || tiles.Get(22, 20).IsActuated) return false;
        Put(tiles, 20, 30, 136); Put(tiles, 21, 29, 1); Put(tiles, 21, 33, 1);
        for (int row = 0; row < 3; row++)
            tiles.Set(21, 30 + row, new WorldTile { Type = 10, FrameY = (short)(row * 18),
                Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        var doorPress = new TerrariaHitSwitchState1458(20, 30);
        state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, doorPress));
        if (tiles.Get(21, 30).Type != 11) return false;
        state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, doorPress));
        if (tiles.Get(21, 30).Type != 10) return false;
        byte[] importance = new byte[(VanillaWorldFormat326.TileTypeCount + 7) / 8];
        importance[144 >> 3] |= 1 << (144 & 7);
        var timerOnly = new WorldTileStore(new WorldDimensions(1, 1));
        timerOnly.Set(0, 0, new WorldTile { Type = 144, FrameX = 54, FrameY = 18,
            Flags = WorldTileFlags.Active | WorldTileFlagMasks.Wires | WorldTileFlags.Actuator | WorldTileFlags.Inactive });
        using var payload = new MemoryStream();
        return WorldFileTileEncoder.TryEncode(timerOnly, VanillaWorldFormat326.TileTypeCount, importance,
            payload, out _) == WorldFileTileEncodeResult.Encoded &&
            payload.ToArray().AsSpan().SequenceEqual(Convert.FromBase64String("Aw8mkDYAEgA="));
    }

    private static void Put(WorldTileStore tiles, int x, int y, ushort type, bool actuator = false) =>
        tiles.Set(x, y, new WorldTile { Type = type, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed |
            (actuator ? WorldTileFlags.Actuator : WorldTileFlags.None) });
}
