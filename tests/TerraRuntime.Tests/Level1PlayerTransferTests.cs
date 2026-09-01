using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class Level1PlayerTransferTests
{
    [Fact]
    public async Task Route_moves_player_primary_to_sandbox_and_back_without_changing_wire_slot()
    {
        using WorldRuntime primary = CreateRuntime("Primary", seed: 101);
        using WorldRuntime sandbox = CreateRuntime("Arena", seed: 202);
        primary.Start();
        sandbox.Start();

        GameCommandSourceId source = GameCommandSourceId.FromConnection(42);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8192, maxQueuedBytes: 64 * 1024 * 1024, maxFrameBytes: 4 * 1024 * 1024));
        Assert.True(RuntimeConnectionWorldBinding.TryCreateTransferred(
            primary,
            source,
            outbound,
            new PlayerSlotId(0),
            "Alice",
            out RuntimeConnectionWorldBinding? primaryBinding));
        Assert.NotNull(primaryBinding);
        Assert.True(primaryBinding!.TryRegister());
        using var route = new RuntimeConnectionRoute(source, outbound, primaryBinding);

        PlayerHandle primaryPlayer = AssertPlayer(route.ActivePlayer);
        await AttachInitialPlayerAsync(primary, source, primaryPlayer, "Alice", x: 96f, y: 128f, life: 70, maxLife: 100);

        Assert.True(route.TryTransfer(sandbox, forceRespawn: false, out string? moveError), moveError);
        Assert.Same(sandbox, route.ActiveRuntime);
        PlayerHandle sandboxPlayer = AssertPlayer(route.ActivePlayer);
        Assert.Equal(primaryPlayer.Slot, sandboxPlayer.Slot);
        Assert.Equal("Alice", route.ActivePlayerName);

        Assert.Null(await primary.PlayerStateSnapshots.CaptureAsync(primaryPlayer, TestContext.Current.CancellationToken));
        PlayerStateSnapshot? sandboxSnapshot = await sandbox.PlayerStateSnapshots.CaptureAsync(
            sandboxPlayer,
            TestContext.Current.CancellationToken);
        Assert.NotNull(sandboxSnapshot);
        Assert.Equal(96f, sandboxSnapshot.Value.PositionX);
        Assert.Equal(128f, sandboxSnapshot.Value.PositionY);
        Assert.Equal((short)70, sandboxSnapshot.Value.Life);
        Assert.Equal((short)100, sandboxSnapshot.Value.MaxLife);

        Assert.True(route.TryTransfer(primary, forceRespawn: false, out string? returnError), returnError);
        Assert.Same(primary, route.ActiveRuntime);
        Assert.Equal(primaryPlayer, route.ActivePlayer);
        Assert.Equal("Alice", route.ActivePlayerName);
        Assert.Null(await sandbox.PlayerStateSnapshots.CaptureAsync(sandboxPlayer, TestContext.Current.CancellationToken));

        PlayerStateSnapshot? returned = await primary.PlayerStateSnapshots.CaptureAsync(
            primaryPlayer,
            TestContext.Current.CancellationToken);
        Assert.NotNull(returned);
        Assert.Equal(96f, returned.Value.PositionX);
        Assert.Equal(128f, returned.Value.PositionY);
        Assert.Equal((short)70, returned.Value.Life);
    }

    [Fact]
    public async Task Destination_wire_slot_collision_fails_without_detaching_source_player()
    {
        using WorldRuntime primary = CreateRuntime("Primary", seed: 404);
        using WorldRuntime sandbox = CreateRuntime("Arena", seed: 505);
        primary.Start();
        sandbox.Start();

        GameCommandSourceId source = GameCommandSourceId.FromConnection(44);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8192, maxQueuedBytes: 64 * 1024 * 1024, maxFrameBytes: 4 * 1024 * 1024));
        Assert.True(RuntimeConnectionWorldBinding.TryCreateTransferred(
            primary,
            source,
            outbound,
            new PlayerSlotId(0),
            "Carol",
            out RuntimeConnectionWorldBinding? binding));
        Assert.NotNull(binding);
        Assert.True(binding!.TryRegister());
        using var route = new RuntimeConnectionRoute(source, outbound, binding);
        PlayerHandle player = AssertPlayer(route.ActivePlayer);
        await AttachInitialPlayerAsync(primary, source, player, "Carol", x: 80f, y: 96f, life: 90, maxLife: 100);

        Assert.True(sandbox.Slots.TryAcquireServerOwned(out PlayerSlotPool.PlayerSlotLease? blocker));
        Assert.NotNull(blocker);
        using (blocker)
        {
            Assert.Equal(player.Slot, blocker!.Slot);
            Assert.False(route.TryTransfer(sandbox, forceRespawn: false, out string? error));
            Assert.Contains("cannot reserve player slot", error, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Same(primary, route.ActiveRuntime);
        PlayerStateSnapshot? stillPrimary = await primary.PlayerStateSnapshots.CaptureAsync(
            player,
            TestContext.Current.CancellationToken);
        Assert.NotNull(stillPrimary);
        Assert.Equal(80f, stillPrimary.Value.PositionX);
    }

    [Fact]
    public async Task Same_runtime_respawn_forces_spawn_position_and_restores_health()
    {
        using WorldRuntime primary = CreateRuntime("Primary", seed: 303);
        primary.Start();

        GameCommandSourceId source = GameCommandSourceId.FromConnection(43);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8192, maxQueuedBytes: 64 * 1024 * 1024, maxFrameBytes: 4 * 1024 * 1024));
        Assert.True(RuntimeConnectionWorldBinding.TryCreateTransferred(
            primary,
            source,
            outbound,
            new PlayerSlotId(0),
            "Bob",
            out RuntimeConnectionWorldBinding? binding));
        Assert.NotNull(binding);
        Assert.True(binding!.TryRegister());
        using var route = new RuntimeConnectionRoute(source, outbound, binding);

        PlayerHandle player = AssertPlayer(route.ActivePlayer);
        await AttachInitialPlayerAsync(primary, source, player, "Bob", x: 160f, y: 176f, life: 1, maxLife: 120, dead: true);

        Assert.True(route.TryTransfer(primary, forceRespawn: true, out string? error), error);
        Assert.Equal(player, route.ActivePlayer);

        PlayerStateSnapshot? snapshot = await primary.PlayerStateSnapshots.CaptureAsync(
            player,
            TestContext.Current.CancellationToken);
        Assert.NotNull(snapshot);
        Assert.False(snapshot.Value.IsDead);
        Assert.Equal((short)120, snapshot.Value.Life);
        Assert.Equal(primary.World.RuntimeMetadata.SpawnX * 16f, snapshot.Value.PositionX);
        Assert.Equal(primary.World.RuntimeMetadata.SpawnY * 16f, snapshot.Value.PositionY);
        Assert.Equal(0f, snapshot.Value.VelocityX);
        Assert.Equal(0f, snapshot.Value.VelocityY);
        Assert.Equal((ushort)0, snapshot.Value.MountType);
    }

    [Fact]
    public async Task Detached_transfer_transaction_restores_source_ownership_without_exposing_payload_to_route()
    {
        using WorldRuntime runtime = CreateRuntime("Primary", seed: 606);
        runtime.Start();

        GameCommandSourceId source = GameCommandSourceId.FromConnection(45);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8192, maxQueuedBytes: 64 * 1024 * 1024, maxFrameBytes: 4 * 1024 * 1024));
        Assert.True(RuntimeConnectionWorldBinding.TryCreateTransferred(
            runtime,
            source,
            outbound,
            new PlayerSlotId(0),
            "Dora",
            out RuntimeConnectionWorldBinding? binding));
        Assert.NotNull(binding);
        using (binding)
        {
            PlayerHandle player = AssertPlayer(binding!.Player);
            var connection = new ConnectionHandle(source, player);
            await AttachInitialPlayerAsync(runtime, source, player, "Dora", x: 112f, y: 144f, life: 65, maxLife: 100);

            RuntimePlayerTransferTransaction? transfer = RuntimePlayerTransferTransaction.Detach(
                runtime,
                connection,
                TestContext.Current.CancellationToken);
            Assert.NotNull(transfer);
            Assert.Equal("Dora", transfer!.PlayerName);
            Assert.Null(await runtime.PlayerStateSnapshots.CaptureAsync(player, TestContext.Current.CancellationToken));

            transfer.RestoreSource(TestContext.Current.CancellationToken);
            PlayerStateSnapshot? restored = await runtime.PlayerStateSnapshots.CaptureAsync(
                player,
                TestContext.Current.CancellationToken);
            Assert.NotNull(restored);
            Assert.Equal(112f, restored.Value.PositionX);
            Assert.Equal(144f, restored.Value.PositionY);
            Assert.Equal((short)65, restored.Value.Life);
            Assert.Throws<InvalidOperationException>(() => transfer.RestoreSource(CancellationToken.None));
        }
    }

    private static async Task AttachInitialPlayerAsync(
        WorldRuntime runtime,
        GameCommandSourceId source,
        PlayerHandle player,
        string name,
        float x,
        float y,
        short life,
        short maxLife,
        bool dead = false)
    {
        var snapshot = new PlayerStateSnapshot(
            player,
            new PlayerStateRevision(1),
            Team: 2,
            ControlFlags: 1,
            MovementFlags: 0,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: 0,
            PositionX: x,
            PositionY: y,
            VelocityX: 1.25f,
            VelocityY: -0.5f,
            MountType: 1,
            PotionOfReturnOriginalPositionX: 0f,
            PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f,
            PotionOfReturnHomePositionY: 0f,
            CameraTargetX: 0f,
            CameraTargetY: 0f)
        {
            HasHealth = true,
            Life = life,
            MaxLife = maxLife,
            IsDead = dead,
            HasMana = true,
            Mana = 20,
            MaxMana = 40
        };
        var appearance = new PlayerAppearanceCommitRequest(
            player.Slot,
            SkinVariant: 0,
            VoiceVariant: 0,
            VoicePitchOffset: 0f,
            Hair: 0,
            Name: name,
            HairDye: 0,
            HideVisibleAccessory: 0,
            HideMisc: 0,
            HairColor: default,
            SkinColor: default,
            EyeColor: default,
            ShirtColor: default,
            UnderShirtColor: default,
            PantsColor: default,
            ShoeColor: default,
            DifficultyFlags: 0,
            TorchAndCartFlags: 0,
            ConsumableUnlockFlags: 0);
        var transfer = new RuntimePlayerTransferState(
            snapshot,
            new RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount],
            appearance,
            []);
        var connection = new ConnectionHandle(source, player);
        Assert.True(await runtime.TransferIngress.AttachAsync(
            connection,
            transfer,
            checked((short)runtime.World.RuntimeMetadata.SpawnX),
            checked((short)runtime.World.RuntimeMetadata.SpawnY),
            preserveWorldPosition: true,
            forceRespawn: false,
            TestContext.Current.CancellationToken));
    }

    private static PlayerHandle AssertPlayer(PlayerHandle? player)
    {
        Assert.True(player.HasValue);
        return player.Value;
    }

    private static WorldRuntime CreateRuntime(string name, ulong seed)
    {
        var source = new SandboxWorldSource.Generated(
            FlatWorldGenerationProvider.GeneratorId,
            name,
            seed,
            WidthTiles: 32,
            HeightTiles: 24,
            WorldGenerationOptions.Default);
        var materializer = new SandboxWorldMaterializer(
            BuiltInWorldGeneratorSource.Instance,
            TerrariaServerHost.CreateServerWorldLoadLimits());
        SandboxWorldMaterializationResult result = materializer.Materialize(source, CancellationToken.None);
        Assert.True(result.Succeeded, result.Error);
        return new WorldRuntime(
            new WorldRuntimeIdentity(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()),
            source,
            result.World!,
            result.Bootstrap!,
            new InterestManagementControl(),
            new WorldRuntimeOptions { MaxPlayers = 4 });
    }
}
