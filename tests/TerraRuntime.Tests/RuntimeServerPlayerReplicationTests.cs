using System.Buffers;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class RuntimeServerPlayerReplicationTests
{
    [Fact]
    public void Committed_server_player_state_replicates_to_playing_clients_and_despawns()
    {
        var registry = new RuntimeConnectionRegistry();
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(7001);
        ConnectionHandle real = Connection(source, slot: 0);
        Assert.True(registry.TryRegister(source, outbound));
        PlayerSpawnCommitRequest realSpawn = CreateSpawn(real.Player.Slot);
        registry.PlayerSpawned(real, in realSpawn);

        var slots = new PlayerSlotPool(2);
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? occupied));
        Assert.NotNull(occupied);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var service = new ServerPlayerAuthority(states, identities, events: registry);
        var id = new ServerPlayerId("test:replicated-fake");
        ServerPlayerCreateResult created = service.Create(id, 160f, 320f);
        Assert.True(created.IsCreated);

        ServerPlayerAppearanceState appearance = CreateAppearance("Replica");
        var vitals = new ServerPlayerVitalsState(100, 100, 20, 20);
        var item = new ServerPlayerItemState(0, VanillaItemIds.DirtBlock, 2, default, 0);
        Assert.True(service.SetAppearance(id, in appearance));
        Assert.True(service.SetVitals(id, in vitals));
        Assert.True(service.SetItem(id, in item));
        Assert.Equal(6, outbound.QueuedFrames);

        Assert.True(registry.TryGetServerPlayerAppearanceFrame(created.Player, out OutboundFrame appearanceFrame));
        TerrariaFrame decodedAppearanceFrame = Decode(appearanceFrame);
        Assert.Equal(
            TerrariaPlayerAppearanceDecodeResult.Decoded,
            TerrariaPlayerAppearanceCodec.TryDecode(
                decodedAppearanceFrame,
                out TerrariaPlayerAppearanceState decodedAppearance));
        Assert.Equal(created.Player.Slot.Value, decodedAppearance.PlayerId);
        Assert.Equal("Replica", decodedAppearance.Name);

        Assert.True(registry.TryGetServerPlayerHealthFrame(created.Player, out OutboundFrame healthFrame));
        TerrariaFrame decodedHealthFrame = Decode(healthFrame);
        Assert.Equal(
            TerrariaPlayerHealthDecodeResult.Decoded,
            TerrariaPlayerVitalsCodec.TryDecodeHealth(decodedHealthFrame, out TerrariaPlayerHealthState health));
        Assert.Equal(created.Player.Slot.Value, health.PlayerId);
        Assert.Equal((short)100, health.Life);

        Assert.True(registry.TryGetServerPlayerItemFrame(created.Player, 0, out OutboundFrame itemFrame));
        TerrariaFrame decodedItemFrame = Decode(itemFrame);
        Assert.Equal(
            TerrariaPlayerEquipmentDecodeResult.Decoded,
            TerrariaPlayerEquipmentCodec.TryDecode(decodedItemFrame, out TerrariaPlayerEquipmentState equipment));
        Assert.Equal(created.Player.Slot.Value, equipment.PlayerId);
        Assert.Equal((short)VanillaItemIds.DirtBlock.Value, equipment.ItemNetId);

        Assert.True(states.TrySetMotion(created.Player, 176f, 336f, 1f, 2f, out PlayerStateSnapshot moved));
        registry.ServerPlayerMoved(in moved);
        Assert.True(registry.TryGetServerPlayerMovementFrame(created.Player, out OutboundFrame movementFrame));
        TerrariaFrame decodedMovementFrame = Decode(movementFrame);
        Assert.Equal(
            TerrariaPlayerMovementDecodeResult.Decoded,
            TerrariaPlayerMovementDecoder.TryDecode(decodedMovementFrame, out TerrariaPlayerMovementRequest movement));
        Assert.Equal(created.Player.Slot.Value, movement.ClaimedPlayerId);
        Assert.Equal(176f, movement.PositionX);
        Assert.Equal(7, outbound.QueuedFrames);

        Assert.True(service.Despawn(id));
        Assert.Equal(8, outbound.QueuedFrames);
        Assert.False(registry.TryGetServerPlayerMovementFrame(created.Player, out _));
        occupied.Dispose();
    }

    [Fact]
    public void Newly_playing_client_receives_complete_existing_server_player_baseline()
    {
        var registry = new RuntimeConnectionRegistry();
        var slots = new PlayerSlotPool(2);
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? occupied));
        Assert.NotNull(occupied);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var service = new ServerPlayerAuthority(states, identities, events: registry);
        var id = new ServerPlayerId("test:baseline-fake");
        ServerPlayerCreateResult created = service.Create(id, 160f, 320f);
        Assert.True(created.IsCreated);
        ServerPlayerAppearanceState appearance = CreateAppearance("Baseline");
        var vitals = new ServerPlayerVitalsState(100, 100, 20, 20);
        var item = new ServerPlayerItemState(0, VanillaItemIds.DirtBlock, 1, default, 0);
        Assert.True(service.SetAppearance(id, in appearance));
        Assert.True(service.SetVitals(id, in vitals));
        Assert.True(service.SetItem(id, in item));

        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(7002);
        ConnectionHandle real = Connection(source, slot: 0);
        Assert.True(registry.TryRegister(source, outbound));
        PlayerSpawnCommitRequest realSpawn = CreateSpawn(real.Player.Slot);
        registry.PlayerSpawned(real, in realSpawn);

        Assert.Equal(6, outbound.QueuedFrames);
        occupied.Dispose();
    }

    private static TerrariaFrame Decode(OutboundFrame outbound)
    {
        var input = new ReadOnlySequence<byte>(outbound.Bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        return frame;
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 32_768, maxFrameBytes: 1_024));

    private static ConnectionHandle Connection(GameCommandSourceId source, byte slot) =>
        new(source, new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(1)));

    private static PlayerSpawnCommitRequest CreateSpawn(PlayerSlotId slot) =>
        new(slot, 100, 200, 0, 0, 0, 0, 0);

    private static ServerPlayerAppearanceState CreateAppearance(string name) =>
        new(
            SkinVariant: 0,
            VoiceVariant: 1,
            VoicePitchOffset: 0f,
            Hair: 0,
            Name: name,
            HairDye: 0,
            HideVisibleAccessory: 0,
            HideMisc: 0,
            HairColor: new PlayerRgbColor(1, 2, 3),
            SkinColor: new PlayerRgbColor(4, 5, 6),
            EyeColor: new PlayerRgbColor(7, 8, 9),
            ShirtColor: new PlayerRgbColor(10, 11, 12),
            UnderShirtColor: new PlayerRgbColor(13, 14, 15),
            PantsColor: new PlayerRgbColor(16, 17, 18),
            ShoeColor: new PlayerRgbColor(19, 20, 21),
            DifficultyFlags: 0,
            TorchAndCartFlags: 0,
            ConsumableUnlockFlags: 0);
}
