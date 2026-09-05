using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts;
using TerraRuntime.Application.Operations;
using TerraRuntime.Application.TerminalUI;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.Tests;

public sealed class RuntimeOverviewDashboardInteractionTests
{
    [Fact]
    public void Dashboard_initialization_repairs_focus_chain_and_focuses_command_input()
    {
        using IApplication app = Terminal.Gui.App.Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(80, 20);
        using var window = new Window
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        var host = new View
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        var dashboard = new RuntimeOverviewDashboard
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        host.Add(dashboard);
        window.Add(host);

        SessionToken token = app.Begin(window)!;
        try
        {
            app.LayoutAndDraw();

            Assert.True(host.CanFocus);
            Assert.True(dashboard.DashboardCanFocusForSmoke);
            Assert.True(dashboard.CommandInputHasFocusForSmoke);
            Assert.True(dashboard.CommandInputFrameUsesAccentForSmoke);
        }
        finally
        {
            app.End(token);
        }
    }

    [Fact]
    public void Dashboard_layout_uses_wide_console_network_row_and_world_player_tree_without_global_tps_tile()
    {
        using IApplication app = Terminal.Gui.App.Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(160, 28);
        using var window = new Window
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        var dashboard = new RuntimeOverviewDashboard
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        window.Add(dashboard);

        SessionToken token = app.Begin(window)!;
        try
        {
            dashboard.Refresh(
                default(RuntimeDashboardSnapshot) with
                {
                    WorldName = "Primary",
                    Port = 7777,
                    TargetTicksPerSecond = 60,
                    ObservedTicksPerSecond = 60d
                },
                default,
                default,
                default,
                default,
                default,
                status: null);
            app.LayoutAndDraw();

            Assert.Equal(3, dashboard.GetVisiblePanelCountForSmoke());
            Assert.Contains("Worlds / Players", dashboard.GetPanelTitleForSmoke("Worlds"));

            var console = dashboard.GetPanelFrameForSmoke("Console");
            var network = dashboard.GetPanelFrameForSmoke("Network");
            var worlds = dashboard.GetPanelFrameForSmoke("Worlds");

            Assert.True(console.Width > worlds.Width);
            Assert.Equal(0, network.Y);
            Assert.Equal(network.Bottom, worlds.Y);
            Assert.True(worlds.Height > network.Height);
            Assert.Contains("Logs INFO+", dashboard.GetFeedControlsForSmoke());
            Assert.Contains("Chat ON", dashboard.GetFeedControlsForSmoke());
            Assert.DoesNotContain("Level", dashboard.GetFeedControlsForSmoke(), StringComparison.OrdinalIgnoreCase);
            Assert.True(dashboard.CommandInputFrameUsesAccentForSmoke);
        }
        finally
        {
            app.End(token);
        }
    }

    [Fact]
    public void Maximized_network_graph_scales_history_across_wide_viewport()
    {
        using IApplication app = Terminal.Gui.App.Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(160, 28);
        using var window = new Window
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        var dashboard = new RuntimeOverviewDashboard
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        window.Add(dashboard);

        SessionToken token = app.Begin(window)!;
        try
        {
            RuntimeDashboardSnapshot runtime = default(RuntimeDashboardSnapshot) with
            {
                WorldName = "Primary",
                TargetTicksPerSecond = 60,
                ObservedTicksPerSecond = 60d
            };

            for (int i = 0; i < 60; i++)
                dashboard.Refresh(runtime with { Tick = i + 1 }, default, default, default, default, default, status: null);

            app.LayoutAndDraw();
            Assert.True(dashboard.GetGraphCellSizeForSmoke("Network").X >= 1f);

            dashboard.TogglePanelForSmoke("Network");
            app.LayoutAndDraw();
            dashboard.Refresh(runtime with { Tick = 61 }, default, default, default, default, default, status: null);
            app.LayoutAndDraw();

            Assert.Equal(1, dashboard.GetVisiblePanelCountForSmoke());
            Assert.True(dashboard.GetGraphCellSizeForSmoke("Network").X < 1f);
        }
        finally
        {
            app.End(token);
        }
    }

