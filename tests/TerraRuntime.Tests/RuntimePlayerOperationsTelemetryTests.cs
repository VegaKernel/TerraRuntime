using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerOperationsTelemetryTests
{
    [Fact]
    public void Authoritative_events_publish_generation_safe_player_snapshot()
    {
        var telemetry = new RuntimePlayerOperationsTelemetry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(7);
        var slot = new PlayerSlotId(3);
        var player = new PlayerHandle(slot, new PlayerSessionGeneration(2));
        var connection = new ConnectionHandle(source, player);

        PlayerAppearanceCommitRequest appearance = CreateAppearance(slot, "Alice");
        telemetry.PlayerAppearanceUpdated(connection, in appearance);
        var health = new PlayerHealthCommitRequest(slot, 87, 100);
        telemetry.PlayerHealthUpdated(connection, in health);
        var mana = new PlayerManaCommitRequest(slot, 16, 20);
        telemetry.PlayerManaUpdated(connection, in mana);
        var spawn = new PlayerSpawnCommitRequest(slot, 100, 200, 0, 0, 0, 4, 0);
        telemetry.PlayerSpawned(connection, in spawn);
        var movement = new PlayerMovementCommitRequest(
            slot,
            ControlFlags: 0,
            MovementFlags: 0,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: 8,
            PositionX: 321f,
            PositionY: 654f,
            HasVelocity: true,
            VelocityX: 1.5f,
            VelocityY: -2.25f,
            HasMount: true,
            MountType: 11,
            HasPotionOfReturnPositions: false,
            PotionOfReturnOriginalPositionX: 0f,
            PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f,
            PotionOfReturnHomePositionY: 0f,
            HasCameraTarget: false,
            CameraTargetX: 0f,
            CameraTargetY: 0f);
        telemetry.PlayerMoved(connection, in movement);

        RuntimePlayersSnapshot snapshot = telemetry.CaptureSnapshot();
        Assert.Equal(1, snapshot.Players.Length);
        RuntimePlayerSnapshot live = snapshot.Players.Span[0];
        Assert.Equal(7, live.ConnectionId);
        Assert.Equal((byte)3, live.Slot);
        Assert.Equal(2UL, live.Generation);
        Assert.Equal("Alice", live.Name);
        Assert.Equal((byte)4, live.Team);
        Assert.Equal(321f, live.PositionX);
        Assert.Equal(654f, live.PositionY);
        Assert.Equal(1.5f, live.VelocityX);
        Assert.Equal(-2.25f, live.VelocityY);
        Assert.Equal((byte)8, live.SelectedItem);
        Assert.Equal((ushort)11, live.MountType);
        Assert.True(live.HasHealth);
        Assert.Equal((short)87, live.Life);
        Assert.Equal((short)100, live.MaxLife);
        Assert.True(live.HasMana);
        Assert.Equal((short)16, live.Mana);
        Assert.Equal((short)20, live.MaxMana);

        PlayerMovementCommitRequest stoppedMovement = movement with
        {
            SelectedItem = 9,
            PositionX = 400f,
            PositionY = 700f,
            HasVelocity = false,
            HasMount = false
        };
        telemetry.PlayerMoved(connection, in stoppedMovement);

        live = telemetry.CaptureSnapshot().Players.Span[0];
        Assert.Equal(400f, live.PositionX);
        Assert.Equal(700f, live.PositionY);
        Assert.Equal(0f, live.VelocityX);
        Assert.Equal(0f, live.VelocityY);
        Assert.Equal((byte)9, live.SelectedItem);
        Assert.Equal((ushort)0, live.MountType);

        telemetry.PlayerDisconnected(connection);
        Assert.Equal(0, telemetry.CaptureSnapshot().Players.Length);
    }

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
