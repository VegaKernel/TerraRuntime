using TerraRuntime.Core;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

public sealed partial class ServerPlayerStateStore
{
    private const byte ControlUseItemFlag = 1 << 5;

    /// <summary>
    /// Commits one server-owned kinematic update. This is deliberately not a physics implementation: G6-D will
    /// compute validated gravity/collision results and call this single-writer commit surface.
    /// </summary>
    public bool TrySetMotion(
        PlayerHandle player,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        out PlayerStateSnapshot snapshot)
    {
        if (!TryGetState(player, out ServerPlayerRuntimeState? state))
        {
            snapshot = default;
            return false;
        }

        return TrySetMotion(
            player,
            positionX,
            positionY,
            velocityX,
            velocityY,
            state.ControlFlags,
            out snapshot);
    }

    /// <summary>
    /// Commits server-owned kinematics together with Terraria packet-13 button/facing state. The flags are produced
    /// by the runtime controller, not accepted from a remote client.
    /// </summary>
    public bool TrySetMotion(
        PlayerHandle player,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        byte controlFlags,
        out PlayerStateSnapshot snapshot)
    {
        if (!float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY) ||
            !TryGetState(player, out ServerPlayerRuntimeState? state) ||
            state.Revision == ulong.MaxValue)
        {
            snapshot = default;
            return false;
        }

        state.Revision++;
        state.PositionX = positionX;
        state.PositionY = positionY;
        state.VelocityX = velocityX;
        state.VelocityY = velocityY;
        state.ControlFlags = controlFlags;
        snapshot = state.CaptureSnapshot();
        return true;
    }

    /// <summary>
    /// Commits the hotbar selection and server-owned use-button state carried by Terraria packet 13.
    /// Runtime players expose only the ten vanilla hotbar slots as held items.
    /// </summary>
    public bool TrySetHeldItem(
        PlayerHandle player,
        byte selectedItem,
        bool useItem,
        out PlayerStateSnapshot snapshot)
    {
        if (selectedItem >= 10 ||
            !TryGetState(player, out ServerPlayerRuntimeState? state) ||
            state.Revision == ulong.MaxValue)
        {
            snapshot = default;
            return false;
        }

        byte controlFlags = useItem
            ? (byte)(state.ControlFlags | ControlUseItemFlag)
            : (byte)(state.ControlFlags & ~ControlUseItemFlag);
        if (state.SelectedItem == selectedItem && state.ControlFlags == controlFlags)
        {
            snapshot = state.CaptureSnapshot();
            return true;
        }

        state.Revision++;
        state.SelectedItem = selectedItem;
        state.ControlFlags = controlFlags;
        snapshot = state.CaptureSnapshot();
        return true;
    }

    public bool TrySetDead(
        PlayerHandle player,
        bool isDead,
        out PlayerStateSnapshot snapshot)
    {
        if (!TryGetState(player, out ServerPlayerRuntimeState? state) ||
            state.Revision == ulong.MaxValue)
        {
            snapshot = default;
            return false;
        }

        state.Revision++;
        state.IsDead = isDead;
        snapshot = state.CaptureSnapshot();
        return true;
    }

    public bool TrySetVitals(
        PlayerHandle player,
        in ServerPlayerVitalsState vitals,
        out PlayerStateSnapshot snapshot)
    {
        if (!TryGetState(player, out ServerPlayerRuntimeState? state) ||
            state.Revision == ulong.MaxValue)
        {
            snapshot = default;
            return false;
        }

        var health = new PlayerHealthCommitRequest(player.Slot, vitals.Life, vitals.MaxLife);
        PlayerHealthCommitRequest normalizedHealth = VanillaVitalsRules.NormalizeHealth(in health);
        state.Revision++;
        state.HasHealth = true;
        state.Life = normalizedHealth.Life;
        state.MaxLife = normalizedHealth.MaxLife;
        state.IsDead = normalizedHealth.Life <= 0;
        state.HasMana = true;
        state.Mana = vitals.Mana;
        state.MaxMana = vitals.MaxMana;
        snapshot = state.CaptureSnapshot();
        return true;
    }
}
