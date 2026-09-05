using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Application.Operations;
using TerraRuntime.Application.TerminalUI;

namespace TerraRuntime.Tests;

public sealed class TerminalUiWorldInspectionTests
{
    [Fact]
    public void World_scoped_detail_screens_expose_selector_and_preserve_selected_runtime()
    {
        var source = new InspectionOperations();
        using var workspace = new DashboardWorkspaceWindow(
            source,
            source,
            source,
            source,
            source,
            source,
            terminalDashboards: null,
            worldInspectionOperations: source);

        workspace.ShowPlayers();
        Assert.True(workspace.WorldSelectorVisibleForSmoke);
        Assert.Equal(2, workspace.WorldSelectorTargetCountForSmoke);
        Assert.Equal(source.PrimaryId, workspace.SelectedInspectionWorldForSmoke);
        Assert.Contains("PrimaryPlayer", workspace.GetDetailTextForSmoke(), StringComparison.Ordinal);

        Assert.True(workspace.SelectWorldForSmoke(source.SandboxId));
        Assert.Equal(source.SandboxId, workspace.SelectedInspectionWorldForSmoke);
        Assert.Contains("SandboxPlayer", workspace.GetDetailTextForSmoke(), StringComparison.Ordinal);
        Assert.Contains("@ arena", workspace.GetRowTextForSmoke(0), StringComparison.Ordinal);

        workspace.ShowNpcs();
        Assert.True(workspace.WorldSelectorVisibleForSmoke);
        Assert.Equal(source.SandboxId, workspace.SelectedInspectionWorldForSmoke);

        workspace.ShowProjectiles();
        Assert.True(workspace.WorldSelectorVisibleForSmoke);
        Assert.Equal(source.SandboxId, workspace.SelectedInspectionWorldForSmoke);

        workspace.ShowItems();
        Assert.True(workspace.WorldSelectorVisibleForSmoke);
        Assert.Equal(source.SandboxId, workspace.SelectedInspectionWorldForSmoke);

        workspace.ShowWorld();
        Assert.True(workspace.WorldSelectorVisibleForSmoke);
        Assert.Equal(source.SandboxId, workspace.SelectedInspectionWorldForSmoke);
        Assert.Contains("WORLD @ arena", workspace.GetRowTextForSmoke(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Process_scoped_detail_screens_do_not_show_fake_world_selector()
    {
        var source = new InspectionOperations();
        using var workspace = new DashboardWorkspaceWindow(
            source,
            source,
            source,
            source,
            source,
            source,
            terminalDashboards: null,
            worldInspectionOperations: source);

        workspace.ShowNetwork();
        Assert.False(workspace.WorldSelectorVisibleForSmoke);

        workspace.ShowLogs();
        Assert.False(workspace.WorldSelectorVisibleForSmoke);
    }

    [Fact]
    public void Inspection_cache_refreshes_only_demanded_category_for_selected_world()
    {
        var source = new InspectionOperations();
        var cache = new OperationsCache(
            source,
            source,
            source,
            source,
            source,
            source,
            worldInspectionSource: source);
        IRuntimeWorldInspectionOperations inspection = cache;

        Assert.Equal(2, inspection.CaptureTargets().Length);
        Assert.False(inspection.TryCaptureNpcs(source.SandboxId, out _));
        Assert.Equal(0, source.SandboxNpcCaptures);
        Assert.Equal(0, source.SandboxProjectileCaptures);
        Assert.Equal(0, source.SandboxItemCaptures);
        Assert.Equal(0, source.SandboxPlayerCaptures);

        cache.Refresh();

        Assert.True(inspection.TryCaptureNpcs(source.SandboxId, out RuntimeNpcsSnapshot npcs));
        Assert.Equal(1, npcs.Npcs.Length);
        Assert.Equal(1, source.SandboxNpcCaptures);
        Assert.Equal(0, source.SandboxProjectileCaptures);
        Assert.Equal(0, source.SandboxItemCaptures);
        Assert.Equal(0, source.SandboxPlayerCaptures);

        RuntimePlayersSnapshot primaryPlayers = ((IPlayerOperations)cache).CaptureSnapshot();
        Assert.Equal("PrimaryPlayer", primaryPlayers.Players.Span[0].Name);
    }

    private sealed class InspectionOperations :
        IRuntimeDashboardOperations,
        IPlayerOperations,
        INpcOperations,
        INetworkOperations,
        IWorldOperations,
        ILogOperations,
        IRuntimeWorldInspectionOperations
    {
        private readonly RuntimePlayersSnapshot primaryPlayers;
        private readonly RuntimePlayersSnapshot sandboxPlayers;
        private readonly RuntimeNpcsSnapshot primaryNpcs;
        private readonly RuntimeNpcsSnapshot sandboxNpcs;
        private readonly RuntimeProjectilesSnapshot primaryProjectiles;
        private readonly RuntimeProjectilesSnapshot sandboxProjectiles;
        private readonly RuntimeWorldItemsSnapshot primaryItems;
        private readonly RuntimeWorldItemsSnapshot sandboxItems;
        private readonly RuntimeWorldInspectionTarget[] targets;

        public InspectionOperations()
        {
            PrimaryId = new WorldRuntimeId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
            SandboxId = new WorldRuntimeId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
            WorldSessionId primarySession = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
            WorldSessionId sandboxSession = new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

            primaryPlayers = CreatePlayers("PrimaryPlayer", connectionId: 10, slot: 0);
            sandboxPlayers = CreatePlayers("SandboxPlayer", connectionId: 20, slot: 1);
            primaryNpcs = CreateNpcs(slot: 2, type: 22);
            sandboxNpcs = CreateNpcs(slot: 3, type: 33);
            primaryProjectiles = CreateProjectiles(spawner: 0, type: 100);
            sandboxProjectiles = CreateProjectiles(spawner: 1, type: 200);
            primaryItems = CreateItems(itemNetId: 50);
            sandboxItems = CreateItems(itemNetId: 60);

            targets =
            [
                new RuntimeWorldInspectionTarget(
                    PrimaryId,
                    "primary-world",
                    IsPrimary: true,
                    WorldRuntimeLifecycle.Running,
                    primarySession,
                    TargetTicksPerSecond: 60,
                    ObservedTicksPerSecond: 59.8),
                new RuntimeWorldInspectionTarget(
                    SandboxId,
                    "arena",
                    IsPrimary: false,
                    WorldRuntimeLifecycle.Running,
                    sandboxSession,
                    TargetTicksPerSecond: 120,
                    ObservedTicksPerSecond: 119.4)
            ];
        }

        public WorldRuntimeId PrimaryId { get; }
        public WorldRuntimeId SandboxId { get; }
        public int SandboxPlayerCaptures { get; private set; }
        public int SandboxNpcCaptures { get; private set; }
        public int SandboxProjectileCaptures { get; private set; }
        public int SandboxItemCaptures { get; private set; }

        public RuntimeDashboardSnapshot CaptureSnapshot() => default;

        public bool TrySetInterestManagementEnabled(bool enabled) => true;

        RuntimePlayersSnapshot IPlayerOperations.CaptureSnapshot() => primaryPlayers;

        RuntimeNpcsSnapshot INpcOperations.CaptureSnapshot() => primaryNpcs;

        RuntimeNetworkSnapshot INetworkOperations.CaptureSnapshot() => default;

        RuntimeWorldSnapshot IWorldOperations.CaptureSnapshot() => default;

        RuntimeLogSnapshot ILogOperations.CaptureSnapshot(RuntimeLogQuery query) => default;

        ReadOnlyMemory<string> ILogOperations.CaptureSources(int maxSources) => ReadOnlyMemory<string>.Empty;

        public ReadOnlyMemory<RuntimeWorldInspectionTarget> CaptureTargets() => targets;

        public bool TryCaptureRuntime(WorldRuntimeId runtimeId, out WorldRuntimeSnapshot snapshot)
        {
            if (runtimeId == PrimaryId)
            {
                snapshot = CreateRuntimeSnapshot(PrimaryId, targets[0].SessionId, "primary-world", 60, 59.8);
                return true;
            }
            if (runtimeId == SandboxId)
            {
                snapshot = CreateRuntimeSnapshot(SandboxId, targets[1].SessionId, "arena", 120, 119.4);
                return true;
            }

            snapshot = default;
            return false;
        }

        public bool TryCapturePlayers(WorldRuntimeId runtimeId, out RuntimePlayersSnapshot snapshot)
        {
            if (runtimeId == PrimaryId)
            {
                snapshot = primaryPlayers;
                return true;
            }
            if (runtimeId == SandboxId)
            {
                SandboxPlayerCaptures++;
                snapshot = sandboxPlayers;
                return true;
            }

            snapshot = default;
            return false;
        }

        public bool TryCaptureNpcs(WorldRuntimeId runtimeId, out RuntimeNpcsSnapshot snapshot)
        {
            if (runtimeId == PrimaryId)
            {
                snapshot = primaryNpcs;
                return true;
            }
            if (runtimeId == SandboxId)
            {
                SandboxNpcCaptures++;
                snapshot = sandboxNpcs;
                return true;
            }

            snapshot = default;
            return false;
        }

        public bool TryCaptureProjectiles(WorldRuntimeId runtimeId, out RuntimeProjectilesSnapshot snapshot)
        {
            if (runtimeId == PrimaryId)
            {
                snapshot = primaryProjectiles;
                return true;
            }
            if (runtimeId == SandboxId)
            {
                SandboxProjectileCaptures++;
                snapshot = sandboxProjectiles;
                return true;
            }

            snapshot = default;
            return false;
        }

        public bool TryCaptureWorldItems(WorldRuntimeId runtimeId, out RuntimeWorldItemsSnapshot snapshot)
        {
            if (runtimeId == PrimaryId)
            {
                snapshot = primaryItems;
                return true;
            }
            if (runtimeId == SandboxId)
            {
                SandboxItemCaptures++;
                snapshot = sandboxItems;
                return true;
            }

            snapshot = default;
            return false;
        }

        private static RuntimePlayersSnapshot CreatePlayers(string name, long connectionId, byte slot)
        {
            RuntimePlayerSnapshot[] players =
            [
                new RuntimePlayerSnapshot(
                        connectionId,
                        slot,
                        Generation: 1,
                        name,
                        Team: 0,
                        PositionX: 16,
                        PositionY: 32,
                        VelocityX: 0,
                        VelocityY: 0,
                        SelectedItem: 0,
                        MountType: 0,
            DifficultyFlags: 0,
                        HasHealth: true,
                        Life: 100,
                        MaxLife: 100,
                        HasMana: true,
                    Mana: 20,
                    MaxMana: 20)
            ];
            return new RuntimePlayersSnapshot(players.AsMemory(), DateTimeOffset.UnixEpoch);
        }

        private static RuntimeNpcsSnapshot CreateNpcs(byte slot, int type)
        {
            RuntimeNpcSnapshot[] npcs =
            [
                new RuntimeNpcSnapshot(
                        slot,
                        Generation: 1,
                        Revision: 1,
                        type,
                        NetId: checked((short)type),
                        PositionX: 16,
                        PositionY: 32,
                        VelocityX: 0,
                        VelocityY: 0,
                        Target: 0,
                        Ai0: 0,
                        Ai1: 0,
                        Ai2: 0,
                        Ai3: 0,
                        DirectionX: 1,
                        DirectionY: 1,
                        CollideX: false,
                        CollideY: false,
                        Wet: false,
                    NoGravity: false,
                    NoTileCollide: false)
            ];
            return new RuntimeNpcsSnapshot(
                npcs.AsMemory(),
                CommittedSpawns: 1,
                CommittedUpdates: 0,
                CommittedDespawns: 0,
                DateTimeOffset.UnixEpoch);
        }

        private static RuntimeProjectilesSnapshot CreateProjectiles(byte spawner, int type)
        {
            RuntimeProjectileGroupSnapshot[] groups =
            [new RuntimeProjectileGroupSnapshot(spawner, type, 1, 16, 32, 0, 0, 10, 10, 1)];
            return new RuntimeProjectilesSnapshot(
                ActiveProjectiles: 1,
                Groups: groups.AsMemory(),
                CommittedSpawns: 1,
                CommittedUpdates: 0,
                CommittedDespawns: 0,
                DateTimeOffset.UnixEpoch);
        }

        private static RuntimeWorldItemsSnapshot CreateItems(short itemNetId)
        {
            RuntimeWorldItemGroupSnapshot[] groups =
            [new RuntimeWorldItemGroupSnapshot(itemNetId, 1, 1, 0, 0, 1, 16, 32)];
            return new RuntimeWorldItemsSnapshot(
                ActiveItems: 1,
                Groups: groups.AsMemory(),
                DateTimeOffset.UnixEpoch);
        }

        private static WorldRuntimeSnapshot CreateRuntimeSnapshot(
            WorldRuntimeId runtimeId,
            WorldSessionId sessionId,
            string worldName,
            int targetTps,
            double observedTps) =>
            new(
                new WorldRuntimeIdentity(runtimeId, sessionId),
                worldName,
                new SandboxWorldSource.WorldFile($"{worldName}.wld"),
                WorldPersistenceMode.Ephemeral,
                WorldRuntimeLifecycle.Running,
                Tick: 123,
                TargetTicksPerSecond: targetTps,
                ObservedTicksPerSecond: observedTps,
                Connections: 1,
                Npcs: 1,
                Projectiles: 1,
                WorldItems: 1,
                Fault: null);
    }
}