    [Fact]
    public void Console_feed_follows_tail_but_preserves_manual_history_scroll()
    {
        using IApplication app = Terminal.Gui.App.Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(80, 20);
        using var window = new Window
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        var dashboard = new RuntimeOverviewDashboard
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        window.Add(dashboard);

        SessionToken token = app.Begin(window)!;
        try
        {
            RuntimeLogSnapshot logs = CreateLogs(48, "Runtime", OperationsLogLevel.Information);
            RuntimeLogSnapshot chat = CreateLogs(32, "Chat", OperationsLogLevel.Information);
            RuntimeDashboardSnapshot first = default(RuntimeDashboardSnapshot) with
            {
                Tick = 100,
                WorldName = "Primary",
                MaxPlayers = 8,
                TargetTicksPerSecond = 60,
                ObservedTicksPerSecond = 60d
            };

            dashboard.Refresh(first, default, default, default, logs, chat, status: null);
            app.LayoutAndDraw();

            Assert.True(dashboard.ConsoleLinesForSmoke > 1);
            Assert.True(dashboard.ConsoleViewportYForSmoke > 0);

            dashboard.ScrollConsoleToTopForSmoke();
            Assert.Equal(0, dashboard.ConsoleViewportYForSmoke);

            RuntimeDashboardSnapshot second = first with { Tick = 101 };
            dashboard.Refresh(second, default, default, default, logs, chat, status: null);
            app.LayoutAndDraw();

            Assert.Equal(0, dashboard.ConsoleViewportYForSmoke);
        }
        finally
        {
            app.End(token);
        }
    }

    [Fact]
    public void Feed_log_mode_and_chat_visibility_are_ui_local_filters()
    {
        using var dashboard = new RuntimeOverviewDashboard();
        DateTimeOffset startedAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        RuntimeLogSnapshot logs = CreateSnapshot(
        [
            new RuntimeLogEntry(1, startedAt.AddSeconds(1), OperationsLogLevel.Debug, "Runtime", "debug-entry"),
            new RuntimeLogEntry(2, startedAt.AddSeconds(2), OperationsLogLevel.Information, "Runtime", "info-entry"),
            new RuntimeLogEntry(3, startedAt.AddSeconds(3), OperationsLogLevel.Warning, "Network", "warn-entry"),
            new RuntimeLogEntry(4, startedAt.AddSeconds(4), OperationsLogLevel.Error, "World", "error-entry")
        ]);
        RuntimeLogSnapshot chat = CreateSnapshot(
        [
            new RuntimeLogEntry(1, startedAt.AddSeconds(2.5), OperationsLogLevel.Information, "Chat", "#3: hello-chat")
        ]);

        dashboard.Refresh(
            default(RuntimeDashboardSnapshot) with { WorldName = "Primary", MaxPlayers = 8 },
            default,
            default,
            default,
            logs,
            chat,
            status: null);

        dashboard.SetFeedForSmoke(logs: false, chat: true, OperationsLogLevel.Debug);
        string chatOnly = dashboard.GetConsoleTextForSmoke();
        Assert.Contains("CHAT #3: hello-chat", chatOnly);
        Assert.DoesNotContain("warn-entry", chatOnly);
        Assert.Contains("Logs OFF", dashboard.GetFeedControlsForSmoke());

        dashboard.SetFeedForSmoke(logs: true, chat: false, OperationsLogLevel.Warning);
        string warnings = dashboard.GetConsoleTextForSmoke();
        Assert.Contains("WARN Network warn-entry", warnings);
        Assert.Contains("ERR  World error-entry", warnings);
        Assert.DoesNotContain("info-entry", warnings);
        Assert.DoesNotContain("hello-chat", warnings);
        Assert.Contains("Logs WARN+", dashboard.GetFeedControlsForSmoke());
        Assert.Contains("Chat OFF", dashboard.GetFeedControlsForSmoke());
    }

    [Fact]
    public void World_tree_projects_current_world_and_players()
    {
        using var dashboard = new RuntimeOverviewDashboard();
        RuntimePlayerSnapshot[] players =
        [
            CreatePlayer(0, 11, "Alice"),
            CreatePlayer(1, 12, "Bob")
        ];

        dashboard.Refresh(
            default(RuntimeDashboardSnapshot) with { WorldName = "Main", MaxPlayers = 8 },
            default,
            default,
            new RuntimePlayersSnapshot(players.AsMemory(), DateTimeOffset.UtcNow),
            default,
            default,
            status: null);

        string tree = dashboard.GetWorldsTextForSmoke();
        Assert.Contains("▼ Main  [primary]", tree);
        Assert.Contains("#0 Alice", tree);
        Assert.Contains("#1 Bob", tree);
    }

