using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.HostContracts.WorldGeneration;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.HostModuleFixture;

public sealed class FixtureHostModule : ITerraRuntimeHostModule, ITerraRuntimeHostModuleWorldActivation
{
    public const string DashboardId = "fixture.dashboard";
    public const string WorldGeneratorId = "fixture:worldgen";
    public const string BotId = "fixture:bot";
    public const string MerchantShopId = "fixture:merchant-shop";
    public const string MerchantArchetypeId = "fixture:merchant";
    public const string MerchantControllerId = "fixture:merchant-controller";
    public const string DisabledWorldName = "Fixture Disabled World";
    public const string FaultStartMarker = "fixture-host-module.fail-start";
    public const string FaultAttachMarker = "fixture-host-module.fail-attach";
    public const string FaultDetachMarker = "fixture-host-module.fail-detach";
    public const string FaultStopMarker = "fixture-host-module.fail-stop";

    private string? dataDirectory;
    private ITerraRuntimeTerminalDashboardRegistry? terminalDashboards;
    private IServerPlayerOperations? serverPlayers;
    private INpcActorOperations? npcActors;
    private INpcShopRegistration? merchantShop;
    private INpcArchetypeRegistration? merchantArchetype;
    private NpcHandle merchantNpc;
    private bool botSpawned;

    public string Name => "FixtureHostModule";

    public bool IsEnabledForWorld(TerraRuntimeHostRuntimeInfo world) =>
        !string.Equals(world.WorldName, DisabledWorldName, StringComparison.Ordinal);

    public async ValueTask StartAsync(
        ITerraRuntimeHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);
        dataDirectory = environment.DataDirectory;
        ThrowIfFaultRequested(FaultStartMarker, "start");
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
        ThrowIfFaultRequested(FaultAttachMarker, "attach");

        float botX = Math.Clamp(runtime.Info.WidthTiles * 8f, 16f, runtime.Info.WidthTiles * 16f - 36f);
        float botY = Math.Clamp(runtime.Info.HeightTiles * 5f, 16f, runtime.Info.HeightTiles * 16f - 58f);
        npcActors = runtime.NpcActors;
        var descriptor = new NpcArchetypeDescriptor(
            new GameplayArchetypeId(MerchantArchetypeId),
            VanillaNpcIds.Zombie);
        NpcArchetypeRegistrationStatus archetypeStatus =
            npcActors.TryRegisterArchetype(descriptor, out merchantArchetype);
        if (archetypeStatus != NpcArchetypeRegistrationStatus.Registered || merchantArchetype is null)
        {
            throw new InvalidOperationException(
                $"The fixture merchant archetype could not be registered: {archetypeStatus}.");
        }

        NpcActorSpawnResult merchant = await npcActors
            .SpawnAsync(new NpcActorSpawnRequest(descriptor.Id, botX - 48f, botY), cancellationToken)
            .ConfigureAwait(false);
        if (!merchant.IsSpawned)
            throw new InvalidOperationException($"The fixture merchant NPC could not be spawned: {merchant.Status}.");

