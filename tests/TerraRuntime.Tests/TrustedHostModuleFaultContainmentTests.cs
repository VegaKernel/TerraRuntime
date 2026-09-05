using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Extensibility;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.HostModuleFixture;
using Terminal.Gui.ViewBase;

namespace TerraRuntime.Tests;

public sealed class TrustedHostModuleFaultContainmentTests
{
    [Fact]
    public async Task Bad_image_module_does_not_abort_healthy_neighbor_or_publish_resources()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var root = new TemporaryHostLayout();
        File.WriteAllBytes(Path.Combine(root.HostModules, "A-Broken.dll"), [0x54, 0x52, 0x2D, 0x42, 0x41, 0x44]);
        File.Copy(typeof(FixtureHostModule).Assembly.Location, Path.Combine(root.HostModules, "B-Fixture.dll"));
        using var diagnostics = new StringWriter();
        await using var loader = new TrustedHostModuleLoader(
            root.HostModules,
            TrustedHostModuleLoadPolicy.Resilient,
            diagnostics);

        int loaded = await loader.StartAllAsync(root.CreateEnvironment(loader), cancellationToken);

        Assert.Multiple(() =>
        {
            Assert.Equal(1, loaded);
            Assert.Single(loader.CaptureDashboards().ToArray());
            Assert.Single(loader.CaptureWorldGeneratorIds().ToArray());
            Assert.Empty(loader.CaptureFaults().ToArray());
        });
    }

    [Fact]
    public async Task Resilient_startup_keeps_healthy_module_when_neighbor_fails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var root = new TemporaryHostLayout();
        File.Copy(typeof(FixtureHostModule).Assembly.Location, Path.Combine(root.HostModules, "A-Fixture.dll"));
        File.Copy(typeof(FixtureHostModule).Assembly.Location, Path.Combine(root.HostModules, "Z-Duplicate.dll"));
        using var diagnostics = new StringWriter();
        await using var loader = new TrustedHostModuleLoader(
            root.HostModules,
            TrustedHostModuleLoadPolicy.Resilient,
            diagnostics);

        int loaded = await loader.StartAllAsync(root.CreateEnvironment(loader), cancellationToken);
        TrustedHostModuleFault fault = Assert.Single(loader.CaptureFaults().ToArray());

        Assert.Multiple(() =>
        {
            Assert.Equal(1, loaded);
            Assert.Equal("Z-Duplicate.dll", fault.FileName);
            Assert.Equal(TrustedHostModuleFaultPhase.Startup, fault.Phase);
            Assert.False(fault.Required);
            Assert.Contains(nameof(InvalidOperationException), fault.Detail, StringComparison.Ordinal);
            Assert.Contains("already loaded", fault.Detail, StringComparison.OrdinalIgnoreCase);
            AssertHealthDashboardPresent(loader, 2);
            Assert.Single(loader.CaptureWorldGeneratorIds().ToArray());
            Assert.Contains("Trusted host module fault", diagnostics.ToString(), StringComparison.Ordinal);
            Assert.Contains(nameof(InvalidOperationException), diagnostics.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Strict_startup_rolls_back_previously_loaded_modules()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var root = new TemporaryHostLayout();
        File.Copy(typeof(FixtureHostModule).Assembly.Location, Path.Combine(root.HostModules, "A-Fixture.dll"));
        File.Copy(typeof(FixtureHostModule).Assembly.Location, Path.Combine(root.HostModules, "Z-Duplicate.dll"));
        using var diagnostics = new StringWriter();
        await using var loader = new TrustedHostModuleLoader(
            root.HostModules,
            TrustedHostModuleLoadPolicy.Strict,
            diagnostics);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await loader.StartAllAsync(root.CreateEnvironment(loader), cancellationToken));
        TrustedHostModuleFault fault = Assert.Single(loader.CaptureFaults().ToArray());

        Assert.Multiple(() =>
        {
            Assert.Contains("Required trusted host module", failure.Message, StringComparison.Ordinal);
            Assert.True(fault.Required);
            AssertHealthDashboardPresent(loader, 1);
            Assert.Empty(loader.CaptureWorldGeneratorIds().ToArray());
        });
    }

    [Fact]
    public async Task Optional_runtime_attach_failure_retires_module_and_keeps_loader_alive()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var root = new TemporaryHostLayout();
        File.Copy(typeof(FixtureHostModule).Assembly.Location, Path.Combine(root.HostModules, "Fixture.dll"));
        using var diagnostics = new StringWriter();
        await using var loader = new TrustedHostModuleLoader(
            root.HostModules,
            TrustedHostModuleLoadPolicy.Resilient,
            diagnostics);
        var actors = new RejectingActorOperations();
        var runtime = new TestRuntime(
            new RuntimeInfo(
                "Fault World",
                Path.Combine(root.Worlds, "fault.wld"),
                8400,
                2400,
                7777,
                16),
            new InterestControl(),
            new NullPlayerSnapshots(),
            actors,
            new RejectingShopOperations(),
            new RejectingServerPlayers());

        Assert.Equal(1, await loader.StartAllAsync(root.CreateEnvironment(loader), cancellationToken));
        await loader.AttachRuntimeAsync(runtime, cancellationToken);

        TrustedHostModuleFault attachFault = Assert.Single(
            loader.CaptureFaults().ToArray(),
            fault => fault.Phase == TrustedHostModuleFaultPhase.RuntimeAttach);
        Assert.Multiple(() =>
        {
            Assert.False(attachFault.Required);
            Assert.Contains("could not be controlled", attachFault.Detail, StringComparison.OrdinalIgnoreCase);
            AssertHealthDashboardPresent(loader, 1);
            Assert.Empty(loader.CaptureWorldGeneratorIds().ToArray());
            Assert.True(actors.ArchetypeRetiredCount >= 1);
            Assert.True(actors.DespawnCount >= 1);
        });

        // The runtime session still has a valid lifecycle state even though its only optional module was retired.
        await loader.DetachRuntimeAsync(cancellationToken);
    }

    [Fact]
    public async Task Optional_start_failure_is_attributed_and_leaves_no_registrations()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var root = new TemporaryHostLayout();
        File.Copy(typeof(FixtureHostModule).Assembly.Location, Path.Combine(root.HostModules, "Fixture.dll"));
        File.WriteAllText(Path.Combine(root.Data, FixtureHostModule.FaultStartMarker), "fault");
        using var diagnostics = new StringWriter();
        await using var loader = new TrustedHostModuleLoader(
            root.HostModules,
            TrustedHostModuleLoadPolicy.Resilient,
            diagnostics);

        int loaded = await loader.StartAllAsync(root.CreateEnvironment(loader), cancellationToken);
        TrustedHostModuleFault fault = Assert.Single(
            loader.CaptureFaults().ToArray(),
            candidate => candidate.Phase == TrustedHostModuleFaultPhase.Startup);

        Assert.Multiple(() =>
        {
            Assert.Equal(0, loaded);
            Assert.False(fault.Required);
            Assert.Contains("requested start failure", fault.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(nameof(InvalidOperationException), fault.Detail, StringComparison.Ordinal);
            AssertHealthDashboardPresent(loader, 1);
            Assert.Empty(loader.CaptureWorldGeneratorIds().ToArray());
        });
    }

    [Fact]
    public async Task Optional_runtime_detach_failure_retires_only_faulty_module()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var root = new TemporaryHostLayout();
        File.Copy(typeof(FixtureHostModule).Assembly.Location, Path.Combine(root.HostModules, "Fixture.dll"));
        using var diagnostics = new StringWriter();
        await using var loader = new TrustedHostModuleLoader(
            root.HostModules,
            TrustedHostModuleLoadPolicy.Resilient,
            diagnostics);
        var actors = new PermissiveActorOperations();
        var runtime = new TestRuntime(
            new RuntimeInfo(
                "Detach Fault World",
                Path.Combine(root.Worlds, "detach-fault.wld"),
                8400,
                2400,
                7777,
                16),
            new InterestControl(),
            new NullPlayerSnapshots(),
            actors,
            new PermissiveShopOperations(),
            new PermissiveServerPlayers());

        Assert.Equal(1, await loader.StartAllAsync(root.CreateEnvironment(loader), cancellationToken));
        await loader.AttachRuntimeAsync(runtime, cancellationToken);
        File.WriteAllText(Path.Combine(root.Data, FixtureHostModule.FaultDetachMarker), "fault");

        await loader.DetachRuntimeAsync(cancellationToken);

        TrustedHostModuleFault fault = Assert.Single(
            loader.CaptureFaults().ToArray(),
            candidate => candidate.Phase == TrustedHostModuleFaultPhase.RuntimeDetach);
        Assert.Multiple(() =>
        {
            Assert.False(fault.Required);
            Assert.Contains("requested detach failure", fault.Message, StringComparison.OrdinalIgnoreCase);
            AssertHealthDashboardPresent(loader, 1);
            Assert.Empty(loader.CaptureWorldGeneratorIds().ToArray());
            Assert.True(actors.ArchetypeRetiredCount >= 1);
            Assert.True(actors.DespawnCount >= 1);
        });
    }

    [Fact]
    public async Task Stop_failure_is_contained_and_loader_owned_registrations_are_retired()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var root = new TemporaryHostLayout();
        File.Copy(typeof(FixtureHostModule).Assembly.Location, Path.Combine(root.HostModules, "Fixture.dll"));
        using var diagnostics = new StringWriter();
        var loader = new TrustedHostModuleLoader(
            root.HostModules,
            TrustedHostModuleLoadPolicy.Resilient,
            diagnostics);

        Assert.Equal(1, await loader.StartAllAsync(root.CreateEnvironment(loader), cancellationToken));
        File.WriteAllText(Path.Combine(root.Data, FixtureHostModule.FaultStopMarker), "fault");

        await loader.DisposeAsync();

        TrustedHostModuleFault fault = Assert.Single(
            loader.CaptureFaults().ToArray(),
            candidate => candidate.Phase == TrustedHostModuleFaultPhase.Stop);
        Assert.Multiple(() =>
        {
            Assert.Contains("requested stop failure", fault.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(nameof(InvalidOperationException), fault.Detail, StringComparison.Ordinal);
            AssertHealthDashboardPresent(loader, 1);
            Assert.Empty(loader.CaptureWorldGeneratorIds().ToArray());
        });
    }

    [Fact]
    public void Environment_policy_can_require_selected_modules_without_making_every_module_fatal()
    {
        TrustedHostModuleLoadPolicy policy = TrustedHostModuleLoadPolicy.FromEnvironment(
            name => name == TrustedHostModuleLoadPolicy.RequiredModulesEnvironmentVariable
                ? "Vega.dll;Metrics.dll"
                : null);

        Assert.Multiple(() =>
        {
            Assert.False(policy.RequireAllModules);
            Assert.True(policy.IsRequired(Path.Combine("HostModules", "Vega.dll")));
            Assert.True(policy.IsRequired(Path.Combine("HostModules", "metrics.DLL")));
            Assert.False(policy.IsRequired(Path.Combine("HostModules", "Optional.dll")));
        });
    }

    [Fact]
    public void Environment_policy_star_requires_every_host_module()
    {
        TrustedHostModuleLoadPolicy policy = TrustedHostModuleLoadPolicy.FromEnvironment(
            _ => "*");

        Assert.True(policy.RequireAllModules);
        Assert.True(policy.IsRequired(Path.Combine("HostModules", "Anything.dll")));
    }

    [Fact]
    public void Dashboard_scope_retires_provider_without_module_cooperation()
    {
        var registry = new TerminalDashboardRegistry();
        using TerminalDashboardRegistry.Scope scope = registry.CreateScope();

        Assert.True(scope.TryRegister(new TestDashboard()));
        Assert.Single(registry.CaptureDashboards().ToArray());

        scope.Dispose();

        Assert.Empty(registry.CaptureDashboards().ToArray());
    }

    private static void AssertHealthDashboardPresent(TrustedHostModuleLoader loader, int expectedDashboardCount)
    {
        IDashboardProvider[] dashboards = loader.CaptureDashboards().ToArray();
        Assert.Equal(expectedDashboardCount, dashboards.Length);
        IDashboardProvider health = Assert.Single(
            dashboards,
            provider => provider.Id == TrustedHostModuleHealthDashboardProvider.DashboardId);
        using View root = health.CreateDashboard();
        health.Refresh(root);
    }

    private sealed class TemporaryHostLayout : IDisposable
    {
        public TemporaryHostLayout()
        {
            Root = Path.Combine(Path.GetTempPath(), $"terraruntime-fault-host-{Guid.NewGuid():N}");
            HostModules = Path.Combine(Root, "HostModules");
            ServerPlugins = Path.Combine(Root, "ServerPlugins");
            Worlds = Path.Combine(Root, "Worlds");
            Config = Path.Combine(Root, "config");
            Data = Path.Combine(Root, "data");
            Logs = Path.Combine(Root, "logs");
            foreach (string directory in new[] { HostModules, ServerPlugins, Worlds, Config, Data, Logs })
                Directory.CreateDirectory(directory);
        }

        public string Root { get; }
        public string HostModules { get; }
        public string ServerPlugins { get; }
        public string Worlds { get; }
        public string Config { get; }
        public string Data { get; }
        public string Logs { get; }

        public IEnvironment CreateEnvironment(TrustedHostModuleLoader loader) =>
            new TestEnvironment(
                Root,
                HostModules,
                ServerPlugins,
                Worlds,
                Config,
                Data,
                Logs,
                loader.TerminalDashboards,
                loader.WorldGenerators);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed record TestEnvironment(
        string RootDirectory,
        string HostModulesDirectory,
        string ServerPluginsDirectory,
        string WorldsDirectory,
        string ConfigDirectory,
        string DataDirectory,
        string LogsDirectory,
        IDashboardRegistry TerminalDashboards,
        IGeneratorRegistry WorldGenerators) : IEnvironment;

    private sealed record TestRuntime(
        RuntimeInfo Info,
        IInterestManagementControl InterestManagement,
        IPlayerStateSnapshotReader PlayerStates,
        INpcActorOperations NpcActors,
        INpcShopOperations NpcShops,
        IServerPlayerOperations ServerPlayers) : IRuntime;

    private sealed class InterestControl : IInterestManagementControl
    {
        public bool IsEnabled { get; private set; }

        public bool SetEnabled(bool enabled)
        {
            bool changed = IsEnabled != enabled;
            IsEnabled = enabled;
            return changed;
        }
    }

    private sealed class NullPlayerSnapshots : IPlayerStateSnapshotReader
    {
        public ValueTask<PlayerStateSnapshot?> CaptureAsync(
            PlayerHandle player,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PlayerStateSnapshot?>(null);
    }

    private sealed class PermissiveActorOperations : INpcActorOperations
    {
        private static readonly NpcHandle SpawnedNpc = new(6, new NpcGeneration(1));
        public int ArchetypeRetiredCount { get; private set; }
        public int DespawnCount { get; private set; }

        public NpcArchetypeRegistrationStatus TryRegisterArchetype(
            NpcArchetypeDescriptor descriptor,
            out INpcArchetypeRegistration? registration)
        {
            registration = new Registration(descriptor.Id, () => ArchetypeRetiredCount++);
            return NpcArchetypeRegistrationStatus.Registered;
        }

        public ValueTask<NpcActorSpawnResult> SpawnAsync(
            NpcActorSpawnRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new NpcActorSpawnResult(NpcActorSpawnStatus.Spawned, SpawnedNpc));

        public ValueTask<bool> DespawnAsync(NpcHandle npc, CancellationToken cancellationToken = default)
        {
            DespawnCount++;
            return ValueTask.FromResult(true);
        }

        public ValueTask<NpcActorAcquireStatus> AcquireAsync(
            NpcHandle npc, ActorControllerId controllerId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(NpcActorAcquireStatus.Acquired);

        public ValueTask<bool> SetIntentAsync(
            NpcHandle npc, ActorControllerId controllerId, NpcActorIntent intent, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> ReleaseAsync(
            NpcHandle npc, ActorControllerId controllerId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public ValueTask<int> ReleaseControllerAsync(
            ActorControllerId controllerId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(1);

        private sealed class Registration(GameplayArchetypeId id, Action retired) : INpcArchetypeRegistration
        {
            private Action? retired = retired;
            public GameplayArchetypeId Id { get; } = id;
            public void Dispose() => Interlocked.Exchange(ref retired, null)?.Invoke();
        }
    }

    private sealed class PermissiveShopOperations : INpcShopOperations
    {
        public NpcShopRegistrationStatus TryRegister(
            NpcShopCatalog catalog,
            out INpcShopRegistration? registration)
        {
            registration = new Registration(catalog);
            return NpcShopRegistrationStatus.Registered;
        }

        private sealed class Registration : INpcShopRegistration
        {
            private NpcShopCatalog? current;

            public Registration(NpcShopCatalog catalog)
            {
                current = catalog;
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

            public void Dispose() => current = null;
        }
    }

    private sealed class PermissiveServerPlayers : IServerPlayerOperations
    {
        private static readonly PlayerHandle Player = new(new PlayerSlotId(9), new PlayerSessionGeneration(1));
        public ValueTask<ServerPlayerCreateResult> CreateAsync(
            ServerPlayerId id, float positionX, float positionY, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ServerPlayerCreateResult(ServerPlayerCreateStatus.Created, Player));
        public ValueTask<bool> SetHorizontalIntentAsync(ServerPlayerId id, ServerPlayerHorizontalIntent intent, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> SetJumpIntentAsync(ServerPlayerId id, ServerPlayerJumpIntent intent, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> SetMovementIntentAsync(ServerPlayerId id, ServerPlayerMovementIntent intent, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> SetAppearanceAsync(ServerPlayerId id, ServerPlayerAppearanceState appearance, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> SetVitalsAsync(ServerPlayerId id, ServerPlayerVitalsState vitals, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> SetItemAsync(ServerPlayerId id, ServerPlayerItemState item, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> DespawnAsync(ServerPlayerId id, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class RejectingActorOperations : INpcActorOperations
    {
        private static readonly NpcHandle SpawnedNpc = new(5, new NpcGeneration(1));

        public int ArchetypeRetiredCount { get; private set; }
        public int DespawnCount { get; private set; }

        public NpcArchetypeRegistrationStatus TryRegisterArchetype(
            NpcArchetypeDescriptor descriptor,
            out INpcArchetypeRegistration? registration)
        {
            registration = new Registration(descriptor.Id, () => ArchetypeRetiredCount++);
            return NpcArchetypeRegistrationStatus.Registered;
        }

        public ValueTask<NpcActorSpawnResult> SpawnAsync(
            NpcActorSpawnRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new NpcActorSpawnResult(NpcActorSpawnStatus.Spawned, SpawnedNpc));

        public ValueTask<bool> DespawnAsync(NpcHandle npc, CancellationToken cancellationToken = default)
        {
            DespawnCount++;
            return ValueTask.FromResult(true);
        }

        public ValueTask<NpcActorAcquireStatus> AcquireAsync(
            NpcHandle npc,
            ActorControllerId controllerId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(NpcActorAcquireStatus.QueueRejected);

        public ValueTask<bool> SetIntentAsync(
            NpcHandle npc,
            ActorControllerId controllerId,
            NpcActorIntent intent,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask<bool> ReleaseAsync(
            NpcHandle npc,
            ActorControllerId controllerId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask<int> ReleaseControllerAsync(
            ActorControllerId controllerId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        private sealed class Registration(GameplayArchetypeId id, Action retired) : INpcArchetypeRegistration
        {
            private Action? retired = retired;
            public GameplayArchetypeId Id { get; } = id;
            public void Dispose() => Interlocked.Exchange(ref retired, null)?.Invoke();
        }
    }

    private sealed class RejectingShopOperations : INpcShopOperations
    {
        public NpcShopRegistrationStatus TryRegister(
            NpcShopCatalog catalog,
            out INpcShopRegistration? registration)
        {
            registration = null;
            return NpcShopRegistrationStatus.RuntimeDetached;
        }
    }

    private sealed class RejectingServerPlayers : IServerPlayerOperations
    {
        public ValueTask<ServerPlayerCreateResult> CreateAsync(
            ServerPlayerId id,
            float positionX,
            float positionY,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ServerPlayerCreateResult(ServerPlayerCreateStatus.QueueRejected, default));

        public ValueTask<bool> SetHorizontalIntentAsync(
            ServerPlayerId id,
            ServerPlayerHorizontalIntent intent,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

        public ValueTask<bool> SetJumpIntentAsync(
            ServerPlayerId id,
            ServerPlayerJumpIntent intent,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

        public ValueTask<bool> SetMovementIntentAsync(
            ServerPlayerId id,
            ServerPlayerMovementIntent intent,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

        public ValueTask<bool> SetAppearanceAsync(
            ServerPlayerId id,
            ServerPlayerAppearanceState appearance,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

        public ValueTask<bool> SetVitalsAsync(
            ServerPlayerId id,
            ServerPlayerVitalsState vitals,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

        public ValueTask<bool> SetItemAsync(
            ServerPlayerId id,
            ServerPlayerItemState item,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

        public ValueTask<bool> DespawnAsync(
            ServerPlayerId id,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class TestDashboard : IDashboardProvider
    {
        public string Id => "test.crash-containment";
        public string Title => "Crash containment";
        public View CreateDashboard() => new();
        public void Refresh(View rootView) { }
    }
}