    [Fact]
    public void World_tree_projects_primary_sandboxes_and_route_membership()
    {
        WorldRuntimeIdentity primaryIdentity = new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew());
        WorldRuntimeIdentity arenaIdentity = new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew());
        SandboxTreeWorldSnapshot[] worlds =
        [
            new(
                Sandbox: null,
                DisplayName: "Main",
                IsPrimary: true,
                Runtime: default(WorldRuntimeSnapshot) with
                {
                    Identity = primaryIdentity,
                    WorldName = "Main",
                    Lifecycle = WorldRuntimeLifecycle.Running,
                    TargetTicksPerSecond = 60,
                    ObservedTicksPerSecond = 59.8
                },
                PendingJob: null,
                Players: new SandboxTreePlayerSnapshot[]
                {
                    new("#0", 0, "Alice", isPlaying: true)
                }),
            new(
                new SandboxName("arena"),
                "arena",
                IsPrimary: false,
                Runtime: default(WorldRuntimeSnapshot) with
                {
                    Identity = arenaIdentity,
                    WorldName = "Arena World",
                    Lifecycle = WorldRuntimeLifecycle.Running,
                    TargetTicksPerSecond = 120,
                    ObservedTicksPerSecond = 119.6
                },
                PendingJob: null,
                Players: new SandboxTreePlayerSnapshot[]
                {
                    new("#1", 1, "Bob", isPlaying: true)
                })
        ];
        var tree = new SandboxTreeSnapshot(worlds, ReadOnlyMemory<SandboxJobSnapshot>.Empty, DateTimeOffset.UtcNow);
        using var dashboard = new RuntimeOverviewDashboard(sandboxTreeSource: () => tree);

        dashboard.Refresh(
            default(RuntimeDashboardSnapshot) with { WorldName = "Main", MaxPlayers = 8 },
            default,
            default,
            default,
            default,
            default,
            status: null);

        string rendered = dashboard.GetWorldsTextForSmoke();
        Assert.Contains("Main  [primary]", rendered);
        Assert.Contains("TPS 59.8/60", rendered);
        Assert.Contains("#0 Alice", rendered);
        Assert.Contains("arena  [sandbox · running]", rendered);
        Assert.Contains("TPS 119.6/120", rendered);
        Assert.Contains("#1 Bob", rendered);
    }

    [Fact]
    public void World_tree_drag_maps_exact_player_session_to_destination_semantic_target()
    {
        using var tree = new SandboxWorldTreeView();
        var destination = new SandboxName("arena");
        RuntimePlayerSnapshot alice = CreatePlayer(0, 11, "Alice");
        PlayerHandle? player = null;
        SandboxName? target = null;
        tree.TransferRequested += (handle, sandbox) =>
        {
            player = handle;
            target = sandbox;
        };
        tree.SetRows(
        ["primary", "#0 Alice", "arena"],
        [
            new SandboxWorldTreeRow(SandboxWorldTreeRowKind.World, Target: null, PlayerSelector: null),
            new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Player, Target: null, PlayerSelector: "#0", alice),
            new SandboxWorldTreeRow(SandboxWorldTreeRowKind.World, destination, PlayerSelector: null)
        ]);

        Assert.True(tree.TryTransferRows(sourceRow: 1, targetRow: 2));
        Assert.True(player.HasValue);
        Assert.Equal(new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)), player.Value);
        Assert.Equal(destination, target);
    }

    [Fact]
    public void World_tree_drag_keeps_original_player_across_refresh_and_accepts_player_row_as_target()
    {
        using var tree = new SandboxWorldTreeView();
        var arena = new SandboxName("arena");
        RuntimePlayerSnapshot alice = CreatePlayer(0, 11, "Alice");
        RuntimePlayerSnapshot bob = CreatePlayer(1, 12, "Bob");
        RuntimePlayerSnapshot carol = CreatePlayer(2, 13, "Carol");
        PlayerHandle? moved = null;
        SandboxName? target = null;
        tree.TransferRequested += (handle, sandbox) =>
        {
            moved = handle;
            target = sandbox;
        };

        tree.SetRows(
            ["primary", "#0 Alice", "#1 Bob", "arena", "#2 Carol"],
            [
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.World, Target: null, PlayerSelector: null),
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Player, Target: null, PlayerSelector: "#0", alice),
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Player, Target: null, PlayerSelector: "#1", bob),
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.World, arena, PlayerSelector: null),
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Player, arena, PlayerSelector: "#2", carol)
            ]);
        Assert.True(tree.BeginDragForSmoke(2));

        // A dashboard refresh reorders the primary rows while the drag is still active.
        tree.SetRows(
            ["primary", "#1 Bob", "#0 Alice", "arena", "#2 Carol"],
            [
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.World, Target: null, PlayerSelector: null),
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Player, Target: null, PlayerSelector: "#1", bob),
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Player, Target: null, PlayerSelector: "#0", alice),
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.World, arena, PlayerSelector: null),
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Player, arena, PlayerSelector: "#2", carol)
            ]);

        Assert.True(tree.DropDraggedForSmoke(4));
        Assert.True(moved.HasValue);
        Assert.Equal(new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)), moved.Value);
        Assert.Equal(arena, target);
    }

    [Fact]
    public void World_tree_explicit_actions_map_rows_to_typed_semantics()
    {
        using var tree = new SandboxWorldTreeView();
        var arena = new SandboxName("arena");
        SandboxName? destroyed = null;
        string? kicked = null;
        tree.DestroyRequested += sandbox => destroyed = sandbox;
        tree.KickRequested += player => kicked = player;
        tree.SetRows(
            ["primary", "arena", "#4 Bob"],
            [
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.World, Target: null, PlayerSelector: null),
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.World, arena, PlayerSelector: null),
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Player, arena, PlayerSelector: "#4")
            ]);

        Assert.False(tree.TryInvokeActionForSmoke(0));
        Assert.True(tree.TryInvokeActionForSmoke(1));
        Assert.Equal(arena, destroyed);
        Assert.True(tree.TryInvokeActionForSmoke(2));
        Assert.Equal("#4", kicked);
    }

    [Fact]
    public void Player_details_godmode_selection_survives_periodic_refresh_until_apply()
    {
        RuntimePlayerSnapshot alice = CreatePlayer(3, 33, "Alice");
        var players = new FixedPlayerOperations(alice);
        var administration = new FakePlayerAdministration(initialGodMode: false);
        var sessions = new RuntimeConnectionSessionDirectory();
        sessions.Register(alice.ConnectionId, "127.0.0.1", 7777, DateTimeOffset.UtcNow);
        using var window = new PlayerDetailsWindow(alice, players, administration, sessions);

        Assert.False(window.GodModeForSmoke);
        window.SetGodModeForSmoke(enabled: true);
        window.RefreshLiveState();
        Assert.True(window.GodModeForSmoke);

        window.ApplyGodModeForSmoke();

        Assert.True(administration.GodMode);
        Assert.True(administration.LastSetPlayer.HasValue);
        Assert.Equal(window.Player, administration.LastSetPlayer.Value);
    }

    [Fact]
    public void Settings_button_raises_runtime_settings_request()
    {
        using var dashboard = new RuntimeOverviewDashboard();
        int requests = 0;
        dashboard.SettingsRequested += () => requests++;

        Assert.True(dashboard.SettingsButtonEnabledForSmoke);
        dashboard.RequestSettingsForSmoke();

        Assert.Equal(1, requests);
    }

    private static RuntimePlayerSnapshot CreatePlayer(byte slot, long connectionId, string name) =>
        new(
            connectionId,
            slot,
            Generation: 1,
            name,
            Team: 0,
            PositionX: 0,
            PositionY: 0,
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
            MaxMana: 20);

    private sealed class FixedPlayerOperations(RuntimePlayerSnapshot player) : IPlayerOperations
    {
        public RuntimePlayersSnapshot CaptureSnapshot() =>
            new(new[] { player }.AsMemory(), DateTimeOffset.UtcNow);
    }

    private sealed class FakePlayerAdministration(bool initialGodMode) : IPlayerAdministrativeOperations
    {
        public bool GodMode { get; private set; } = initialGodMode;
        public PlayerHandle? LastSetPlayer { get; private set; }

        public ValueTask<bool> SetGodModeAsync(
            PlayerHandle player,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            LastSetPlayer = player;
            GodMode = enabled;
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool?> GetGodModeAsync(
            PlayerHandle player,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<bool?>(GodMode);
    }

    private static RuntimeLogSnapshot CreateLogs(int count, string source, OperationsLogLevel level)
    {
        DateTimeOffset startedAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        RuntimeLogEntry[] entries = Enumerable.Range(1, count)
            .Select(index => new RuntimeLogEntry(
                index,
                startedAt.AddSeconds(index),
                level,
                source,
                $"message-{index:D3}"))
            .ToArray();
        return CreateSnapshot(entries);
    }

    private static RuntimeLogSnapshot CreateSnapshot(RuntimeLogEntry[] entries)
    {
        DateTimeOffset capturedAt = entries.Length == 0 ? DateTimeOffset.UtcNow : entries[^1].TimestampUtc;
        return new RuntimeLogSnapshot(
            entries.AsMemory(),
            PublishedEntries: entries.Length,
            OverwrittenEntries: 0,
            MinimumLevel: OperationsLogLevel.Debug,
            CapturedAtUtc: capturedAt);
    }
}
