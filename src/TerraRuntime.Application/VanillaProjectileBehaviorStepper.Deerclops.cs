using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal static partial class VanillaProjectileBehaviorStepper
{
    private static bool TryStepDeerclopsShadowHand(
        in ProjectileSnapshot current,
        in VanillaProjectileDefinition definition,
        out VanillaProjectileBehaviorResult next)
    {
        float ai0 = current.Ai.Ai0;
        if (!TryResolveShadowHandPhase(ai0, out int variation, out int fakeCounter, out int counterMax))
        {
            next = new VanillaProjectileBehaviorResult(
                current.VelocityX, current.VelocityY, ai0, Kill: true);
            return true;
        }

        // AI_187 kills before applying the last movement step of each variation.
        if (fakeCounter >= counterMax - 1)
        {
            next = new VanillaProjectileBehaviorResult(
                current.VelocityX, current.VelocityY, ai0, Kill: true);
            return true;
        }

        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;
        float? positionX = null;
        float? positionY = null;
        float fromValue = fakeCounter / (float)counterMax;

        switch (variation)
        {
            case 0:
                velocityX *= 0.98f;
                velocityY *= 0.98f;
                break;

            case 1:
            {
                int direction = velocityX > 0f ? 1 : -1;
                if (MathF.Sqrt(velocityX * velocityX + velocityY * velocityY) > 0.1f)
                {
                    velocityX *= 0.95f;
                    velocityY *= 0.95f;
                }

                // Projectile.rotation is local-only and is not part of packet 27. For this source variation it is
                // fully derivable from the phase counter because SetDefaults starts it at zero and every delta is
                // a pure function of elapsed phase time and horizontal direction. Reconstructing it here avoids
                // smuggling a presentation-only rotation field into synchronized projectile AI.
                float rotationBefore = ComputeShadowOrbitRotation(fakeCounter, direction);
                float delta = ComputeShadowOrbitDelta(fromValue) * -direction;
                float rotationAfter = WrapAngle(rotationBefore + delta);
                float radius = 70f * direction;
                float centerX = current.PositionX + definition.Width * 0.5f;
                float centerY = current.PositionY + definition.Height * 0.5f;
                float anchorX = centerX - MathF.Cos(rotationBefore) * radius;
                float anchorY = centerY - MathF.Sin(rotationBefore) * radius;
                float nextCenterX = anchorX + MathF.Cos(rotationAfter) * radius;
                float nextCenterY = anchorY + MathF.Sin(rotationAfter) * radius;
                positionX = nextCenterX - definition.Width * 0.5f;
                positionY = nextCenterY - definition.Height * 0.5f;
                break;
            }

            case 2:
            {
                float speed =
                    Remap(fromValue, 0f, 0.4f, 1f, 0f) * 2f +
                    Remap(fromValue, 0.3f, 0.4f, 0f, 1f) *
                    Remap(fromValue, 0.4f, 1f, 1f, 0f) * 8f +
                    0.01f;
                velocityX = MathF.Cos(current.Ai.Ai1) * speed;
                velocityY = MathF.Sin(current.Ai.Ai1) * speed;
                break;
            }

            case 3:
                Rotate(ref velocityX, ref velocityY, current.Ai.Ai1);
                break;
        }

        next = new VanillaProjectileBehaviorResult(
            velocityX,
            velocityY,
            ai0 + 1f,
            PositionXOverride: positionX,
            PositionYOverride: positionY);
        return true;
    }


    private static bool TryResolveShadowHandPhase(
        float ai0,
        out int variation,
        out int fakeCounter,
        out int counterMax)
    {
        int counter = (int)ai0;
        if (counter is >= 0 and < 180)
        {
            variation = 0;
            fakeCounter = counter;
            counterMax = 180;
            return true;
        }
        if (counter is >= 180 and < 300)
        {
            variation = 1;
            fakeCounter = counter - 180;
            counterMax = 120;
            return true;
        }
        if (counter is >= 300 and < 390)
        {
            variation = 2;
            fakeCounter = counter - 300;
            counterMax = 90;
            return true;
        }
        if (counter is >= 390 and < 480)
        {
            variation = 3;
            fakeCounter = counter - 390;
            counterMax = 90;
            return true;
        }

        variation = default;
        fakeCounter = default;
        counterMax = default;
        return false;
    }


    private static float ComputeShadowOrbitRotation(int elapsedTicks, int direction)
    {
        float rotation = 0f;
        for (int tick = 0; tick < elapsedTicks; tick++)
        {
            float progress = tick / 120f;
            rotation = WrapAngle(rotation + ComputeShadowOrbitDelta(progress) * -direction);
        }
        return rotation;
    }


    private static float ComputeShadowOrbitDelta(float progress)
    {
        float forward = Remap(progress, 0.3f, 0.5f, 0f, 1f) *
                        Remap(progress, 0.45f, 0.5f, 1f, 0f);
        float reverse = Remap(progress, 0.5f, 0.55f, 0f, 1f) *
                        Remap(progress, 0.5f, 1f, 1f, 0f);
        return forward * MathF.PI / 60f - reverse * MathF.PI * 8f / 60f;
    }
}
