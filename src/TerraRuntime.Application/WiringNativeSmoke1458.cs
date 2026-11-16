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
        var torch = tiles.Get(21, 20); torch.FrameX = 44; tiles.Set(21, 20, torch);
        var sourceWorld = new RuntimePlayerUpdateWorld1458(100, 100, 60, false, false)
        {
            GetGoodWorld = false, InfectedSeed = false, NotTheBeesWorld = false, NoTrapsWorld = false,
            TenthAnniversaryWorld = false, DontStarveWorld = false, DrunkWorld = false,
            DualDungeonsSeed = false, JourneyMode = false, HallowBackground = 0,
            OnlyShimmerOceanWorlds = false
        };
        var statueItems = new TerraRuntime.Core.Worlds.RuntimeWorldItemStore();
        var statueNpcs = new TerraRuntime.Core.Npcs.RuntimeNpcStore();
        var state = new ServerRuntimeState(npcs: statueNpcs, worldTiles: tiles, worldItems: statueItems, sourcePlayerWorld: sourceWorld,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 60 },
            townSpawnWorldFacts: default(TerraRuntime.Gameplay.Npcs.VanillaTownSpawnWorldFacts1458));
        var slots = new PlayerSlotPool(1);
        if (!slots.TryAcquireConnection(out var lease) || lease is null) return false;
        using var session = new PlayerJoinSession(lease);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var owner = new ConnectionHandle(GameCommandSourceId.FromConnection(145859), session.Handle);
        state.Apply(new PlayerHealthRuntimeCommand(owner, new(owner.Player.Slot, 100, 100)));
        state.Apply(new PlayerManaRuntimeCommand(owner, new(owner.Player.Slot, 0, 20)));
        state.Apply(new PlayerEquipmentRuntimeCommand(owner, new(owner.Player.Slot, 0, 0, 0, 0, 0)));
        var spawn = new PlayerSpawnCommitRequest(owner.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        state.Apply(new PlayerSpawnRuntimeCommand(owner, session, spawn));
        if (state.LastSpawnCommitResult != PlayerSpawnCommitResult.Committed) return false;
        var sequence = new ReadOnlySequence<byte>(new byte[] { 7, 0, 59, 20, 0, 20, 0 });
        if (TerrariaFrameDecoder.TryRead(ref sequence, out var frame) != TerrariaFrameReadResult.Frame ||
            !TerrariaHitSwitchCodec1458.TryDecode(in frame, out var press)) return false;
        state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, press));
        if (tiles.Get(21, 20).FrameX != 110 || !tiles.Get(22, 20).IsActuated) return false;
        Put(tiles, 20, 20, 144);
        state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, press));
        for (int tick = 0; tick < 59; tick++) state.Tick();
        if (tiles.Get(21, 20).FrameX != 110) return false;
        state.Tick();
        if (tiles.Get(21, 20).FrameX != 44 || tiles.Get(22, 20).IsActuated) return false;
        Put(tiles, 20, 30, 136); Put(tiles, 21, 29, 1); Put(tiles, 21, 33, 1);
        for (int row = 0; row < 3; row++)
            tiles.Set(21, 30 + row, new WorldTile { Type = 10, FrameY = (short)(row * 18),
                Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        var doorPress = new TerrariaHitSwitchState1458(20, 30);
        state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, doorPress));
        if (tiles.Get(21, 30).Type != 11) return false;
        state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, doorPress));
        if (tiles.Get(21, 30).Type != 10) return false;
        // Exercise actual authenticated input -> logic wave -> tile publication in shipping smokes.
        tiles.Set(45, 50, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue });
        for (int x = 46; x <= 51; x++) tiles.Set(x, 50, new WorldTile { Flags = WorldTileFlags.WireBlue });
        tiles.Set(51, 50, new WorldTile { Type = 419, Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue });
        tiles.Set(51, 51, new WorldTile { Type = 420, FrameY = 18, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        tiles.Set(52, 51, new WorldTile { Flags = WorldTileFlags.WireRed });
        tiles.Set(53, 51, new WorldTile { Type = 149, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        var logicPress = new TerrariaHitSwitchState1458(45, 50);
        state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, logicPress));
        if (tiles.Get(51, 50).FrameX != 18 || tiles.Get(51, 51).FrameX != 18 || tiles.Get(53, 51).FrameX != 54) return false;
        state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, logicPress));
        if (tiles.Get(51, 50).FrameX != 0 || tiles.Get(51, 51).FrameX != 0 || tiles.Get(53, 51).FrameX != 0) return false;
        foreach (int style in new[] { 2, 17, 37 })
        {
            int top = style == 2 ? 65 : style == 17 ? 70 : 75;
            for (int x = 55; x <= 60; x++) tiles.Set(x, top, new WorldTile { Flags = WorldTileFlags.WireBlue });
            tiles.Set(55, top, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue });
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
                tiles.Set(60 + dx, top + dy, new WorldTile { Type = 105, FrameX = (short)(style * 36 + dx * 18),
                    FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue });
            state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, new(55, (short)top)));
        }
        if (statueItems.ActiveCount != 3) return false;
        state.Tick();
        // One authenticated input exercises all twelve devices before an outer NPC update.
        // It also preserves the existing per-player edit budget rather than bypassing admission.
        for (int y = 80; y <= 92; y++) tiles.Set(5, y, new WorldTile { Flags = WorldTileFlags.WireYellow });
        foreach (int y in new[] { 82, 87, 92 })
            for (int x = 5; x <= 71; x++) tiles.Set(x, y, new WorldTile { Flags = WorldTileFlags.WireYellow });
        tiles.Set(5, 80, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireYellow });
        int statueIndex = 0;
        foreach (int style in new[] { 4, 5, 7, 8, 9, 10, 13, 18, 27, 30, 35, 42 })
        {
            int left = 10 + statueIndex % 4 * 20, top = 82 + statueIndex / 4 * 5;
            statueIndex++;
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
                tiles.Set(left + dx, top + dy, new WorldTile { Type = 105, FrameX = (short)(style * 36 + dx * 18),
                    FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active | WorldTileFlags.WireYellow });
        }
        state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, new(5, 80)));
        if (statueNpcs.ActiveCount != 12) return false;
        for (byte slot = 0; slot < 12; slot++)
            if (!statueNpcs.TryGetActive(slot, out var born) || !born.Simulation.SpawnedFromStatue ||
                born.Simulation.MoneyValue != 0f || born.Simulation.SpawnSlotCost != 0f || !statueNpcs.HasPendingBirth(born.Handle)) return false;

        foreach (int style in new[] { 40, 41 })
        {
            int type = style == 40 ? 22 : 18;
            var input = new NpcStateUpdate(type, (short)type, 200, 200, 0, 0, 255, default, NpcSimulationState.Initial);
            if (!statueNpcs.TrySpawn((byte)(style - 28), in input, out var resident)) return false;
            int top = style == 40 ? 20 : 25;
            for (int x = 60; x <= 65; x++) tiles.Set(x, top, new WorldTile { Flags = WorldTileFlags.WireBlue });
            tiles.Set(60, top, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue });
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
                tiles.Set(65 + dx, top + dy, new WorldTile { Type = 105, FrameX = (short)(style * 36 + dx * 18),
                    FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue });
            state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, new(60, (short)top)));
            if (!statueNpcs.TryGet(resident.Handle, out var moved) || moved.Simulation.TeleportStyle != 14 ||
                moved.Simulation.TeleportTime != 1f || moved.PositionY == resident.PositionY || moved.Simulation.SpawnedFromStatue) return false;
        }

        for (int kind = 0; kind < 4; kind++)
        {
            int top = 10 + kind * 5;
            for (int x = 5; x <= 15; x++) tiles.Set(x, top, new WorldTile { Flags = WorldTileFlags.WireRed });
            tiles.Set(5, top, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
            foreach (int left in new[] { 8, 14 })
            for (int dx = 0; dx < 2; dx++)
            {
                for (int dy = 0; dy < 2; dy++)
                    tiles.Set(left + dx, top + dy, new WorldTile { Type = (ushort)(left == 8 ? 142 : 143),
                        FrameX = (short)(dx * 18), FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active | WorldTileFlags.WireRed,
                        LiquidAmount = (byte)(left == 8 ? 100 : 0), LiquidKind = (WorldLiquidKind)kind });
                tiles.Set(left + dx, top + 2, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            }
            state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, new(5, (short)top)));
            int volume = 0;
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++)
            {
                if (tiles.Get(8 + dx, top + dy).LiquidAmount != 0) return false;
                volume += tiles.Get(14 + dx, top + dy).LiquidAmount;
            }
            if (volume != 400) return false;
        }

        byte[] importance = new byte[(VanillaWorldFormat326.TileTypeCount + 7) / 8];
        importance[144 >> 3] |= 1 << (144 & 7);
        var timerOnly = new WorldTileStore(new WorldDimensions(1, 1));
        timerOnly.Set(0, 0, new WorldTile { Type = 144, FrameX = 54, FrameY = 18,
            Flags = WorldTileFlags.Active | WorldTileFlagMasks.Wires | WorldTileFlags.Actuator | WorldTileFlags.Inactive });
        using var payload = new MemoryStream();
        return RunNpcRoster() && WorldFileTileEncoder.TryEncode(timerOnly, VanillaWorldFormat326.TileTypeCount, importance,
            payload, out _) == WorldFileTileEncodeResult.Encoded &&
            payload.ToArray().AsSpan().SequenceEqual(Convert.FromBase64String("Aw8mkDYAEgA="));
    }

    private static bool RunNpcRoster()
    {
        var tiles = new WorldTileStore(new WorldDimensions(160, 120));
        var npcs = new TerraRuntime.Core.Npcs.RuntimeNpcStore();
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 60 });
        var slots = new PlayerSlotPool(1);
        if (!slots.TryAcquireConnection(out var lease) || lease is null) return false;
        using var session = new PlayerJoinSession(lease);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var owner = new ConnectionHandle(GameCommandSourceId.FromConnection(145851), session.Handle);
        state.Apply(new PlayerHealthRuntimeCommand(owner, new(owner.Player.Slot, 100, 100)));
        state.Apply(new PlayerManaRuntimeCommand(owner, new(owner.Player.Slot, 0, 20)));
        state.Apply(new PlayerEquipmentRuntimeCommand(owner, new(owner.Player.Slot, 0, 0, 0, 0, 0)));
        state.Apply(new PlayerSpawnRuntimeCommand(owner, session, new(owner.Player.Slot, 20, 20, 0, 0, 0, 0, 0)));
        if (state.LastSpawnCommitResult != PlayerSpawnCommitResult.Committed) return false;
        for (int y = 10; y <= 70; y++) tiles.Set(5, y, new WorldTile { Flags = WorldTileFlags.WireBlue });
        for (int row = 0; row < 6; row++) for (int x = 5; x <= 121; x++)
            tiles.Set(x, 20 + row * 10, new WorldTile { Flags = WorldTileFlags.WireBlue });
        tiles.Set(5, 10, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue });
        int index = 0;
        foreach (int style in new[] { 16, 23, 28, 50 }.Concat(Enumerable.Range(51, 32)))
        {
            int left = 20 + index % 6 * 20, top = 20 + index / 6 * 10;
            index++;
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
                tiles.Set(left + dx, top + dy, new WorldTile { Type = 105,
                    FrameX = (short)(style % 55 * 36 + dx * 18), FrameY = (short)(style / 55 * 54 + dy * 18),
                    Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue });
        }
        state.Apply(new ClientHitSwitchRuntimeCommand1458(owner, new(5, 10)));
        if (npcs.ActiveCount != 36) return false;
        for (byte slot = 0; slot < 36; slot++)
            if (!npcs.TryGetActive(slot, out var born) || !born.Simulation.SpawnedFromStatue ||
                !born.Simulation.CanBeReplacedByOtherNpcs || born.Simulation.MoneyValue != 0f ||
                born.Simulation.SpawnSlotCost != 0f || !npcs.HasPendingBirth(born.Handle)) return false;
        return true;
    }

    private static void Put(WorldTileStore tiles, int x, int y, ushort type, bool actuator = false) =>
        tiles.Set(x, y, new WorldTile { Type = type, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed |
            (actuator ? WorldTileFlags.Actuator : WorldTileFlags.None) });
}
