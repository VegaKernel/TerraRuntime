using System.Runtime.CompilerServices;
using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Network;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Tiles;
using ProjectileDefinitions = TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

// Independent CLIENT-side ordinary pick driver. Packet125 carries THIS hit's
// damage, never the accumulated cache. Source damage/501 rules are separately
// bound to the actual original ItemCheck and HitTile numeric fixtures.
internal static class MiningSourceScene1458
{
    private sealed class Client { internal ToolHitState1458 Hits = ToolHitState1458.Constructor; internal bool Started; internal int ReportedCalls; internal ushort BirthGeneration = 1; }
    private static readonly ConditionalWeakTable<ServerRuntimeState, Dictionary<PlayerHandle, Client>> clients = new();
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    private static extern ref ServerRuntimeComposition Composition(ServerRuntimeState state);

    internal static RuntimePlayerUpdateWorld1458 Facts(WorldTileStore tiles, double surface = 80) =>
        new(tiles.Dimensions.WidthTiles, tiles.Dimensions.HeightTiles, surface, false, false)
        {
            GetGoodWorld = false, InfectedSeed = false, NotTheBeesWorld = false,
            NoTrapsWorld = false, TenthAnniversaryWorld = false, DontStarveWorld = false,
            DrunkWorld = false, JourneyMode = false, OnlyShimmerOceanWorlds = false,
            HallowBackground = 0
        };

    internal static TerrariaFrame ObservationFrame(RuntimeCommand command)
    {
        byte[] bytes;
        switch (command)
        {
            case ClientTileManipulationRuntimeCommand tile:
                Assert.Equal(TerrariaTileManipulationEncodeResult.Encoded,
                    TerrariaTileManipulationCodec.TryEncode(tile.State, out bytes)); break;
            case ClientTilePickingRuntimeCommand1458 picking:
                bytes = TerrariaTilePickingCodec1458.Encode(picking.State); break;
            case ClientToolChestUpdatesRuntimeCommand1458 chest:
                bytes = TerrariaChestUpdatesCodec1458.Encode(chest.State); break;
            case ClientProjectileUpdateRuntimeCommand projectile:
                Assert.True(TerrariaProjectileEncoder.TryEncodeUpdate(projectile.State, out bytes)); break;
            default: throw new InvalidOperationException("Not a source tool observation.");
        }
        var buffer = new ReadOnlySequence<byte>(bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out var frame));
        return frame;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_InnerQueue")]
    private static extern BoundedOutboundQueue InnerQueue(TerrariaConnectionOutboundQueue queue);

    internal static TerrariaFrame TakeFrame(TerrariaConnectionOutboundQueue outbound)
    {
        Assert.True(InnerQueue(outbound).TryRead(out var bytes));
        var buffer = new ReadOnlySequence<byte>(bytes.Bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame,TerrariaFrameDecoder.TryRead(ref buffer,out var frame));
        Assert.Equal(0,buffer.Length);
        return frame;
    }

    internal static WorldTileAuthority World(ServerRuntimeState state) => Composition(state).WorldTileAuthority;

    internal static void AssertRetainedWithoutAuthority(ServerRuntimeState state,
        WorldTileStore tiles, ConnectionHandle connection, int x, int y)
    {
        var players = Composition(state).Players;
        Assert.True(players.TryGet(connection, out var before));
        Assert.NotNull(before.ItemPhase);
        ulong serial = before.ItemPhase!.Value.ToolUse!.ActionSerial;
        var image = tiles.Get(x, y);
        long allocations = state.AppliedWorldItemAllocations;
        var input = before.ItemPhase.Value.ToolUse.Input;
        int reports = 0;
        if (input is not null)
        {
            foreach (var observation in input.Observations) if (observation.Tile is not null) reports++;
        }
        else Assert.True(World(state).RejectedToolReports > 0);
        Assert.True(World(state).RetainedToolReports > 0);
        long rejected = state.RejectedClientTileManipulations;
        Assert.True(World(state).TryPrepareWorldProjectileRandom(out var random));
        Move();
        Assert.True(random!.IsCurrent); // Denial/correction grants no Main RNG work.
        Assert.Equal(rejected + reports, state.RejectedClientTileManipulations);
        long once = state.RejectedClientTileManipulations;
        Move();
        Assert.Equal(once, state.RejectedClientTileManipulations);
        state.Tick();
        Assert.True(players.TryGet(connection, out var after));
        Assert.Equal(image, tiles.Get(x, y));
        Assert.Equal(allocations, state.AppliedWorldItemAllocations);
        Assert.NotNull(after.ItemPhase);
        Assert.Equal(serial, after.ItemPhase!.Value.ToolUse!.ActionSerial);
        Assert.False(after.ItemPhase.Value.ToolUse.Input?.IsCurrent ?? false);
        void Move() => state.Apply(new PlayerMovementRuntimeCommand(connection,
            new(connection.Player.Slot, 64, 16, 0, 64, before.SelectedItem,
                before.PositionX, before.PositionY, false, 0, 0, false, 0,
                false, 0, 0, 0, 0, false, 0, 0)));
    }

