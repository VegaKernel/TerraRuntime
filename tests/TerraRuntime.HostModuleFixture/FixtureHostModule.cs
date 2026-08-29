using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.HostContracts.WorldGeneration;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.HostModuleFixture;

public sealed class FixtureHostModule : ITerraRuntimeHostModule
{
    public const string DashboardId = "fixture.dashboard";
    public const string WorldGeneratorId = "fixture:worldgen";

    private string? dataDirectory;
    private ITerraRuntimeTerminalDashboardRegistry? terminalDashboards;

    public string Name => "FixtureHostModule";

    public async ValueTask StartAsync(
        ITerraRuntimeHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);
        dataDirectory = environment.DataDirectory;
        terminalDashboards = environment.TerminalDashboards;
        if (!terminalDashboards.TryRegister(new FixtureDashboardProvider()))
            throw new InvalidOperationException("The fixture terminal dashboard could not be registered.");

        TerraRuntimeWorldGeneratorRegistrationResult generatorRegistration =
            environment.WorldGenerators.TryRegister(new FixtureWorldGenerator(), out _);
        if (generatorRegistration != TerraRuntimeWorldGeneratorRegistrationResult.Registered)
        {
            throw new InvalidOperationException(
                $"The fixture world generator could not be registered: {generatorRegistration}.");
        }

        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.started"),
            environment.ServerPluginsDirectory,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask AttachRuntimeAsync(
        ITerraRuntimeHostRuntime runtime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (dataDirectory is null)
            throw new InvalidOperationException("The fixture host module has not been started.");

        string runtimeSummary = $"{runtime.Info.WorldName}|{runtime.Info.Port}|{runtime.InterestManagement.IsEnabled}";
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.attached"),
            runtimeSummary,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (dataDirectory is null)
            return;

        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.detached"),
            "detached",
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        terminalDashboards?.TryUnregister(DashboardId);
        terminalDashboards = null;

        if (dataDirectory is null)
            return;

        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.stopped"),
            "stopped",
            cancellationToken).ConfigureAwait(false);
    }

    private sealed class FixtureWorldGenerator : IWorldGenerationProvider
    {
        public WorldGeneratorId Id => new(WorldGeneratorId);

        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            request.Validate();
            builder.Add(
                new WorldGenerationPassDescriptor(new WorldGenerationPassId("fixture:terrain")),
                new FixtureWorldGenerationPass());
        }
    }

    private sealed class FixtureWorldGenerationPass : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.CancellationToken.ThrowIfCancellationRequested();

            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            var marker = new WorldGenerationTile(
                Type: 0,
                Wall: 0,
                FrameX: 0,
                FrameY: 0,
                Flags: WorldGenerationTileFlags.Active,
                LiquidAmount: 0,
                TileColor: 0,
                WallColor: 0,
                Shape: 0,
                LiquidKind: WorldGenerationLiquidKind.Water);
            if (!context.Workspace.TrySetTile(0, height - 1, in marker))
                throw new InvalidOperationException("Fixture generator could not write its deterministic marker tile.");

            IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
                throw new InvalidOperationException("Fixture generator requires the runtime metadata workspace.");
            int anchorY = Math.Max(0, height / 3);
            if (!metadata.TrySetSpawn(width / 2, anchorY))
                throw new InvalidOperationException("Fixture generator could not set spawn metadata.");
            if (!metadata.TrySetDungeon(Math.Max(0, width / 4), anchorY))
                throw new InvalidOperationException("Fixture generator could not set dungeon metadata.");

            double worldSurface = height * 0.40d;
            double rockLayer = height * 0.65d;
            if (!metadata.TrySetLayers(worldSurface, rockLayer))
                throw new InvalidOperationException("Fixture generator could not set layer metadata.");

            context.ReportProgress(1d, "Fixture custom generator complete");
        }
    }

    private sealed class FixtureDashboardProvider : ITerraRuntimeTerminalDashboardProvider
    {
        public string Id => DashboardId;

        public string Title => "Fixture Dashboard";

        public View CreateDashboard()
        {
            var root = new View
            {
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            root.Add(new Label
            {
                X = 1,
                Y = 1,
                Text = "Fixture dashboard"
            });
            return root;
        }

        public void Refresh(View rootView)
        {
            ArgumentNullException.ThrowIfNull(rootView);
        }
    }
}
