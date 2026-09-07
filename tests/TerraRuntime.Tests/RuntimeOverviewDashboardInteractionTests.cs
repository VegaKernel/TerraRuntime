using System.Text;
using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Npcs;
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
        using IApplication app = Terminal.Gui.App.Application.Create().Init(DriverRegistry.Names.DOTNET);
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
        using IApplication app = Terminal.Gui.App.Application.Create().Init(DriverRegistry.Names.DOTNET);
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
            Assert.Equal(["+ Sandbox", "+ Bot"], dashboard.WorldActionButtonsForSmoke);
            Assert.True(dashboard.CommandInputFrameUsesAccentForSmoke);
        }
        finally
        {
            app.End(token);
        }
    }

    [Fact]
    public void Bot_add_button_accept_command_reaches_runtime_bot_operations()
    {
        var ingress = new CompletingBotCommandIngress();
        var operations = new RuntimeBotOperations(ingress, new RuntimeBotTelemetry());
        using var dashboard = new RuntimeOverviewDashboard(botOperations: operations);

        Assert.True(dashboard.BotAddEnabledForSmoke);
        Assert.NotNull(dashboard.InvokeBotAddForSmoke());
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref ingress.CreateCount) == 1, TimeSpan.FromSeconds(2)));
        Assert.True(dashboard.HasPendingBotCommandForSmoke);
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                dashboard.PublishBotCommandCompletionForSmoke();
                return dashboard.CommandFeedbackForSmoke.Contains("created Bot 1", StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Maximized_network_chart_expands_history_across_wide_viewport()
    {
        using IApplication app = Terminal.Gui.App.Application.Create().Init(DriverRegistry.Names.DOTNET);
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
            int tiledPlotWidth = dashboard.GetNetworkPlotWidthForSmoke();
            Assert.True(tiledPlotWidth > 0);

            dashboard.TogglePanelForSmoke("Network");
            app.LayoutAndDraw();
            dashboard.Refresh(runtime with { Tick = 61 }, default, default, default, default, default, status: null);
            app.LayoutAndDraw();

            Assert.Equal(1, dashboard.GetVisiblePanelCountForSmoke());
            Assert.True(dashboard.GetNetworkPlotWidthForSmoke() > tiledPlotWidth);
        }
        finally
        {
            app.End(token);
        }
    }

    [Fact]
    public void Network_chart_scales_inbound_and_outbound_independently_so_quiet_direction_stays_visible()
    {
        using var chart = new NetworkTrafficChartView();
        chart.SetSamples(
        [
            new NetworkTrafficSample(InboundPacketsPerSecond: 2048d, OutboundPacketsPerSecond: 2d),
            new NetworkTrafficSample(InboundPacketsPerSecond: 1024d, OutboundPacketsPerSecond: 1d)
        ]);

        Assert.True(chart.InboundScaleMaximumForSmoke >= 2048d);
        Assert.True(chart.OutboundScaleMaximumForSmoke >= 2d);
        Assert.True(chart.InboundScaleMaximumForSmoke > chart.OutboundScaleMaximumForSmoke * 100d);

        (int inboundHeight, int outboundHeight) = chart.GetBarHeightsForSmoke(sampleIndex: 0, plotHeight: 6);
        Assert.True(inboundHeight >= 2);
        Assert.True(outboundHeight >= 2);
        Assert.InRange(Math.Abs(inboundHeight - outboundHeight), 0, 1);
    }

    [Fact]
    public void Network_chart_renders_block_columns_and_both_axes_in_headless_framebuffer()
    {
        using IApplication app = Terminal.Gui.App.Application.Create().Init(DriverRegistry.Names.DOTNET);
        app.Driver!.SetScreenSize(60, 10);
        using var window = new Window
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        using var chart = new NetworkTrafficChartView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        window.Add(chart);

        SessionToken token = app.Begin(window)!;
        try
        {
            chart.SetSamples(
            [
                new NetworkTrafficSample(InboundPacketsPerSecond: 2048d, OutboundPacketsPerSecond: 2d),
                new NetworkTrafficSample(InboundPacketsPerSecond: 512d, OutboundPacketsPerSecond: 1.5d),
                new NetworkTrafficSample(InboundPacketsPerSecond: 0d, OutboundPacketsPerSecond: 1d)
            ]);
            app.LayoutAndDraw();
            Assert.NotNull(app.Driver.Contents);

            var framebuffer = new StringBuilder();
            for (int row = 0; row < app.Driver.Contents!.GetLength(0); row++)
            {
                for (int column = 0; column < app.Driver.Contents.GetLength(1); column++)
                    framebuffer.Append(app.Driver.Contents[row, column]!.Grapheme);
                framebuffer.AppendLine();
            }

            string rendered = framebuffer.ToString();
            Assert.Contains("IN", rendered);
            Assert.Contains("OUT", rendered);
            Assert.Contains('▒', rendered);
            Assert.True(rendered.Contains('█') || rendered.Contains('▓'));
        }
        finally
        {
            app.End(token);
        }
    }

    [Fact]
    public void Console_feed_follows_tail_but_preserves_manual_history_scroll()
    {
        using IApplication app = Terminal.Gui.App.Application.Create().Init(DriverRegistry.Names.DOTNET);
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
        WorldRuntimeIdentity drainingIdentity = new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew());
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
                }),
            new(
                new SandboxName("draining"),
                "draining",
                IsPrimary: false,
                Runtime: default(WorldRuntimeSnapshot) with
                {
                    Identity = drainingIdentity,
                    WorldName = "Draining World",
                    Lifecycle = WorldRuntimeLifecycle.Stopping,
                    TargetTicksPerSecond = 60,
                    ObservedTicksPerSecond = 0d
                },
                PendingJob: null,
                Players: ReadOnlyMemory<SandboxTreePlayerSnapshot>.Empty)
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
        Assert.Contains("arena  [sandbox]", rendered);
        Assert.DoesNotContain("sandbox · running", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("draining  [sandbox · stopping]", rendered);
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
    public void World_tree_drag_source_cannot_be_replaced_by_later_pressed_player_row()
    {
        using var tree = new SandboxWorldTreeView();
        var arena = new SandboxName("arena");
        RuntimePlayerSnapshot alice = CreatePlayer(0, 21, "Alice");
        RuntimePlayerSnapshot bob = CreatePlayer(1, 22, "Bob");
        PlayerHandle? moved = null;
        tree.TransferRequested += (handle, _) => moved = handle;
        tree.SetRows(
            ["primary", "#0 Alice", "#1 Bob", "arena"],
            [
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.World, Target: null, PlayerSelector: null),
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Player, Target: null, PlayerSelector: "#0", alice),
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Player, Target: null, PlayerSelector: "#1", bob),
                new SandboxWorldTreeRow(SandboxWorldTreeRowKind.World, arena, PlayerSelector: null)
            ]);

        Assert.True(tree.BeginDragForSmoke(2));
        // A held-button move over Alice must not recapture the drag source.
        Assert.True(tree.BeginDragForSmoke(1));
        Assert.True(tree.DropDraggedForSmoke(3));

        Assert.True(moved.HasValue);
        Assert.Equal(new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)), moved.Value);
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
    public void Player_details_keeps_routed_godmode_enabled_when_primary_telemetry_does_not_contain_sandbox_player()
    {
        RuntimePlayerSnapshot sandboxPlayer = CreatePlayer(4, 44, "SandboxAlice");
        var players = new EmptyPlayerOperations();
        var administration = new FakePlayerAdministration(initialGodMode: false);
        var sessions = new RuntimeConnectionSessionDirectory();
        sessions.Register(sandboxPlayer.ConnectionId, "127.0.0.1", 7777, DateTimeOffset.UtcNow);
        using var window = new PlayerDetailsWindow(sandboxPlayer, players, administration, sessions);

        Assert.True(window.GodModeControlEnabledForSmoke);
        window.SetGodModeForSmoke(enabled: true);
        window.ApplyGodModeForSmoke();

        Assert.True(administration.GodMode);
        Assert.Equal(window.Player, administration.LastSetPlayer);
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

    private sealed class EmptyPlayerOperations : IPlayerOperations
    {
        public RuntimePlayersSnapshot CaptureSnapshot() =>
            new(ReadOnlyMemory<RuntimePlayerSnapshot>.Empty, DateTimeOffset.UtcNow);
    }

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

    [Fact]
    public void World_tree_bot_row_supports_double_click_open_and_explicit_despawn_action()
    {
        using var tree = new SandboxWorldTreeView();
        var bot = new RuntimeBotSnapshot(
            Id: 7,
            new ServerPlayerId("bot:7"),
            Player: default,
            Npc: new NpcHandle(3, new NpcGeneration(1)),
            Name: "Bot 7",
            new RuntimeBotConfiguration(
                RuntimeBotMode.Guard,
                Target: default,
                Body: RuntimeBotBodyKind.Npc,
                NpcType: VanillaNpcIds.Zombie),
            TargetAvailable: false,
            PvpEnabled: false,
            IsStuck: false,
            TeleportCount: 0,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        tree.SetRows(
            ["  └─ [NpcBot #3] Bot 7  [guard]  [X]"],
            [new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Bot, null, null, Bot: bot)]);

        RuntimeBotSnapshot? opened = null;
        int? despawned = null;
        tree.BotOpenRequested += value => opened = value;
        tree.BotDespawnRequested += id => despawned = id;

        Assert.True(tree.TryOpenRowForSmoke(0));
        Assert.Equal(7, opened?.Id);
        Assert.True(tree.TryInvokeActionForSmoke(0));
        Assert.Equal(7, despawned);
    }

    [Fact]
    public void Bot_settings_show_only_controls_applicable_to_selected_body()
    {
        var operations = new RuntimeBotOperations(new RejectingBotCommandIngress(), new RuntimeBotTelemetry());
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var playerBot = new RuntimeBotSnapshot(
            1,
            new ServerPlayerId("bot:1"),
            new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)),
            default,
            "Bot 1",
            new RuntimeBotConfiguration(
                RuntimeBotMode.Idle,
                default),
            false,
            false,
            false,
            0,
            now);
        var npcBot = playerBot with
        {
            Id = 2,
            ServerPlayerId = new ServerPlayerId("bot:2"),
            Player = default,
            Npc = new NpcHandle(2, new NpcGeneration(1)),
            Configuration = playerBot.Configuration with
            {
                Body = RuntimeBotBodyKind.Npc,
                NpcType = VanillaNpcIds.Zombie,
                FlightEnabled = false
            }
        };

        using var playerWindow = new BotSettingsWindow(playerBot, [], operations);
        Assert.False(playerWindow.NpcPresetVisibleForSmoke);
        Assert.True(playerWindow.PlayerFieldsVisibleForSmoke);
        int expectedNpcTypes = VanillaNpcAiCoverageCatalog.All
            .ToArray()
            .Select(static value => value.Type)
            .Distinct()
            .Count(VanillaBotNpcPresetCatalog1458.IsSupported);
        Assert.Equal(expectedNpcTypes, playerWindow.NpcPresetCountForSmoke);

        using var npcWindow = new BotSettingsWindow(npcBot, [], operations);
        Assert.True(npcWindow.NpcPresetVisibleForSmoke);
        Assert.False(npcWindow.PlayerFieldsVisibleForSmoke);
    }

    private sealed class RejectingBotCommandIngress : IGameCommandIngress<RuntimeCommand>
    {
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command) => false;
    }

    private sealed class CompletingBotCommandIngress : IGameCommandIngress<RuntimeCommand>
    {
        public int CreateCount;

        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            if (source != GameCommandSourceId.System || command is not RuntimeBotCreateCommand create)
                return false;

            Interlocked.Increment(ref CreateCount);
            create.Completion.TrySetResult(new RuntimeBotSnapshot(
                1,
                new ServerPlayerId("bot:1"),
                new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)),
                default,
                "Bot 1",
                new RuntimeBotConfiguration(RuntimeBotMode.Idle, default),
                false,
                false,
                false,
                0,
                DateTimeOffset.UtcNow));
            return true;
        }
    }

}
