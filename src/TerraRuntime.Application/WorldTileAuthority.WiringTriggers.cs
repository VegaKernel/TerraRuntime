using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private RuntimePlayerSnapshotLookup? wiringPlayers;
    private RuntimeWorldClock? wiringClock;
    private readonly Dictionary<int, int> sensorDryCounters = [];
    private List<PlayerStateSnapshot> sensorPlayers = [];
    private readonly PlayerHandle[] weightedOwners = new PlayerHandle[255];

    internal void BindWiringTriggers(RuntimePlayerSnapshotLookup snapshots, RuntimeWorldClock? clock)
    { wiringPlayers = snapshots; wiringClock = clock; }

    internal void ReleaseWeightedPlayer(PlayerHandle player) =>
        tiles?.RuntimePressurePlates?.ResetPlayer(player, PulseWithoutPlayers);

    private void PulseWithoutPlayers(int x, int y)
    {
        if (wiring is null) return;
        _ = wiring.HitSwitchWithoutPlayerTeleportation(x, y);
        // PressurePlateHelper.Poke publishes even if a saved key no longer names a switch.
        replication?.TryPublishHitSwitch(default, new(checked((short)x), checked((short)y)));
    }

    internal void UpdateWeightedTeleportPosition(PlayerStateSnapshot player, float x, float y)
    {
        var (width, height) = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) : (20f, 42f);
        tiles?.RuntimePressurePlates?.Update(player.Player, x, y, (int)width, (int)height, PulseWithoutPlayers);
    }

    internal void TickWeightedPlates()
    {
        if (tiles?.RuntimePressurePlates is not { } pressure || wiringPlayers is null) return;
        for (int slot = 0; slot < weightedOwners.Length; slot++)
        {
            if (!wiringPlayers.TryGetPlayer(new PlayerSlotId((byte)slot), out var player))
            {
                if (weightedOwners[slot].IsAssigned) pressure.ResetPlayer(weightedOwners[slot], PulseWithoutPlayers);
                weightedOwners[slot] = default;
                continue;
            }
            if (weightedOwners[slot] != player.Player)
            {
                pressure.ResetPlayer(player.Player, PulseWithoutPlayers);
                weightedOwners[slot] = player.Player;
            }
            if (player.IsDead || (player.MovementFlags & 64) != 0) continue;
            var (width, height) = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) : (20f, 42f);
            pressure.Update(player.Player, player.PositionX, player.PositionY, (int)width, (int)height, PulseWithoutPlayers);
        }
        // Main runs the load-time release after all player updates, before NPC updates.
        pressure.FirstUpdate(PulseWithoutPlayers);
    }

    internal void TickLogicSensors()
    {
        try { TickLogicSensorsCore(); }
        finally { wiring?.EndLogicSensorUpdate(); }
    }

    private void TickLogicSensorsCore()
    {
        if (wiringPlayers is null || wiringClock is null) return;
        // UpdateStart freezes all eligible player boxes before any sensor's wire effects run.
        var bodies = new List<PlayerStateSnapshot>();
        for (int slot = 0; slot < 255; slot++)
            if (wiringPlayers.TryGetPlayer(new PlayerSlotId((byte)slot), out var player) &&
                !player.IsDead && (player.MovementFlags & 64) == 0) bodies.Add(player);
        sensorPlayers = bodies;
        if (tiles?.RuntimeTileEntities is not { } metadata) return;
        var sensors = metadata.EnumerateRows().Where(e => e.Kind == WorldTileEntityKind.LogicSensor)
            .OrderBy(e => e.PersistedId).ToArray();
        var liveIds = sensors.Select(e => e.PersistedId).ToHashSet();
        foreach (int id in sensorDryCounters.Keys.ToArray()) if (!liveIds.Contains(id)) sensorDryCounters.Remove(id);
        var pulses = new List<(short X, short Y, bool Block)>();
        foreach (var sensor in sensors)
        {
            if (sensor.Payload is not WorldLogicSensorPayload state || state.LogicCheck is < 1 or > 7) continue;
            var tile = tiles.Get(sensor.X, sensor.Y);
            bool above = state.LogicCheck == 3 && IsPlayerAboveLogicSensor(sensor.X, sensor.Y, bodies);
            sensorDryCounters.TryGetValue(sensor.PersistedId, out int counted);
            if (!VanillaLogicSensor1458.TryStep(state.LogicCheck, state.IsOn, counted, wiringClock.DayTime, above,
                tile.LiquidAmount, tile.LiquidKind == WorldLiquidKind.Lava, tile.LiquidKind == WorldLiquidKind.Honey,
                tile.LiquidKind == WorldLiquidKind.Shimmer, out var step)) continue;
            if (step.Changed)
            {
                if (!metadata.TrySetLogicSensorState(sensor, in tile, step.IsOn)) continue;
                PublishWiringSquare(sensor.X, sensor.Y, 1, 1);
                if (step.TripWire) pulses.Add((sensor.X, sensor.Y, step.BlockPlayers));
            }
            sensorDryCounters[sensor.PersistedId] = step.CountedData;
        }
        // TELogicSensor.UpdateEnd invokes deferred pulses only after every sensor adopted its frame.
        foreach (var pulse in pulses)
        {
            if (wiring is null) continue;
            _ = wiring.HitLogicSensorSwitch(pulse.X, pulse.Y, pulse.Block);
            replication?.TryPublishHitSwitch(default, new(pulse.X, pulse.Y));
        }
    }

    // FigureCheckType evaluates player presence against the last UpdateStart census.
    // Liquid checks are initialized without an instance and first sample water on Update.
    private bool IsLogicSensorOnAtPlacement(int x, int y, byte check) => check switch
    {
        1 => wiringClock is { DayTime: true },
        2 => wiringClock is { DayTime: false },
        3 => IsPlayerAboveLogicSensor(x, y, sensorPlayers),
        _ => false
    };

    private static bool IsPlayerAboveLogicSensor(int x, int y, List<PlayerStateSnapshot> players)
    {
        foreach (var player in players)
        {
            var (width, height) = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) : (20f, 42f);
            if (VanillaLogicSensor1458.PlayerAbove(x, y, player.PositionX, player.PositionY, (int)width, (int)height)) return true;
        }
        return false;
    }
}
