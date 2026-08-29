using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.ExtensibleHost;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.HostModuleFixture;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;

namespace TerraRuntime.Tests;

public sealed class TrustedHostModuleLoaderTests
{
    [Fact]
    public async Task Runtime_scope_retires_actor_controllers_and_shop_registrations()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var npcActors = new TestNpcActorOperations(acquire: true);
        var npcShops = new TestNpcShopOperations();
        var source = new TestHostRuntime(
            new TerraRuntimeHostRuntimeInfo("Scope World", "scope.wld", 100, 100, 7777, 8),
            new TestInterestManagementControl(),
            new TestPlayerStateSnapshotReader(),
            npcActors,
            npcShops,
            new TestServerPlayerOperations());
        var scope = new ScopedHostRuntime(source);
        var npc = new NpcHandle(1, new NpcGeneration(1));
        var controller = new ActorControllerId("fixture:controller");
        var archetype = new NpcArchetypeDescriptor(
            new GameplayArchetypeId("fixture:scope-actor"),
            VanillaNpcIds.Zombie);

        Assert.Equal(
            NpcArchetypeRegistrationStatus.Registered,
            scope.NpcActors.TryRegisterArchetype(archetype, out _));
        NpcActorSpawnResult spawned = await scope.NpcActors.SpawnAsync(
            new NpcActorSpawnRequest(archetype.Id, 100f, 100f),
            cancellationToken);
        Assert.True(spawned.IsSpawned);

        Assert.Equal(
            NpcActorAcquireStatus.Acquired,
            await scope.NpcActors.AcquireAsync(npc, controller, cancellationToken));
        var catalog = new NpcShopCatalog(
            new ShopId("fixture:scope-shop"),
            new GameplayArchetypeId("fixture:scope-merchant"),
            [new ShopOffer(new ShopOfferId("dirt"), VanillaItemIds.DirtBlock, 1, 25)]);
        Assert.Equal(
            NpcShopRegistrationStatus.Registered,
            scope.NpcShops.TryRegister(catalog, out _));

        await scope.RetireAsync();