    internal static int ReportedCalls(ServerRuntimeState state, ConnectionHandle connection) =>
        clients.GetOrCreateValue(state)[connection.Player].ReportedCalls;

    internal static ConnectionHandle Connection(ServerRuntimeState state, PlayerHandle player)
    {
        Assert.True(Composition(state).Players.TryGet(player.Slot, out var member));
        Assert.Equal(player, member.Connection.Player);
        return member.Connection;
    }

    internal static void FinishUse(ServerRuntimeState state, ConnectionHandle connection)
    {
        if (!clients.TryGetValue(state, out var map) || !map.TryGetValue(connection.Player, out var client) || !client.Started) return;
        var players = Composition(state).Players;
        Assert.True(players.TryGet(connection, out var member));
        state.Apply(new PlayerMovementRuntimeCommand(connection,
            new(connection.Player.Slot, 64, 16, 0, 64, member.SelectedItem,
                member.PositionX, member.PositionY, false, 0, 0, false, 0,
                false, 0, 0, 0, 0, false, 0, 0)));
        // Release is owned input. Held AI terminates through its source cancel
        // branch; no raw projectile destroy or private clock write is used.
        for (int tick = 0; tick < 240; tick++)
        {
            state.Tick();
            Assert.True(players.TryGet(connection, out member));
            Assert.NotNull(member.ItemPhase);
            var clocks = member.ItemPhase!.Value.ToolUse!.Clocks.Selected;
            if (clocks.Animation == 0 && clocks.ItemTime == 0) { client.Started = false; return; }
        }
        throw new InvalidOperationException("Released source tool did not finish its clocks.");
    }

