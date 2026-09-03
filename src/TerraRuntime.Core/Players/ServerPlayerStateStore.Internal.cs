using TerraRuntime.Core;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

public sealed partial class ServerPlayerStateStore
{
    private sealed class ServerPlayerRuntimeState
    {
        public ServerPlayerId Id { get; init; }
        public PlayerHandle Player { get; init; }
        public ulong Revision { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public bool IsDead { get; set; }
        public ServerPlayerAppearanceState? Appearance { get; set; }
        public bool HasHealth { get; set; }
        public short Life { get; set; }
        public short MaxLife { get; set; }
        public bool HasMana { get; set; }
        public short Mana { get; set; }
        public short MaxMana { get; set; }
        public Dictionary<short, ServerPlayerItemState>? Items { get; set; }

        public PlayerStateSnapshot CaptureSnapshot() =>
            new(
                Player,
                new PlayerStateRevision(Revision),
                Team: 0,
                ControlFlags: 0,
                MovementFlags: 0,
                MiscFlags1: 0,
                MiscFlags2: 0,
                SelectedItem: 0,
                PositionX,
                PositionY,
                VelocityX,
                VelocityY,
                MountType: 0,
                PotionOfReturnOriginalPositionX: 0f,
                PotionOfReturnOriginalPositionY: 0f,
                PotionOfReturnHomePositionX: 0f,
                PotionOfReturnHomePositionY: 0f,
                CameraTargetX: 0f,
                CameraTargetY: 0f)
            {
                Hostile = false,
                HasHealth = HasHealth,
                Life = Life,
                MaxLife = MaxLife,
                IsDead = IsDead,
                HasMana = HasMana,
                Mana = Mana,
                MaxMana = MaxMana
            };
    }
}
