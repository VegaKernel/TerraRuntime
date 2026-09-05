using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal static partial class VanillaProjectileBehaviorStepper
{
    private static void Rotate(ref float x, ref float y, float radians)
    {
        float oldX = x;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        x = oldX * cos - y * sin;
        y = oldX * sin + y * cos;
    }


    private static float AngleLerp(float current, float target, float amount)
    {
        float delta = WrapAngle(target - current);
        return WrapAngle(current + delta * amount);
    }


    private static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax)
    {
        float amount = Math.Clamp((value - fromMin) / (fromMax - fromMin), 0f, 1f);
        return toMin + (toMax - toMin) * amount;
    }


    private static float WrapAngle(float angle)
    {
        while (angle <= -MathF.PI)
            angle += MathF.PI * 2f;
        while (angle > MathF.PI)
            angle -= MathF.PI * 2f;
        return angle;
    }


    private static bool TryFindClosestPlayer(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        IRuntimePlayerSlotSnapshotLookup? players,
        out float centerX,
        out float centerY) =>
        TryFindClosestPlayer(in projectile, in definition, players, out _, out centerX, out centerY);

    private static bool TryFindClosestPlayer(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        IRuntimePlayerSlotSnapshotLookup? players,
        out PlayerSlotId slot,
        out float centerX,
        out float centerY)
    {
        slot = default;
        centerX = 0f;
        centerY = 0f;
        if (players is null)
            return false;

        float projectileCenterX = projectile.PositionX + definition.Width * 0.5f;
        float projectileCenterY = projectile.PositionY + definition.Height * 0.5f;
        float bestDistanceSquared = float.PositiveInfinity;
        bool found = false;
        for (int rawSlot = 0; rawSlot < byte.MaxValue; rawSlot++)
        {
            var candidateSlot = new PlayerSlotId(checked((byte)rawSlot));
            if (!players.TryGetPlayer(candidateSlot, out PlayerStateSnapshot player) || player.IsDead)
                continue;

            float playerCenterX = player.PositionX + 10f;
            float playerCenterY = player.PositionY + 21f;
            float dx = playerCenterX - projectileCenterX;
            float dy = playerCenterY - projectileCenterY;
            float distanceSquared = dx * dx + dy * dy;
            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            slot = candidateSlot;
            centerX = playerCenterX;
            centerY = playerCenterY;
            found = true;
        }
        return found;
    }
}