        Assert.Equal(1, npcActors.ReleasedControllerCount);
        Assert.Equal(1, npcActors.DespawnCount);
        Assert.Equal(1, npcActors.ArchetypeRetiredCount);
        Assert.Equal(1, npcShops.RetiredCount);
        Assert.Equal(
            NpcActorAcquireStatus.QueueRejected,
            await scope.NpcActors.AcquireAsync(npc, controller, cancellationToken));
        Assert.Equal(
            NpcShopRegistrationStatus.RuntimeDetached,
            scope.NpcShops.TryRegister(catalog, out _));
    }

    [Fact]
    public async Task StartAttachDetachStop_LoadsDropInModule_ThroughContractBoundary()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Path.Combine(Path.GetTempPath(), $"terraruntime-host-{Guid.NewGuid():N}");
        string hostModules = Path.Combine(root, "HostModules");
        string serverPlugins = Path.Combine(root, "ServerPlugins");
        string worlds = Path.Combine(root, "Worlds");
        string config = Path.Combine(root, "config");
        string data = Path.Combine(root, "data");
        string logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(hostModules);
        Directory.CreateDirectory(serverPlugins);
        Directory.CreateDirectory(worlds);
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(logs);

        string modulePath = Path.Combine(hostModules, "Vega.dll");
        File.Copy(typeof(FixtureHostModule).Assembly.Location, modulePath);

        var loader = new TrustedHostModuleLoader(hostModules);
        var environment = new TestHostEnvironment(
            root,
            hostModules,
            serverPlugins,
            worlds,
            config,
            data,
            logs,
            loader.TerminalDashboards,
            loader.WorldGenerators);
        var interestManagement = new TestInterestManagementControl();
        var serverPlayers = new TestServerPlayerOperations();
        var npcShops = new TestNpcShopOperations();
        var npcActors = new TestNpcActorOperations(acquire: true);
        var runtime = new TestHostRuntime(
            new TerraRuntimeHostRuntimeInfo(
                "Fixture World",
                Path.Combine(worlds, "fixture.wld"),
                8400,
                2400,
                7777,
                32),
            interestManagement,
            new TestPlayerStateSnapshotReader(),
            npcActors,
            npcShops,
            serverPlayers);

        WorldGeneratorId fixtureGeneratorId = new(FixtureHostModule.WorldGeneratorId);
        try
        {
            int loaded = await loader.StartAllAsync(environment, cancellationToken);
            await loader.AttachRuntimeAsync(runtime, cancellationToken);

            Assert.Equal(1, loaded);
            string startedMarker = Path.Combine(data, "fixture-host-module.started");
            Assert.True(File.Exists(startedMarker));
            Assert.Equal(
                serverPlugins,
                await File.ReadAllTextAsync(startedMarker, cancellationToken));

            ReadOnlyMemory<ITerraRuntimeTerminalDashboardProvider> dashboardProviders = loader.CaptureDashboards();
            Assert.Single(dashboardProviders.ToArray());
            ITerraRuntimeTerminalDashboardProvider provider = dashboardProviders.Span[0];
            Assert.Equal(FixtureHostModule.DashboardId, provider.Id);
            Assert.Equal("Fixture Dashboard", provider.Title);

            ReadOnlyMemory<WorldGeneratorId> generatorIds = loader.CaptureWorldGeneratorIds();
            Assert.Single(generatorIds.ToArray());
            Assert.Equal(fixtureGeneratorId, generatorIds.Span[0]);
            Assert.True(loader.TryResolveWorldGenerator(fixtureGeneratorId, out IWorldGenerationProvider? generator));
            Assert.NotNull(generator);
            Assert.Equal(fixtureGeneratorId, generator.Id);

            using (IApplication app = Application.Create().Init(DriverRegistry.Names.ANSI))
            using (View dashboard = provider.CreateDashboard())
            {
                Assert.NotNull(dashboard);
                provider.Refresh(dashboard);
            }

            string attachedMarker = Path.Combine(data, "fixture-host-module.attached");
            Assert.True(File.Exists(attachedMarker));
            Assert.Equal(
                "Fixture World|7777|False",
                await File.ReadAllTextAsync(attachedMarker, cancellationToken));
            Assert.Equal(1, serverPlayers.CreateCount);
            Assert.Equal("Fixture Bot", serverPlayers.Appearance?.Name);
            Assert.Equal(new ServerPlayerVitalsState(100, 100, 20, 20), serverPlayers.Vitals);
            Assert.Equal(
                ServerPlayerMovementIntentKind.MoveTo,
                serverPlayers.MovementIntent?.Kind);
            NpcShopCatalog merchantCatalog = Assert.IsType<NpcShopCatalog>(npcShops.Catalog);
            Assert.Equal(new ShopId(FixtureHostModule.MerchantShopId), merchantCatalog.Id);
            Assert.Equal(
                new GameplayArchetypeId(FixtureHostModule.MerchantArchetypeId),
                merchantCatalog.NpcArchetypeId);
            ShopOffer merchantOffer = Assert.Single(merchantCatalog.Offers.ToArray());
            Assert.Equal(VanillaItemIds.DirtBlock, merchantOffer.ItemType);
            Assert.Equal(25, merchantOffer.UnitPrice);
            Assert.Equal(1, npcActors.SpawnCount);
            Assert.Equal(NpcActorIntentKind.FollowPlayer, npcActors.Intent?.Kind);
            Assert.Equal(new PlayerSlotId(7), npcActors.Intent?.TargetPlayer.Slot);

            await loader.DetachRuntimeAsync(cancellationToken);
            Assert.Equal(1, serverPlayers.DespawnCount);
            Assert.Equal(ServerPlayerMovementIntentKind.Stop, serverPlayers.MovementIntent?.Kind);
            Assert.Equal(1, npcShops.RetiredCount);
            Assert.Equal(1, npcActors.DespawnCount);
            Assert.Equal(1, npcActors.ArchetypeRetiredCount);
            Assert.Equal(NpcActorIntentKind.Stop, npcActors.Intent?.Kind);
            Assert.True(File.Exists(Path.Combine(data, "fixture-host-module.detached")));

            var disabledRuntime = runtime with
            {
                Info = runtime.Info with { WorldName = FixtureHostModule.DisabledWorldName }
            };
            await loader.AttachRuntimeAsync(disabledRuntime, cancellationToken);
            await loader.DetachRuntimeAsync(cancellationToken);

            Assert.Equal(1, serverPlayers.CreateCount);
            Assert.Equal(1, npcActors.SpawnCount);
            Assert.Equal(1, npcShops.RetiredCount);

            int reloaded = await loader.ReloadAllAsync(environment, runtime, cancellationToken);
            Assert.Equal(1, reloaded);
            Assert.Equal(2, serverPlayers.CreateCount);
            Assert.Equal(2, npcActors.SpawnCount);
            await loader.DetachRuntimeAsync(cancellationToken);
            Assert.Equal(2, serverPlayers.DespawnCount);
            Assert.Equal(2, npcActors.DespawnCount);
            Assert.Equal(2, npcActors.ArchetypeRetiredCount);
            Assert.Equal(2, npcShops.RetiredCount);
        }
        finally
        {
            await loader.DisposeAsync();
        }

        Assert.Empty(loader.CaptureDashboards().ToArray());
        Assert.Empty(loader.CaptureWorldGeneratorIds().ToArray());
        Assert.False(loader.TryResolveWorldGenerator(fixtureGeneratorId, out _));
        Assert.True(File.Exists(Path.Combine(data, "fixture-host-module.stopped")));

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A collectible AssemblyLoadContext becomes reclaimable asynchronously; cleanup is best-effort on Windows.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort cleanup rule as above.
        }
    }

    private sealed record TestHostEnvironment(
        string RootDirectory,
        string HostModulesDirectory,
        string ServerPluginsDirectory,
        string WorldsDirectory,
        string ConfigDirectory,
        string DataDirectory,
        string LogsDirectory,
        ITerraRuntimeTerminalDashboardRegistry TerminalDashboards,
        ITerraRuntimeWorldGeneratorRegistry WorldGenerators) : ITerraRuntimeHostEnvironment;

    private sealed record TestHostRuntime(
        TerraRuntimeHostRuntimeInfo Info,
        IInterestManagementControl InterestManagement,
        IPlayerStateSnapshotReader PlayerStates,
        INpcActorOperations NpcActors,
        INpcShopOperations NpcShops,
        IServerPlayerOperations ServerPlayers) : ITerraRuntimeHostRuntime;

    private sealed class TestNpcShopOperations : INpcShopOperations
    {
        public NpcShopCatalog? Catalog { get; private set; }
        public int RetiredCount { get; private set; }

        public NpcShopRegistrationStatus TryRegister(
            NpcShopCatalog catalog,
            out INpcShopRegistration? registration)
        {
            Catalog = catalog;
            registration = new TestNpcShopRegistration(catalog, () => RetiredCount++);
            return NpcShopRegistrationStatus.Registered;
        }
    }

    private sealed class TestNpcShopRegistration : INpcShopRegistration
    {
        private readonly Action retired;
        private NpcShopCatalog? current;

        public TestNpcShopRegistration(NpcShopCatalog catalog, Action retired)
        {
            current = catalog;
            this.retired = retired;
            ShopId = catalog.Id;
            NpcArchetypeId = catalog.NpcArchetypeId;
        }

        public ShopId ShopId { get; }
        public GameplayArchetypeId NpcArchetypeId { get; }

        public bool TryReplaceCatalog(NpcShopCatalog replacement)
        {
            if (current is null || replacement.Id != ShopId || replacement.NpcArchetypeId != NpcArchetypeId)
                return false;

            current = replacement;
            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref current, null) is not null)
                retired();
        }
    }

    private sealed class TestInterestManagementControl : IInterestManagementControl
    {
        public bool IsEnabled { get; private set; }

        public bool SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled)
                return false;

            IsEnabled = enabled;
            return true;
        }
    }

    private sealed class TestPlayerStateSnapshotReader : IPlayerStateSnapshotReader
    {
        public ValueTask<PlayerStateSnapshot?> CaptureAsync(
            PlayerHandle player,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<PlayerStateSnapshot?>(null);
        }
    }

    private sealed class TestServerPlayerOperations : IServerPlayerOperations
    {
        private static readonly PlayerHandle BotPlayer = new(
            new PlayerSlotId(7),
            new PlayerSessionGeneration(1));

        public int CreateCount { get; private set; }
        public int DespawnCount { get; private set; }
        public ServerPlayerAppearanceState? Appearance { get; private set; }
        public ServerPlayerVitalsState? Vitals { get; private set; }
        public ServerPlayerMovementIntent? MovementIntent { get; private set; }

        public ValueTask<ServerPlayerCreateResult> CreateAsync(
            ServerPlayerId id,
            float positionX,
            float positionY,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateCount++;
            return ValueTask.FromResult(new ServerPlayerCreateResult(ServerPlayerCreateStatus.Created, BotPlayer));
        }

        public ValueTask<bool> SetHorizontalIntentAsync(
            ServerPlayerId id,
            ServerPlayerHorizontalIntent intent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }

        public ValueTask<bool> SetJumpIntentAsync(
            ServerPlayerId id,
            ServerPlayerJumpIntent intent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }

        public ValueTask<bool> SetMovementIntentAsync(
            ServerPlayerId id,
            ServerPlayerMovementIntent intent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MovementIntent = intent;
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> SetAppearanceAsync(
            ServerPlayerId id,
            ServerPlayerAppearanceState appearance,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Appearance = appearance;
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> SetVitalsAsync(
            ServerPlayerId id,
            ServerPlayerVitalsState vitals,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Vitals = vitals;
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> SetItemAsync(
            ServerPlayerId id,
            ServerPlayerItemState item,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }

        public ValueTask<bool> DespawnAsync(
            ServerPlayerId id,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DespawnCount++;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class TestNpcActorOperations(bool acquire = false) : INpcActorOperations
    {
        private static readonly NpcHandle SpawnedNpc = new(2, new NpcGeneration(1));

        public int ReleasedControllerCount { get; private set; }
        public int SpawnCount { get; private set; }
        public int DespawnCount { get; private set; }
        public int ArchetypeRetiredCount { get; private set; }
        public NpcActorIntent? Intent { get; private set; }

        public NpcArchetypeRegistrationStatus TryRegisterArchetype(
            NpcArchetypeDescriptor descriptor,
            out INpcArchetypeRegistration? registration)
        {
            registration = new TestNpcArchetypeRegistration(descriptor.Id, () => ArchetypeRetiredCount++);
            return NpcArchetypeRegistrationStatus.Registered;
        }

        public ValueTask<NpcActorSpawnResult> SpawnAsync(
            NpcActorSpawnRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpawnCount++;
            return ValueTask.FromResult(new NpcActorSpawnResult(NpcActorSpawnStatus.Spawned, SpawnedNpc));
        }

        public ValueTask<bool> DespawnAsync(
            NpcHandle npc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DespawnCount++;
            return ValueTask.FromResult(true);
        }

        public ValueTask<NpcActorAcquireStatus> AcquireAsync(
            NpcHandle npc,
            ActorControllerId controllerId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                acquire ? NpcActorAcquireStatus.Acquired : NpcActorAcquireStatus.UnsupportedNpcType);
        }

        public ValueTask<bool> SetIntentAsync(
            NpcHandle npc,
            ActorControllerId controllerId,
            NpcActorIntent intent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Intent = intent;
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> ReleaseAsync(
            NpcHandle npc,
            ActorControllerId controllerId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }

        public ValueTask<int> ReleaseControllerAsync(
            ActorControllerId controllerId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleasedControllerCount++;
            return ValueTask.FromResult(1);
        }
    }

    private sealed class TestNpcArchetypeRegistration(
        GameplayArchetypeId id,
        Action retired) : INpcArchetypeRegistration
    {
        private int disposed;

        public GameplayArchetypeId Id { get; } = id;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                retired();
        }
    }
}