        merchantNpc = merchant.Npc;
        var merchantController = new ActorControllerId(MerchantControllerId);
        NpcActorAcquireStatus acquireStatus = await npcActors
            .AcquireAsync(merchantNpc, merchantController, cancellationToken)
            .ConfigureAwait(false);
        if (acquireStatus != NpcActorAcquireStatus.Acquired ||
            !await npcActors
                .SetIntentAsync(merchantNpc, merchantController, NpcActorIntent.Stop(), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException($"The fixture merchant NPC could not be controlled: {acquireStatus}.");
        }

        var catalog = new NpcShopCatalog(
            new ShopId(MerchantShopId),
            descriptor.Id,
            [new ShopOffer(new ShopOfferId("dirt"), VanillaItemIds.DirtBlock, Stack: 1, UnitPrice: 25)]);
        NpcShopRegistrationStatus shopStatus = runtime.NpcShops.TryRegister(catalog, out merchantShop);
        if (shopStatus != NpcShopRegistrationStatus.Registered || merchantShop is null)
            throw new InvalidOperationException($"The fixture merchant shop could not be registered: {shopStatus}.");

        serverPlayers = runtime.ServerPlayers;
        var botId = new ServerPlayerId(BotId);
        ServerPlayerCreateResult bot = await serverPlayers
            .CreateAsync(botId, botX, botY, cancellationToken)
            .ConfigureAwait(false);
        if (!bot.IsCreated)
            throw new InvalidOperationException($"The fixture bot could not be created: {bot.Status}.");

        botSpawned = true;
        if (!await serverPlayers
                .SetAppearanceAsync(botId, CreateBotAppearance(), cancellationToken)
                .ConfigureAwait(false) ||
            !await serverPlayers
                .SetVitalsAsync(botId, new ServerPlayerVitalsState(100, 100, 20, 20), cancellationToken)
                .ConfigureAwait(false) ||
            !await serverPlayers
                .SetMovementIntentAsync(
                    botId,
                    ServerPlayerMovementIntent.MoveTo(botX + 96f, botY),
                    cancellationToken)
                .ConfigureAwait(false))
        {
            await serverPlayers.DespawnAsync(botId, CancellationToken.None).ConfigureAwait(false);
            botSpawned = false;
            throw new InvalidOperationException("The fixture bot state or movement intent was rejected.");
        }

        if (!await npcActors
                .SetIntentAsync(
                    merchantNpc,
                    merchantController,
                    NpcActorIntent.FollowPlayer(bot.Player),
                    cancellationToken)
                .ConfigureAwait(false))
        {
            await serverPlayers.DespawnAsync(botId, CancellationToken.None).ConfigureAwait(false);
            botSpawned = false;
            throw new InvalidOperationException("The fixture merchant follow intent was rejected.");
        }

        string runtimeSummary = $"{runtime.Info.WorldName}|{runtime.Info.Port}|{runtime.InterestManagement.IsEnabled}";
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.attached"),
            runtimeSummary,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfFaultRequested(FaultDetachMarker, "detach");
        if (botSpawned && serverPlayers is not null)
        {
            var botId = new ServerPlayerId(BotId);
            await serverPlayers
                .SetMovementIntentAsync(botId, ServerPlayerMovementIntent.Stop(), cancellationToken)
                .ConfigureAwait(false);
            await serverPlayers.DespawnAsync(botId, cancellationToken).ConfigureAwait(false);
            botSpawned = false;
        }

        if (merchantNpc.IsAssigned && npcActors is not null)
        {
            var controller = new ActorControllerId(MerchantControllerId);
            await npcActors
                .SetIntentAsync(merchantNpc, controller, NpcActorIntent.Stop(), cancellationToken)
                .ConfigureAwait(false);
            await npcActors.ReleaseAsync(merchantNpc, controller, cancellationToken).ConfigureAwait(false);
            await npcActors.DespawnAsync(merchantNpc, cancellationToken).ConfigureAwait(false);
            merchantNpc = default;
        }

        serverPlayers = null;
        npcActors = null;
        // The loader-owned runtime scope retires the registration even when a module drops its lease during detach.
        merchantShop = null;
        merchantArchetype = null;
        if (dataDirectory is null)
            return;

        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.detached"),
            "detached",
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfFaultRequested(FaultStopMarker, "stop");
        terminalDashboards?.TryUnregister(DashboardId);
        terminalDashboards = null;

        if (dataDirectory is null)
            return;

        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.stopped"),
            "stopped",
            cancellationToken).ConfigureAwait(false);
    }

    private void ThrowIfFaultRequested(string markerFileName, string operation)
    {
        if (dataDirectory is not null && File.Exists(Path.Combine(dataDirectory, markerFileName)))
            throw new InvalidOperationException($"Fixture requested {operation} failure.");
    }

    private static ServerPlayerAppearanceState CreateBotAppearance() =>
        new(
            SkinVariant: 0,
            VoiceVariant: 0,
            VoicePitchOffset: 0f,
            Hair: 1,
            Name: "Fixture Bot",
            HairDye: 0,
            HideVisibleAccessory: 0,
            HideMisc: 0,
            HairColor: new PlayerRgbColor(75, 55, 45),
            SkinColor: new PlayerRgbColor(238, 195, 154),
            EyeColor: new PlayerRgbColor(70, 90, 120),
            ShirtColor: new PlayerRgbColor(45, 85, 160),
            UnderShirtColor: new PlayerRgbColor(220, 220, 220),
            PantsColor: new PlayerRgbColor(55, 55, 70),
            ShoeColor: new PlayerRgbColor(35, 25, 20),
            DifficultyFlags: 0,
            TorchAndCartFlags: 0,
            ConsumableUnlockFlags: 0);

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
