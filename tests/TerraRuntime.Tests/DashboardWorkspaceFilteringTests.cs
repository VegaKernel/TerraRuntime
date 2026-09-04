using TerraRuntime.Operations;
using TerraRuntime.TerminalUI;

namespace TerraRuntime.Tests;

public sealed class DashboardWorkspaceFilteringTests
{
    [Fact]
    public void Players_detail_uses_complete_bounded_snapshot_instead_of_viewport_row_limit()
    {
        using var operations = new FilterOperations(playerCount: 24);
        using var workspace = CreateWorkspace(operations);

        workspace.ShowPlayers();

        Assert.StartsWith("PLAYERS  24", workspace.GetRowTextForSmoke(0), StringComparison.Ordinal);
        Assert.Equal(24, workspace.GetVisibleDetailRowCountForSmoke());
        Assert.Contains("Player23", workspace.GetDetailTextForSmoke(), StringComparison.Ordinal);
        Assert.Contains("24 rows", workspace.GetDetailFooterForSmoke(), StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_filter_is_case_insensitive_and_reports_match_count()
    {
        using var operations = new FilterOperations(playerCount: 24);
        using var workspace = CreateWorkspace(operations);

        workspace.ShowPlayers();
        workspace.SetFilterForSmoke("PLAYER23");

        Assert.StartsWith("PLAYERS", workspace.GetRowTextForSmoke(0), StringComparison.Ordinal);
        Assert.Equal(1, workspace.GetVisibleDetailRowCountForSmoke());
        Assert.Contains("Player23", workspace.GetDetailTextForSmoke(), StringComparison.Ordinal);
        Assert.DoesNotContain("Player22", workspace.GetDetailTextForSmoke(), StringComparison.Ordinal);
        Assert.Contains("1/24 rows", workspace.GetDetailFooterForSmoke(), StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_filter_keeps_empty_match_state_explicit()
    {
        using var operations = new FilterOperations(playerCount: 24);
        using var workspace = CreateWorkspace(operations);

        workspace.ShowPlayers();
        workspace.SetFilterForSmoke("missing-player");

        Assert.Equal(0, workspace.GetVisibleDetailRowCountForSmoke());
        Assert.Equal("<no matching entries>", workspace.GetDetailTextForSmoke());
        Assert.Contains("0/24 rows", workspace.GetDetailFooterForSmoke(), StringComparison.Ordinal);
    }

    private static DashboardWorkspaceWindow CreateWorkspace(FilterOperations operations) =>
        new(
            operations,
            operations,
            operations,
            operations,
            operations,
            operations,
            terminalDashboards: null);

    private sealed class FilterOperations :
        IRuntimeDashboardOperations,
        IPlayerOperations,
        INpcOperations,
        INetworkOperations,
        IWorldOperations,
        ILogOperations,
        IDisposable
    {
        private readonly RuntimePlayersSnapshot players;

        public FilterOperations(int playerCount)
        {
            var snapshots = new RuntimePlayerSnapshot[playerCount];
            for (int i = 0; i < snapshots.Length; i++)
            {
                snapshots[i] = new RuntimePlayerSnapshot(
                    ConnectionId: 1000 + i,
                    Slot: checked((byte)i),
                    Generation: 1,
                    Name: $"Player{i:00}",
                    Team: checked((byte)(i % 6)),
                    PositionX: i * 16f,
                    PositionY: i * 8f,
                    VelocityX: 0.5f,
                    VelocityY: -0.25f,
                    SelectedItem: 0,
                    MountType: 0,
                    DifficultyFlags: 0,
                    HasHealth: true,
                    Life: 100,
                    MaxLife: 100,
                    HasMana: true,
                    Mana: 20,
                    MaxMana: 20);
            }

            players = new RuntimePlayersSnapshot(snapshots, DateTimeOffset.UtcNow);
        }

        public RuntimeDashboardSnapshot CaptureSnapshot() => default;

        public bool TrySetInterestManagementEnabled(bool enabled) => true;

        RuntimePlayersSnapshot IPlayerOperations.CaptureSnapshot() => players;

        RuntimeNpcsSnapshot INpcOperations.CaptureSnapshot() => default;

        RuntimeNetworkSnapshot INetworkOperations.CaptureSnapshot() => default;

        RuntimeWorldSnapshot IWorldOperations.CaptureSnapshot() => default;

        RuntimeLogSnapshot ILogOperations.CaptureSnapshot(RuntimeLogQuery query) => default;

        ReadOnlyMemory<string> ILogOperations.CaptureSources(int maxSources) => ReadOnlyMemory<string>.Empty;

        public void Dispose()
        {
        }
    }
}
