using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class Level1PlayerTransferTests
{
    [Fact]
    public async Task Process_world_tree_tracks_route_membership_across_semantic_transfer()
    {
        WorldRuntime primary = CreateRuntime("Primary", seed: 90);
        using var registry = new WorldRegistry(capacity: 3);
        Assert.True(registry.TryAdmit(primary, primary: true));
        using var sandboxes = new SandboxHost(
            registry,
            BuiltInWorldGeneratorSource.Instance,
            ServerWorldLoadPolicy.CreateLimits());
        var sandboxName = new SandboxName("arena");
        var sandboxSource = new SandboxWorldSource.Generated(
            FlatProvider.GeneratorId,
            "Arena",
            Seed: 91,
            WidthTiles: 32,
            HeightTiles: 24,
            WorldGenerationOptions.Default);
        Assert.True(sandboxes.TryCreate(
            new SandboxCreateRequest(sandboxName, WorldIsolationLevel.InProcess, sandboxSource),
            out SandboxJobId createId,
            out string? createError), createError);
        Assert.Equal(
            SandboxJobStatus.Completed,
            (await sandboxes.WaitForJobAsync(
                createId,
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken)).Status);

        GameCommandSourceId source = GameCommandSourceId.FromConnection(39);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8192, maxQueuedBytes: 64 * 1024 * 1024, maxFrameBytes: 4 * 1024 * 1024));
        Assert.True(RuntimeConnectionWorldBinding.TryCreateTransferred(
            primary,
            source,
            outbound,
            new PlayerSlotId(0),
            "TreePlayer",
            out RuntimeConnectionWorldBinding? binding));
        Assert.NotNull(binding);
        Assert.True(binding!.TryRegister());
        using var route = new RuntimeConnectionRoute(source, outbound, binding);
        PlayerHandle player = AssertPlayer(route.ActivePlayer);
        await AttachInitialPlayerAsync(primary, source, player, "TreePlayer", x: 96f, y: 128f, life: 70, maxLife: 100);

        var directory = new RuntimeConnectionDirectory();
        Assert.True(directory.TryRegister(source, route));
        var transfers = new Level1PlayerTransferCoordinator(directory, registry, sandboxes);

        SandboxTreeSnapshot before = transfers.CaptureTreeSnapshot();
        Assert.Equal(2, before.Worlds.Length);
        Assert.Contains(before.Worlds.ToArray(), world => world.IsPrimary &&
            world.Players.ToArray().Any(candidate => candidate.Name == "TreePlayer"));

        Assert.True(transfers.TryMove("#0", sandboxName, forceRespawn: false, out string? moveError), moveError);
        SandboxTreeSnapshot after = transfers.CaptureTreeSnapshot();
        SandboxTreeWorldSnapshot arena = Assert.Single(
            after.Worlds.ToArray(),
            world => world.Sandbox == sandboxName);
        Assert.Contains(arena.Players.ToArray(), candidate => candidate.Name == "TreePlayer");
        Assert.DoesNotContain(
            Assert.Single(after.Worlds.ToArray(), world => world.IsPrimary).Players.ToArray(),
            candidate => candidate.Name == "TreePlayer");

        Assert.False(sandboxes.TryDestroy(sandboxName, out _, out string? occupiedError));
        Assert.Contains("Move connected players", occupiedError, StringComparison.Ordinal);
        Assert.True(transfers.TryMove("#0", sandbox: null, forceRespawn: false, out string? returnError), returnError);
        Assert.True(sandboxes.TryDestroy(sandboxName, out SandboxJobId destroyId, out string? destroyError), destroyError);
        Assert.Equal(
            SandboxJobStatus.Completed,
            (await sandboxes.WaitForJobAsync(
                destroyId,
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken)).Status);

        Assert.True(directory.TryUnregister(source, out RuntimeConnectionRoute? removed));
        Assert.Same(route, removed);
    }

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
        Assert.Equal(sandbox.World.RuntimeMetadata.SpawnX * 16f + 8f - PlayerAuthority.VanillaBasePlayerWidth * 0.5f, sandboxSnapshot.Value.PositionX);
        Assert.Equal(sandbox.World.RuntimeMetadata.SpawnY * 16f - PlayerAuthority.VanillaBasePlayerHeight, sandboxSnapshot.Value.PositionY);
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
        Assert.Equal(primary.World.RuntimeMetadata.SpawnX * 16f + 8f - PlayerAuthority.VanillaBasePlayerWidth * 0.5f, returned.Value.PositionX);
        Assert.Equal(primary.World.RuntimeMetadata.SpawnY * 16f - PlayerAuthority.VanillaBasePlayerHeight, returned.Value.PositionY);
        Assert.Equal((short)70, returned.Value.Life);
    }

    [Fact]
    public async Task Cross_world_transfer_finishes_replacement_world_batch_with_explicit_packet12_spawn()
    {
        using WorldRuntime primary = CreateRuntime("Primary", seed: 707);
        using WorldRuntime sandbox = CreateRuntime("Arena", seed: 808);
        primary.Start();
        sandbox.Start();

        GameCommandSourceId source = GameCommandSourceId.FromConnection(47);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8192, maxQueuedBytes: 64 * 1024 * 1024, maxFrameBytes: 4 * 1024 * 1024));
        Assert.True(RuntimeConnectionWorldBinding.TryCreateTransferred(
            primary,
            source,
            outbound,
            new PlayerSlotId(0),
            "Wire",
            out RuntimeConnectionWorldBinding? binding));
        Assert.NotNull(binding);
        Assert.True(binding!.TryRegister());
        using var route = new RuntimeConnectionRoute(source, outbound, binding);
        PlayerHandle player = AssertPlayer(route.ActivePlayer);
        await AttachInitialPlayerAsync(primary, source, player, "Wire", x: 96f, y: 128f, life: 80, maxLife: 100);
        DrainOutbound(outbound);

        Assert.True(route.TryTransfer(sandbox, forceRespawn: false, out string? error), error);

        TerrariaFrame[] frames = DrainOutbound(outbound);
        Assert.NotEmpty(frames);
        Assert.Equal((byte)TerrariaMessageId.WorldData, frames[0].MessageId);
        Assert.DoesNotContain(frames, static frame => frame.MessageId == (byte)TerrariaMessageId.PlayerSpawnSelf);
        int spawnIndex = Array.FindLastIndex(
            frames,
            static frame => frame.MessageId == (byte)TerrariaMessageId.PlayerSpawn);
        Assert.True(spawnIndex >= 0);
        TerrariaFrame spawnFrame = frames[spawnIndex];
        // Packet 12 is the final handoff inside the atomic replacement-world bootstrap. The successful
        // destination attach may immediately enqueue packet 82 (CreativePowers SyncEveryone) afterwards;
        // that post-attach baseline does not carry position/world state and must not be confused with the
        // bootstrap handoff itself.
        Assert.All(
            frames[(spawnIndex + 1)..],
            static frame => Assert.Equal((byte)TerrariaMessageId.LoadNetModule, frame.MessageId));
        Assert.Equal(
            TerrariaJoinDecodeResult.Decoded,
            TerrariaJoinRequestDecoder.TryDecodePlayerSpawn(in spawnFrame, out TerrariaPlayerSpawnRequest spawn));
        Assert.Equal(player.Slot.Value, spawn.ClaimedPlayerId);
        Assert.Equal((short)sandbox.World.RuntimeMetadata.SpawnX, spawn.SpawnX);
        Assert.Equal((short)sandbox.World.RuntimeMetadata.SpawnY, spawn.SpawnY);
        Assert.Equal((byte)2, spawn.Team);
        Assert.Equal((byte)1, spawn.SpawnContext);
    }

    [Fact]
    public async Task Cross_world_transfer_rejects_stale_source_coordinates_until_destination_landing_arrives()
    {
        using WorldRuntime primary = CreateRuntime("Primary", seed: 711);
        using WorldRuntime sandbox = CreateRuntime("Arena", seed: 812);
        primary.Start();
        sandbox.Start();

        GameCommandSourceId source = GameCommandSourceId.FromConnection(49);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8192, maxQueuedBytes: 64 * 1024 * 1024, maxFrameBytes: 4 * 1024 * 1024));
        Assert.True(RuntimeConnectionWorldBinding.TryCreateTransferred(
            primary,
            source,
            outbound,
            new PlayerSlotId(0),
            "Landing",
            out RuntimeConnectionWorldBinding? binding));
        Assert.NotNull(binding);
        Assert.True(binding!.TryRegister());
        using var route = new RuntimeConnectionRoute(source, outbound, binding);
        PlayerHandle player = AssertPlayer(route.ActivePlayer);
        await AttachInitialPlayerAsync(primary, source, player, "Landing", x: 490f, y: 300f, life: 80, maxLife: 100);
        DrainOutbound(outbound);

        Assert.True(route.TryTransfer(sandbox, forceRespawn: false, out string? error), error);
        DrainOutbound(outbound);
        PlayerStateSnapshot? landed = await sandbox.PlayerStateSnapshots.CaptureAsync(
            AssertPlayer(route.ActivePlayer),
            TestContext.Current.CancellationToken);
        Assert.NotNull(landed);

        TerrariaFrame stale = MovementFrame(player.Slot, positionX: 490f, positionY: 300f);
        Assert.Equal(TerrariaFrameSinkResult.Continue, route.OnFrame(in stale));
        await Task.Delay(25, TestContext.Current.CancellationToken);

        PlayerStateSnapshot? afterStale = await sandbox.PlayerStateSnapshots.CaptureAsync(
            AssertPlayer(route.ActivePlayer),
            TestContext.Current.CancellationToken);
        Assert.NotNull(afterStale);
        Assert.Equal(landed.Value.PositionX, afterStale.Value.PositionX);
        Assert.Equal(landed.Value.PositionY, afterStale.Value.PositionY);

        TerrariaFrame correction = Assert.Single(DrainOutbound(outbound));
        Assert.Equal((byte)TerrariaMessageId.TeleportEntity, correction.MessageId);
        byte[] payload = correction.Payload.ToArray();
        Assert.Equal(12, payload.Length);
        Assert.Equal(0, payload[0]);
        Assert.Equal(player.Slot.Value, BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(1)));
        Assert.Equal(landed.Value.PositionX, BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(3))));
        Assert.Equal(landed.Value.PositionY, BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(7))));
    }

    [Fact]
    public async Task Cross_world_transfer_ignores_spawning_into_world_packet12_echo_until_destination_landing()
    {
        using WorldRuntime primary = CreateRuntime("Primary", seed: 713);
        using WorldRuntime sandbox = CreateRuntime("Arena", seed: 814);
        primary.Start();
        sandbox.Start();

        GameCommandSourceId source = GameCommandSourceId.FromConnection(50);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8192, maxQueuedBytes: 64 * 1024 * 1024, maxFrameBytes: 4 * 1024 * 1024));
        Assert.True(RuntimeConnectionWorldBinding.TryCreateTransferred(
            primary,
            source,
            outbound,
            new PlayerSlotId(0),
            "SpawnEcho",
            out RuntimeConnectionWorldBinding? binding));
        Assert.NotNull(binding);
        Assert.True(binding!.TryRegister());
        using var route = new RuntimeConnectionRoute(source, outbound, binding);
        PlayerHandle player = AssertPlayer(route.ActivePlayer);
        await AttachInitialPlayerAsync(primary, source, player, "SpawnEcho", x: 490f, y: 300f, life: 80, maxLife: 100);
        DrainOutbound(outbound);

        Assert.True(route.TryTransfer(sandbox, forceRespawn: false, out string? error), error);
        DrainOutbound(outbound);
        PlayerHandle destinationPlayer = AssertPlayer(route.ActivePlayer);
        PlayerStateSnapshot? landed = await sandbox.PlayerStateSnapshots.CaptureAsync(
            destinationPlayer,
            TestContext.Current.CancellationToken);
        Assert.NotNull(landed);

        // Terraria 1.4.5.8 Player.Spawn(SpawningIntoWorld) echoes packet 12 to the server after the synthetic
        // replacement-world handoff. FindSpawn/CheckSpawn is allowed to reduce a personal spawn to -1/-1.
        // That echo is not a new authoritative respawn for TerraRuntime's already-attached destination player.
        var echo = new PlayerSpawnCommitRequest(
            destinationPlayer.Slot,
            SpawnX: -1,
            SpawnY: -1,
            RespawnTimer: 0,
            DeathsPve: 0,
            DeathsPvp: 0,
            Team: 2,
            SpawnContext: 1);
        TerrariaFrame echoFrame = DecodeFrame(TerrariaPlayerReplicationFrameEncoder.EncodeSpawn(in echo));
        Assert.Equal(TerrariaFrameSinkResult.Continue, route.OnFrame(in echoFrame));
        await Task.Delay(25, TestContext.Current.CancellationToken);

        PlayerStateSnapshot? afterEcho = await sandbox.PlayerStateSnapshots.CaptureAsync(
            destinationPlayer,
            TestContext.Current.CancellationToken);
        Assert.NotNull(afterEcho);
        Assert.Equal(landed.Value.PositionX, afterEcho.Value.PositionX);
        Assert.Equal(landed.Value.PositionY, afterEcho.Value.PositionY);

        TerrariaFrame stale = MovementFrame(destinationPlayer.Slot, positionX: 0f, positionY: 0f);
        Assert.Equal(TerrariaFrameSinkResult.Continue, route.OnFrame(in stale));
        TerrariaFrame correction = Assert.Single(DrainOutbound(outbound));
        Assert.Equal((byte)TerrariaMessageId.TeleportEntity, correction.MessageId);
        byte[] correctionPayload = correction.Payload.ToArray();
        Assert.Equal(landed.Value.PositionX, BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(correctionPayload.AsSpan(3))));
        Assert.Equal(landed.Value.PositionY, BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(correctionPayload.AsSpan(7))));
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
        Assert.Equal(primary.World.RuntimeMetadata.SpawnX * 16f + 8f - PlayerAuthority.VanillaBasePlayerWidth * 0.5f, snapshot.Value.PositionX);
        Assert.Equal(primary.World.RuntimeMetadata.SpawnY * 16f - PlayerAuthority.VanillaBasePlayerHeight, snapshot.Value.PositionY);
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
            [],
            GodMode: false);
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

    private static TerrariaFrame[] DrainOutbound(TerrariaConnectionOutboundQueue outbound)
    {
        PropertyInfo property = typeof(TerrariaConnectionOutboundQueue).GetProperty(
            "InnerQueue",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Outbound queue internal contract changed.");
        var queue = Assert.IsType<BoundedOutboundQueue>(property.GetValue(outbound));
        var frames = new List<TerrariaFrame>();
        while (queue.TryRead(out OutboundFrame outboundFrame))
        {
            var sequence = new ReadOnlySequence<byte>(outboundFrame.Bytes);
            Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
            Assert.Equal(0, sequence.Length);
            frames.Add(frame);
        }
        return frames.ToArray();
    }

    private static TerrariaFrame MovementFrame(PlayerSlotId slot, float positionX, float positionY)
    {
        var movement = new PlayerMovementCommitRequest(
            slot,
            ControlFlags: 0,
            MovementFlags: 0,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: 0,
            PositionX: positionX,
            PositionY: positionY,
            HasVelocity: false,
            VelocityX: 0f,
            VelocityY: 0f,
            HasMount: false,
            MountType: 0,
            HasPotionOfReturnPositions: false,
            PotionOfReturnOriginalPositionX: 0f,
            PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f,
            PotionOfReturnHomePositionY: 0f,
            HasCameraTarget: false,
            CameraTargetX: 0f,
            CameraTargetY: 0f);
        byte[] encoded = TerrariaPlayerReplicationFrameEncoder.EncodeMovement(in movement);
        var sequence = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.True(sequence.IsEmpty);
        return frame;
    }

    private static TerrariaFrame DecodeFrame(ReadOnlyMemory<byte> encoded)
    {
        var sequence = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.True(sequence.IsEmpty);
        return frame;
    }

    private static PlayerHandle AssertPlayer(PlayerHandle? player)
    {
        Assert.True(player.HasValue);
        return player.Value;
    }

    private static WorldRuntime CreateRuntime(string name, ulong seed)
    {
        var source = new SandboxWorldSource.Generated(
            FlatProvider.GeneratorId,
            name,
            seed,
            WidthTiles: 32,
            HeightTiles: 24,
            WorldGenerationOptions.Default);
        var materializer = new SandboxWorldMaterializer(
            BuiltInWorldGeneratorSource.Instance,
            ServerWorldLoadPolicy.CreateLimits());
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
