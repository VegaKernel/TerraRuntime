using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Buffs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PlayerBuffStateTests
{
    [Fact]
    public void Actual_moon_leech_world_update_applies_buff_once_through_composite_and_commit_sink()
    {
        var authority = new PlayerAuthority(null, null);
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(72), session.Handle);
        var spawn = new PlayerSpawnCommitRequest(session.Slot, 100, 100, 0, 0, 0, 0, 0);
        Assert.True(authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session, spawn)));
        Assert.True(authority.TryCapture(connection.Player, out var player));
        var npcs = new RuntimeNpcStore();
        var head = new NpcStateUpdate(VanillaNpcIds.MoonLordHead.Value, checked((short)VanillaNpcIds.MoonLordHead.Value),
            1000, 1000, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawn(0, in head, out _));
        var projectiles = new RuntimeProjectileStore(4);
        var state = new ProjectileStateUpdate(VanillaProjectileIds.MoonLeech, 255,
            player.PositionX + 10 + 19.75f - 8, player.PositionY + 21 - 8, 2, -3,
            new ProjectileAiState(1, session.Slot.Value, 0), 0, 0, 0, 0);
        Assert.True(projectiles.TrySpawn(0, in state, out var shot));
        var sink = new RuntimeProjectileSimulationCommitSink(new RuntimeProjectileLiveChildSpawnQueue(4),
            new RuntimeCultistLightningArcTrailRegistry(4), authority);
        var stepper = new RuntimeProjectileBehaviorStateStepper(
            new VanillaProjectileWorldStateStepper(new WorldTileStore(new WorldDimensions(400, 400)),
                new SnapshotLookup(player), npcs: npcs),
            new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper>());
        var executor = new RuntimeProjectileStateExecutor(projectiles, sink);
        executor.Tick(stepper);
        Assert.Equal(840, authority.GetBuffDuration(connection.Player, VanillaBuffIds.MoonLeech));
        Assert.True(projectiles.TryGetLifecycle(shot.Handle, out var lifecycle));
        Assert.Equal(1, lifecycle.LocalAi.Ai1);
        var clear = new PlayerBuffTypesCommitRequest(session.Slot, Array.Empty<BuffTypeId>());
        Assert.True(authority.TryApply(new PlayerBuffTypesRuntimeCommand(connection, clear)));
        executor.Tick(stepper);
        Assert.Equal(0, authority.GetBuffDuration(connection.Player, VanillaBuffIds.MoonLeech));
    }

    private sealed class SnapshotLookup(PlayerStateSnapshot snapshot) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        { player = snapshot; return slot == snapshot.Player.Slot; }
    }

    [Fact]
    public void Moon_leech_insertion_refresh_immunity_and_full_slot_order_match_original()
    {
        using Stream resource = typeof(PlayerBuffStateTests).Assembly.GetManifestResourceStream("PlayerBuff1458")!;
        using JsonDocument json = JsonDocument.Parse(resource);
        foreach (JsonElement row in json.RootElement.EnumerateArray())
        {
            int mode = row.GetProperty("mode").GetInt32();
            var state = new PlayerBuffState();
            state.ReplaceNetworkSnapshot(row.GetProperty("beforeTypes").EnumerateArray()
                .Select(value => new BuffTypeId(value.GetInt32())).TakeWhile(type => type.Value != 0).ToArray());
            if (mode is 1 or 2) state.TryApplyMoonLeech(row.GetProperty("beforeTimes")[0].GetInt32());
            state.TryApplyMoonLeech(840, immune: mode == 7);
            var expected = row.GetProperty("types").EnumerateArray().Select(value => value.GetInt32()).Where(type => type != 0).ToArray();
            Assert.Equal(expected, state.CaptureTypes().Select(type => type.Value));
            int leech = Array.IndexOf(expected, 145);
            Assert.Equal(leech < 0 ? 0 : row.GetProperty("times")[leech].GetInt32(), state.GetDuration(VanillaBuffIds.MoonLeech));
        }
    }

    [Fact]
    public void Debuff_eviction_metadata_matches_original_complete_table()
    {
        using Stream resource = typeof(PlayerBuffStateTests).Assembly.GetManifestResourceStream("Debuffs1458")!;
        using JsonDocument json = JsonDocument.Parse(resource);
        Assert.Equal(json.RootElement.EnumerateArray().Select(value => value.GetInt32()),
            Enumerable.Range(0, VanillaBuffIds.Count).Where(value => VanillaBuffDefinitionCatalog.IsDebuff(new BuffTypeId(value))));
    }

    [Fact]
    public void Remote_buff_state_is_replaced_by_snapshots_and_rejects_retired_session()
    {
        var authority = new PlayerAuthority(null, null);
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(71), session.Handle);
        var spawn = new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0);
        Assert.True(authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session, spawn)));
        var application = new ProjectilePlayerBuffApplication(connection.Player, VanillaBuffIds.MoonLeech, 840);
        Assert.True(authority.TryApplyProjectileBuff(in application));
        authority.AdvanceCombatTick(10000);
        Assert.Equal(840, authority.GetBuffDuration(connection.Player, VanillaBuffIds.MoonLeech));
        var snapshot = new PlayerBuffTypesCommitRequest(session.Slot, new[] { VanillaBuffIds.MoonLeech });
        Assert.True(authority.TryApply(new PlayerBuffTypesRuntimeCommand(connection, snapshot)));
        Assert.Equal(60, authority.GetBuffDuration(connection.Player, VanillaBuffIds.MoonLeech));
        var clear = new PlayerBuffTypesCommitRequest(session.Slot, Array.Empty<BuffTypeId>());
        Assert.True(authority.TryApply(new PlayerBuffTypesRuntimeCommand(connection, clear)));
        Assert.Equal(0, authority.GetBuffDuration(connection.Player, VanillaBuffIds.MoonLeech));
        var stale = application with { Target = new PlayerHandle(session.Slot, new PlayerSessionGeneration(connection.Player.Generation.Value + 1)) };
        Assert.False(authority.TryApplyProjectileBuff(in stale));
    }
}
