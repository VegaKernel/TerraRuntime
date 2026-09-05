using System.Buffers;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerAppearanceRelayTests
{
    [Fact]
    public void Pre_spawn_appearance_is_cached_and_exchanged_when_second_player_becomes_playing()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(1);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(2);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        var first = new PlayerSlotId(1);
        var second = new PlayerSlotId(2);
        ConnectionHandle firstConnection = Connection(firstSource, first);
        ConnectionHandle secondConnection = Connection(secondSource, second);

        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));

        PlayerAppearanceCommitRequest firstAppearance = CreateAppearance(first, "First");
        PlayerAppearanceCommitRequest secondAppearance = CreateAppearance(second, "Second");
        registry.PlayerAppearanceUpdated(firstConnection, in firstAppearance);
        registry.PlayerAppearanceUpdated(secondConnection, in secondAppearance);
        Assert.Equal(0, firstOutbound.QueuedFrames);
        Assert.Equal(0, secondOutbound.QueuedFrames);

        PlayerSpawnCommitRequest firstSpawn = CreateSpawn(first);
        registry.PlayerSpawned(firstConnection, in firstSpawn);
        Assert.Equal(0, firstOutbound.QueuedFrames);
        Assert.Equal(0, secondOutbound.QueuedFrames);

        PlayerSpawnCommitRequest secondSpawn = CreateSpawn(second);
        registry.PlayerSpawned(secondConnection, in secondSpawn);

        Assert.Equal(2, firstOutbound.QueuedFrames);
        Assert.Equal(2, secondOutbound.QueuedFrames);
        Assert.Equal(2, registry.PlayerActiveBaselineFrames);
        Assert.Equal(2, registry.AppearanceBaselineFrames);

        Assert.True(registry.TryGetLatestPlayerAppearanceFrame(first, out OutboundFrame firstFrame));
        Assert.True(registry.TryGetLatestPlayerAppearanceFrame(second, out OutboundFrame secondFrame));
        AssertAppearance(firstFrame, first.Value, "First");
        AssertAppearance(secondFrame, second.Value, "Second");
    }

    [Fact]
    public void Playing_appearance_update_relays_to_peer_with_authoritative_slot()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(10);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(20);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        var first = new PlayerSlotId(10);
        var second = new PlayerSlotId(20);
        ConnectionHandle firstConnection = Connection(firstSource, first);
        ConnectionHandle secondConnection = Connection(secondSource, second);

        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawn(first);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawn(second);
        registry.PlayerSpawned(firstConnection, in firstSpawn);
        registry.PlayerSpawned(secondConnection, in secondSpawn);

        PlayerAppearanceCommitRequest appearance = CreateAppearance(first, "Updated");
        registry.PlayerAppearanceUpdated(firstConnection, in appearance);

        Assert.Equal(1, firstOutbound.QueuedFrames);
        Assert.Equal(2, secondOutbound.QueuedFrames);
        Assert.Equal(1, registry.RelayedAppearanceFrames);
        Assert.True(registry.TryGetLatestPlayerAppearanceFrame(first, out OutboundFrame frame));
        AssertAppearance(frame, first.Value, "Updated");
    }

    [Fact]
    public void Identical_playing_appearance_update_is_not_relayed_twice()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(31);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(32);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        var first = new PlayerSlotId(3);
        var second = new PlayerSlotId(4);
        ConnectionHandle firstConnection = Connection(firstSource, first);
        ConnectionHandle secondConnection = Connection(secondSource, second);

        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawn(first);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawn(second);
        registry.PlayerSpawned(firstConnection, in firstSpawn);
        registry.PlayerSpawned(secondConnection, in secondSpawn);

        PlayerAppearanceCommitRequest appearance = CreateAppearance(first, "Stable");
        registry.PlayerAppearanceUpdated(firstConnection, in appearance);
        int afterFirst = secondOutbound.QueuedFrames;
        registry.PlayerAppearanceUpdated(firstConnection, in appearance);

        Assert.Equal(afterFirst, secondOutbound.QueuedFrames);
        Assert.Equal(1, registry.RelayedAppearanceFrames);
        Assert.Equal(1, registry.SuppressedDuplicateAppearanceFrames);
    }

    private static void AssertAppearance(OutboundFrame outbound, byte expectedSlot, string expectedName)
    {
        var input = new ReadOnlySequence<byte>(outbound.Bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        Assert.Equal(
            TerrariaPlayerAppearanceDecodeResult.Decoded,
            TerrariaPlayerAppearanceCodec.TryDecode(frame, out TerrariaPlayerAppearanceState appearance));
        Assert.Equal(expectedSlot, appearance.PlayerId);
        Assert.Equal(expectedName, appearance.Name);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));

    private static ConnectionHandle Connection(GameCommandSourceId source, PlayerSlotId slot) =>
        new(source, new PlayerHandle(slot, new PlayerSessionGeneration(1)));

    private static PlayerSpawnCommitRequest CreateSpawn(PlayerSlotId slot) =>
        new(
            slot,
            SpawnX: 100,
            SpawnY: 200,
            RespawnTimer: 0,
            DeathsPve: 0,
            DeathsPvp: 0,
            Team: 0,
            SpawnContext: 0);

    private static PlayerAppearanceCommitRequest CreateAppearance(PlayerSlotId slot, string name) =>
        new(
            slot,
            SkinVariant: 1,
            VoiceVariant: 2,
            VoicePitchOffset: 0.1f,
            Hair: 3,
            Name: name,
            HairDye: 4,
            HideVisibleAccessory: 5,
            HideMisc: 6,
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
