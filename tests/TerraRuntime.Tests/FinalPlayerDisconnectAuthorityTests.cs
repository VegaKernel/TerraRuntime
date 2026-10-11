using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Flat;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Core.Players;
using Xunit;
using TerraRuntime.Application;
using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.World;
using System.Collections.Concurrent;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class FinalPlayerDisconnectAuthorityTests
{
    [Fact]
    public async Task Failed_transfer_detach_does_not_leave_member_after_final_live_teardown()
    {
        using WorldRuntime runtime = CreateRuntime("FailedDetachCleanup", seed: 980);
        runtime.Start();
        var source = GameCommandSourceId.FromConnection(980);
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(
            maxFrames: 8192, maxQueuedBytes: 64 * 1024 * 1024, maxFrameBytes: 4 * 1024 * 1024));
        Assert.True(RuntimeConnectionWorldBinding.TryCreateTransferred(runtime, source, outbound,
            new PlayerSlotId(0), "FailedDetach", out var binding));
        Assert.True(binding!.TryRegister());
        using var route = new RuntimeConnectionRoute(source, outbound, binding);
        var player = AssertPlayer(route.ActivePlayer);
        var image = new RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        for (int slot = 0; slot < 50; slot++)
            image[slot] = new RuntimePlayerInventoryItem(VanillaItemIds.DirtBlock, 9999, VanillaPrefixIds.None, 0);
        image[58] = new RuntimePlayerInventoryItem(new ItemTypeId(110), 1, VanillaPrefixIds.None, 0);
        await AttachInitialPlayerAsync(runtime, source, player, "FailedDetach", 96f, 128f, 70, 100, inventory: image);
        var connection = new ConnectionHandle(source, player);
        Assert.Null(await runtime.TransferIngress.DetachAsync(connection, TestContext.Current.CancellationToken));
        Assert.NotNull(await runtime.PlayerStateSnapshots.CaptureAsync(player, TestContext.Current.CancellationToken));
        Assert.True(runtime.State.TryCapturePlayerInventoryItem(player, 58, out var cursor));
        Assert.Equal((short)1, cursor.Stack);
        route.DisconnectActive();
        Assert.Null(await runtime.PlayerStateSnapshots.CaptureAsync(player, TestContext.Current.CancellationToken));
        route.Dispose();
        var replacementSource = GameCommandSourceId.FromConnection(981);
        var replacementOutbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(
            maxFrames: 8192, maxQueuedBytes: 64 * 1024 * 1024, maxFrameBytes: 4 * 1024 * 1024));
        Assert.True(RuntimeConnectionWorldBinding.TryCreateTransferred(runtime, replacementSource, replacementOutbound,
            new PlayerSlotId(0), "Replacement", out var replacementBinding));
        Assert.True(replacementBinding!.TryRegister());
        using var replacementRoute = new RuntimeConnectionRoute(replacementSource, replacementOutbound, replacementBinding);
        var replacementPlayer = AssertPlayer(replacementRoute.ActivePlayer);
        Assert.NotEqual(player.Generation, replacementPlayer.Generation);
        await AttachInitialPlayerAsync(runtime, replacementSource, replacementPlayer, "Replacement", 96f, 128f, 70, 100);
        Assert.True(runtime.CommandIngress.TryPost(GameCommandSourceId.System, new PlayerDisconnectRuntimeCommand(connection)));
        Assert.NotNull(await runtime.PlayerStateSnapshots.CaptureAsync(replacementPlayer, TestContext.Current.CancellationToken));
        Assert.Null(await runtime.PlayerStateSnapshots.CaptureAsync(player, TestContext.Current.CancellationToken));

    }

    [Fact]
    public async Task Final_live_teardown_cancels_failed_detach_before_observer_reentry_and_later_tick()
    {
        int checks = 0;
        void Check(bool value, string name) { checks++; Assert.True(value, name); }
        T Owner<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        var tiles = new WorldTileStore(new WorldDimensions(400, 300)); var npcs = new RuntimeNpcStore();
        var sink = new TeardownSink();
        var state = new ServerRuntimeState(playerEvents: sink, worldTiles: tiles, npcs: npcs, playerUpdateRandomSeed: new(0), itemPrefixWorld: new(false, false, false));
        var players = Owner<ServerRuntimeComposition>(state, "_runtime").Players;
        players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
        players.SetRemotePlayerEnvironment(new(false, false), new RuntimeProjectileStore());
        var members = Owner<RuntimePlayerMembership>(players, "membership"); var items = Owner<RuntimePlayerInventoryStore>(players, "inventory");
        var c = new ConnectionHandle(GameCommandSourceId.FromConnection(101), new(new(0), new(1)));
        var other = new ConnectionHandle(GameCommandSourceId.FromConnection(102), new(new(1), new(1)));
        foreach (var connection in new[] { c })
        {
            members.Commit(new RuntimePlayerMember { Connection = connection, Slot = connection.Player.Slot, Revision = 1, HasHealth = true, Life = 400, MaxLife = 400, HasMana = true, Mana = 200, MaxMana = 200, PositionX = 100, PositionY = 100 });
            Check(Owner<RuntimeNpcRawPlayerSlots1458>(players, "npcRawSlots").TryAttach(connection.Player), "actual raw-slot occupation attached");
            Check(items.TryAttach(connection), "attach");
            Check(Owner<RuntimePlayerTransferProfileStore>(players, "transferProfiles").TrySetBuffTypes(connection, Array.Empty<BuffTypeId>()), "owned clear buff profile");
        }
        Check(items.TrySet(c, new(c.Player.Slot, 50, 1, 0, 73, 0)), "configured trusted money"); var funds = new long[59]; funds[50] = 10000; items.AdoptFundedQuantities(c, new short[59], funds);
        var body = new NpcStateUpdate(17, 17, 100, 100, 0, 0, 255, default, NpcSimulationState.Initial with { Life = 250, LifeMax = 250 });
        Check(npcs.TrySpawn(0, in body, out var vendor), "vendor");
        var offers = new[] { new VanillaTownShopEntry1458(0, new(110), 100, VanillaTownShopCurrency1458.Coins) };
        var session = new RuntimeTownShopSession1458(0, new(17), RuntimeTownShopSessionKind1458.OrdinaryShop, 1, false, default, offers, RuntimeTownCommerceMissingFacts1458.None) { VendorHandle = vendor.Handle };
        Check(members.TrySetTalkNpc(c, 0), "talk"); Check(members.TrySetTownShopSession(c, session), "session");
        var stock = new ShopItem1458[40]; stock[0] = new(new(110, 1, 0, false), 100, false, true, 100);
        Check(RuntimeTownShopQuote1458.TryCreate(session, members, npcs, c, vendor.Handle, stock, 1, false, out var quote, new(false, false, false)), "quote"); session.Quote = quote;
        Check(players.TryTickRemotePlayerPhase(), "initial configured whole phase admitted");
        for (short i = 0; i < 54; i++) if (i != 50) Check(items.TrySet(c, new(c.Player.Slot, i, 9999, 0, 2, 0)), "full ordinary storage");
        Check(items.TrySet(c, new(c.Player.Slot, 58, 1, 0, 110, 0)), "configured full cursor"); var q = items.CaptureFundedQuantities(c); q[58] = 1; items.AdoptFundedQuantities(c, q, items.CaptureFundedCoinValues(c));
        Check(players.TryApply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 58, 0, 0, 0, 0))), "source-shaped sale cursor5");
        Check(players.TryApply(new PlayerInventorySyncCompleteRuntimeCommand(c)), "sealed138");
        Check(!players.CanApplyShopSource(c.Source), "journal sealed before system detach");
        var completion = new TaskCompletionSource<RuntimePlayerTransferState?>();
        Check(players.TryApply(new PlayerTransferDetachRuntimeCommand(c, completion)), "actual System-shaped detach command handled");
        Check(completion.Task.IsCompletedSuccessfully && await completion.Task is null, "full cursor refuses detachable image");
        Check(members.TryGet(c, out var stillPresent) && stillPresent.CursorMove?.Shop is { IsSealed: true }, "diagnostic: null detach retains member and sealed journal");

        var originalStock = Owner<ShopItem1458[]>(quote!, "stock").ToArray();
        var originalMemos = Owner<ShopMemo1458[]>(quote!, "memos").ToArray();
        int callbacks = 0;
        sink.OnDisconnected = () =>
        {
            callbacks++;
            Check(!members.TryGet(c, out _), "membership gone before teardown observer");
            players.TickShopJournalWork();
            Check(players.LastShopWorkSteps == 0, "observer reentry cannot advance orphan work");
        };
        var finalCompletion = new TaskCompletionSource<bool>();
        Check(players.TryApply(new PlayerDisconnectRuntimeCommand(c, finalCompletion)), "final System teardown handled");
        Check(finalCompletion.Task.IsCompletedSuccessfully && await finalCompletion.Task, "failed transfer remains null");
        Check(callbacks == 1 && !members.TryGet(c, out _), "exact actor removed once");
        Check(players.CanApplyShopSource(c.Source), "stale source queue no longer blocked by orphan receipt");
        state.Tick();
        Check(players.LastShopWorkSteps == 0, "later whole State.Tick cannot advance orphan work");
        Check(Owner<ShopItem1458[]>(quote!, "stock").SequenceEqual(originalStock) &&
            Owner<ShopMemo1458[]>(quote!, "memos").SequenceEqual(originalMemos), "no orphan economy adoption");
        // Deliberately inconsistent configured raw-slot ownership exercises the existing
        // invariant exception after removal; this is cleanup policy, not vanilla parity.
        members.Commit(new RuntimePlayerMember { Connection = other, Slot = other.Player.Slot, Revision = 1 });
        Check(items.TryAttach(other), "attach inconsistent raw-slot fixture");
        var failedCompletion = new TaskCompletionSource<bool>();
        var cleanupFailure = Record.Exception(() => players.TryApply(new PlayerDisconnectRuntimeCommand(other, failedCompletion)));
        Assert.IsType<InvalidOperationException>(cleanupFailure);
        Assert.Contains("NPC raw player slot reset", cleanupFailure.Message);
        Check(!members.TryGet(other, out _) && failedCompletion.Task.IsCompletedSuccessfully,
            "completion guaranteed after actor removal and existing cleanup exception");
        Check(await failedCompletion.Task, "removed actor acknowledged despite propagated exception");
        Assert.True(checks > 70);

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
        bool dead = false,
        RuntimePlayerInventoryItem[]? inventory = null)
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
            HasMount = true,
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
            inventory ?? new RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount],
            appearance,
            [],
            BuffTypes: null,
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
    private sealed class TeardownSink : IRuntimePlayerEventSink
    {
        public Action? OnDisconnected;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) { }
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r) { }
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) { }
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) { }
        public void PlayerDisconnected(ConnectionHandle c) => OnDisconnected?.Invoke();
    }

}