    internal static void Start(ServerRuntimeState state, ConnectionHandle connection, Action<RuntimeCommand>? transport = null)
    {
        var players = Composition(state).Players;
        Assert.True(players.TryGet(connection, out var member));
        if (!players.TryGetInventoryItem(connection, member.SelectedItem, out var selected) ||
            !VanillaToolCatalog1458.TryGet(selected.ItemType, out _)) return;
        var map = clients.GetOrCreateValue(state);
        if (!map.TryGetValue(connection.Player, out var client)) map.Add(connection.Player, client = new());
        if (client.Started) return;
        state.Apply(new PlayerHealthRuntimeCommand(connection, new(connection.Player.Slot, 100, 100)));
        state.Apply(new PlayerManaRuntimeCommand(connection, new(connection.Player.Slot, 0, 20)));
        state.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
        Move(false); state.Tick(); Move(true);
        Assert.True(players.TryGetInventoryItem(connection, member.SelectedItem, out selected));
        Assert.True(VanillaToolCatalog1458.TryResolve(selected.ItemType, selected.Prefix, false,
            OperatingSystem.IsWindows(), out var definition));
        if (definition.Shoot > 0)
        {
            Assert.True(VanillaToolCatalog1458.TryGetLaunchStats(definition, 1f, 1f, false, false,
                OperatingSystem.IsWindows(), out var launch));
            Assert.True(ProjectileDefinitions.TryGet(launch.Projectile, out var projectile));
            Assert.True(players.TryGet(connection, out var pose));
            var birth = new ClientProjectileUpdateRuntimeCommand(connection,
                new(new(connection.Player.Slot.Value, 921, client.BirthGeneration++), (short)launch.Projectile.Value,
                    pose.PositionX + 10 - projectile.Width * .5f, pose.PositionY + 21 - projectile.Height * .5f,
                    launch.Speed, 0, 0, 0, 0, 0, (short)launch.Damage, launch.KnockBack, 0));
            if (transport is null) state.Apply(birth); else transport(birth);
        }
        state.Tick(); client.Started = true;
        void Move(bool use)
        {
            Assert.True(players.TryGet(connection, out var current));
            state.Apply(new PlayerMovementRuntimeCommand(connection,
                new(connection.Player.Slot, (byte)(64 | (use ? 32 : 0)), 16, 0, 64,
                    current.SelectedItem, current.PositionX, current.PositionY,
                    false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        }
    }

    internal static void ShovelAction(ServerRuntimeState state, WorldTileStore tiles,
        ConnectionHandle connection, int centerX, int centerY, Action<RuntimeCommand>? transport = null)
    {
        Start(state, connection, transport);
        var client = clients.GetOrCreateValue(state)[connection.Player];
        var players = Composition(state).Players;
        for (int tick = 0; ; tick++)
        {
            Assert.True(tick < 120);
            Assert.True(players.TryGet(connection, out var member));
            Assert.NotNull(member.ItemPhase);
            var clocks = member.ItemPhase!.Value.ToolUse!.Clocks;
            if (clocks.Selected.Animation > 0 && clocks.Selected.ItemTime == 0 && clocks.ToolTime == 0) break;
            state.Tick();
        }
        client.ReportedCalls = 0;
        // Player source loops x-major then y; dirt receives power30, hence
        // damage60 on EACH nominal call. One complete nine-cell cohort, not
        // nine independent requests whose centers are inferred from packet17.
        for (int x = centerX - 1; x <= centerX + 1; x++)
        for (int y = centerY - 1; y <= centerY + 1; y++)
        {
            var tile = tiles.Get(x, y);
            if (!tile.IsActive) continue;
            Assert.Equal(0, tile.Type);
            Assert.True(client.Hits.TryHitObject(x, y, 1, out var hits, out int slot));
            Assert.True(hits.TryAddDamage(slot, 60, true, out hits, out int total, out _));
            client.Hits = total >= 100 ? hits.Clear(slot) : hits;
            Send(new ClientTileManipulationRuntimeCommand(connection,
                new(0, (short)x, (short)y, (short)(total >= 100 ? 0 : 1), 0)));
            client.ReportedCalls++;
            if (total < 100) Send(new ClientTilePickingRuntimeCommand1458(connection,
                new(connection.Player.Slot.Value, (short)x, (short)y, 60)));
        }
        Assert.True(players.TryGet(connection, out var current));
        state.Apply(new PlayerMovementRuntimeCommand(connection,
            new(connection.Player.Slot, 96, 16, 0, 64, current.SelectedItem,
                current.PositionX, current.PositionY, false, 0, 0, false, 0,
                false, 0, 0, 0, 0, false, 0, 0)));
        void Send(RuntimeCommand observation)
        {
            if (transport is null) state.Apply(observation); else transport(observation);
        }
    }

    internal static void Apply(ServerRuntimeState state, WorldTileStore tiles,
        ClientTileManipulationRuntimeCommand command, Action<RuntimeCommand>? transport = null, Action? beforeCompleting = null)
    {
        var players = Composition(state).Players;
        Assert.True(players.TryGet(command.Connection, out var member));
        if (!players.TryGetInventoryItem(command.Connection, member.SelectedItem, out var selected) ||
            !VanillaToolCatalog1458.TryResolve(selected.ItemType, selected.Prefix, false,
                OperatingSystem.IsWindows(), out var tool))
        { state.Apply(command); return; }
        var map = clients.GetOrCreateValue(state);
        if (!map.TryGetValue(command.Connection.Player, out var client))
            map.Add(command.Connection.Player, client = new());
        client.ReportedCalls = 0;
        if (!client.Started) Start(state, command.Connection, transport);
        if (tool.Item.Value == 4711 ||
            (command.State.Action == 0 ? tool.Pick == 0 : command.State.Action != 2 || tool.Hammer == 0))
        {
            client.ReportedCalls = 1;
            if (transport is null) state.Apply(command); else transport(command);
            return;
        }
        Assert.True(players.TryGet(command.Connection, out var currentControls));
        if ((currentControls.ControlFlags & 32) == 0) { Move(true); state.Tick(); }
        int x = command.State.TileX, y = command.State.TileY;
        // Only ordinary pick families are completed here. A forged success on
        // transforming/protected tiles remains the original hostile report.
        var original = tiles.Get(x, y);
        bool wall = command.State.Action == 2;
        if (!wall && original.Type is not (0 or 1 or 53 or 147 or 49 or 136 or 427 or 439 or 21))
        { state.Apply(command); Move(true); return; }
        Span<ToolTargetTile1458> window = stackalloc ToolTargetTile1458[25];
        for (int attempt = 0; attempt < 16; attempt++)
        {
            WaitReady();
            for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
            {
                var tile = tiles.Get(x + dx, y + dy);
                window[(dy + 2) * 5 + dx + 2] = new(tile.TileType, tile.FrameX, tile.FrameY, tile.IsActive, tile.Wall);
            }
            var world = new ToolDamageWorld1458(tiles.Dimensions.WidthTiles, tiles.Dimensions.HeightTiles,
                80, tiles.Dimensions.HeightTiles - 200, false, false, false, false, false);
            Assert.True(client.Hits.TryHitObject(x, y, wall ? 2 : 1, out var hits, out int slot));
            int damage;
            if (wall) Assert.True(VanillaToolDamage1458.TryGetWallDamage(tool.Hammer, out damage));
            else
            {
                Assert.True(VanillaToolDamage1458.TryGetPickDamage(tool.Pick, x, y, window, world, out var preliminary));
                Assert.True(VanillaToolDamage1458.TryFinalizePickDamage(preliminary, original.TileType,
                    true, false, true, hits, slot, tool.Pick, -1, out damage));
            }
            Assert.True(hits.TryAddDamage(slot, damage, true, out hits, out int accumulated, out _));
            client.Hits = hits;
            bool complete = accumulated >= 100;
            if (command.State.Data != 0 && complete)
            { state.Apply(command); Move(true); return; }
            if (complete) beforeCompleting?.Invoke();
            client.ReportedCalls++;
            Send(new ClientTileManipulationRuntimeCommand(command.Connection,
                command.State with { Data = (short)(complete && original.Type != 21 ? 0 : 1) }));
            if (!complete && !wall)
                Send(new ClientTilePickingRuntimeCommand1458(command.Connection,
                    new(command.Connection.Player.Slot.Value, (short)x, (short)y, (byte)damage)));
            if (complete && !wall && original.Type == 21)
                Send(new ClientToolChestUpdatesRuntimeCommand1458(command.Connection,
                    new(1, (short)x, (short)y, 0, 0)));
            if (complete) client.Hits = client.Hits.Clear(slot);
            Move(true); // Actual next13 is the bounded cohort end, not a cursor.
            if (complete || command.State.Data != 0) return;
        }
        throw new InvalidOperationException("Independent ordinary source pick did not complete within sixteen hits.");

        void Send(RuntimeCommand observation)
        {
            if (transport is null) state.Apply(observation); else transport(observation);
        }
        void Move(bool use)
        {
            Assert.True(players.TryGet(command.Connection, out var current));
            state.Apply(new PlayerMovementRuntimeCommand(command.Connection,
                new(command.Connection.Player.Slot, (byte)(64 | (use ? 32 : 0)), 16, 0, 64,
                    current.SelectedItem, current.PositionX, current.PositionY,
                    false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        }
        void WaitReady()
        {
            for (int tick = 0; tick < 120; tick++)
            {
                Assert.True(players.TryGet(command.Connection, out var current));
                Assert.True(current.ItemPhase.HasValue,
                    $"Tool phase missing at ready-wait tick{tick}, Updates={state.Updates}, selected={current.SelectedItem}, " +
                    $"tool={tool.Item.Value}, target={x},{y}, applied={state.AppliedClientTileManipulations}, " +
                    $"source target active={tiles.Get(x,y).IsActive}, pose={current.PositionX},{current.PositionY}");
                var clocks = current.ItemPhase!.Value.ToolUse!.Clocks;
                if ((tool.Channel || clocks.Selected.ItemTime == 0) && clocks.ToolTime == 0 && clocks.Selected.Animation > 0) return;
                state.Tick();
            }
            throw new InvalidOperationException("Owned tool clock never reached the source action boundary.");
        }
    }
}
