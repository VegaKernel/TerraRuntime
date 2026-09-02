using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public sealed partial class RuntimeServerPlayerStateStore
{
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
        PlayerHealthCommitRequest normalizedHealth = VanillaPlayerHealthNormalizer.Normalize(in health);
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
