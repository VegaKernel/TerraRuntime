using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectilePlayerCombatPass
{
    private static bool CanDealHostileProjectileDamage(ProjectileTypeId type, in ProjectileLifecycleState lifecycle)
    {
        // Projectile.Damage_CanDealDamage: Empress lance/sun-dance are harmless through localAI[0] == 60.
        if ((type == VanillaProjectileIds.FairyQueenLance || type == VanillaProjectileIds.FairyQueenSunDance) &&
            lifecycle.LocalAi.Ai0 <= 60f)
        {
            return false;
        }
        return true;
    }

    private bool IntersectsHostile(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        in ProjectileLifecycleState lifecycle,
        NpcHandle sourceNpc,
        RuntimePlayerMember player)
    {
        float playerLeft = player.PositionX;
        float playerTop = player.PositionY;
        float playerWidth = PlayerAuthority.VanillaBasePlayerWidth;
        float playerHeight = PlayerAuthority.VanillaBasePlayerHeight;

        // Projectile.Colliding source gates: the eye is harmless before its firing phase, while a sphere
        // that is still attached to its source cannot hurt until ai[0] reaches 60 (unless ai[1] == -1).
        if (projectile.Type == VanillaProjectileIds.PhantasmalEye && projectile.Ai.Ai0 < 2f)
            return false;
        if (projectile.Type == VanillaProjectileIds.PhantasmalSphere &&
            projectile.Ai.Ai0 >= 0f && projectile.Ai.Ai0 < 60f && projectile.Ai.Ai1 != -1f)
        {
            return false;
        }

        if (projectile.Type == VanillaProjectileIds.CultistBossIceMist && projectile.Ai.Ai1 != 1f)
        {
            // TerrariaServer 1.4.5.8 Projectile.Colliding, type 464: child mist does not use its 60x60
            // sprite box. Six 30x30 lobes move from the center toward a 720 px radial ring over 45 updates.
            float centerX = projectile.PositionX + definition.Width * 0.5f;
            float centerY = projectile.PositionY + definition.Height * 0.5f;
            float velocityAngle = MathF.Atan2(projectile.VelocityY, projectile.VelocityX);
            float baseAngle = velocityAngle - MathF.PI * 0.5f;
            float phase = (projectile.Ai.Ai0 % 45f) / 45f;
            float radialDistance = 720f * phase;
            for (int lobe = 0; lobe < 6; lobe++)
            {
                float angle = baseAngle + lobe * (MathF.PI * 2f / 6f);
                float lobeX = centerX + MathF.Cos(angle) * radialDistance;
                float lobeY = centerY + MathF.Sin(angle) * radialDistance;
                if (IntersectsDynamicAabb(
                        lobeX - 15f, lobeY - 15f, 30f, 30f,
                        playerLeft, playerTop, playerWidth, playerHeight))
                {
                    return true;
                }
            }
            return false;
        }

        if (projectile.Type == VanillaProjectileIds.QueenSlimeSmash)
        {
            float size = lifecycle.LocalAi.Ai1 > 0f ? lifecycle.LocalAi.Ai1 : definition.Width;
            return IntersectsDynamicAabb(projectile.PositionX, projectile.PositionY, size, size, playerLeft, playerTop, playerWidth, playerHeight);
        }

        if (projectile.Type == VanillaProjectileIds.Sharknado || projectile.Type == VanillaProjectileIds.Cthulunado)
        {
            float width = lifecycle.LocalAi.Ai1 > 0f ? lifecycle.LocalAi.Ai1 : definition.Width;
            float height = lifecycle.LocalAi.Ai2 > 0f ? lifecycle.LocalAi.Ai2 : definition.Height;
            return IntersectsDynamicAabb(projectile.PositionX, projectile.PositionY, width, height, playerLeft, playerTop, playerWidth, playerHeight);
        }

        if (projectile.Type == VanillaProjectileIds.PhantasmalDeathray)
        {
            if (lifecycle.LocalAi.Ai0 < 20f || !npcs.TryGet(sourceNpc, out NpcSnapshot source))
                return false;

            float maxScale = source.TypeIdentity == VanillaNpcIds.MoonLordHead
                ? 1f
                : source.TypeIdentity == VanillaNpcIds.MoonLordFreeEye
                    ? 0.4f
                    : 0f;
            if (!(maxScale > 0f))
                return false;

            float scale = MathF.Sin(lifecycle.LocalAi.Ai0 * (MathF.PI / 180f)) * 10f * maxScale;
            scale = MathF.Min(scale, maxScale);
            if (!(scale > 0f) || !float.IsFinite(scale) || !(lifecycle.LocalAi.Ai1 > 0f) ||
                !float.IsFinite(lifecycle.LocalAi.Ai1))
            {
                return false;
            }

            float centerX = projectile.PositionX + definition.Width * 0.5f;
            float centerY = projectile.PositionY + definition.Height * 0.5f;
            float endX = centerX + projectile.VelocityX * lifecycle.LocalAi.Ai1;
            float endY = centerY + projectile.VelocityY * lifecycle.LocalAi.Ai1;
            return CheckAabbVsLine(
                playerLeft, playerTop, playerWidth, playerHeight,
                centerX, centerY, endX, endY, definition.Width * scale);
        }

        if (projectile.Type == VanillaProjectileIds.FairyQueenLance)
        {
            float centerX = projectile.PositionX + definition.Width * 0.5f;
            float centerY = projectile.PositionY + definition.Height * 0.5f;
            float cos = MathF.Cos(projectile.Ai.Ai0);
            float sin = MathF.Sin(projectile.Ai.Ai0);
            return CheckAabbVsLine(
                playerLeft, playerTop, playerWidth, playerHeight,
                centerX - cos * 40f, centerY - sin * 40f,
                centerX + cos * 40f, centerY + sin * 40f,
                8f);
        }

        if (projectile.Type == VanillaProjectileIds.FairyQueenSunDance)
        {
            float localAi0 = lifecycle.LocalAi.Ai0;
            float scaleIn = Math.Clamp(localAi0 / 20f, 0f, 1f);
            float scaleOut = Math.Clamp((180f - localAi0) / 60f, 0f, 1f);
            float scale = scaleIn * scaleOut;
            if (!(scale > 0f))
                return false;

            float rotationLerp = Math.Clamp((localAi0 - 50f) / 130f, 0f, 1f);
            float rotation = projectile.Ai.Ai0 + rotationLerp * (MathF.PI / 9f);
            float dirX = MathF.Cos(rotation);
            float dirY = MathF.Sin(rotation);
            float centerX = projectile.PositionX + definition.Width * 0.5f;
            float centerY = projectile.PositionY + definition.Height * 0.5f;
            float rayX = dirX * scale;
            float rayY = dirY * scale;
            float widthScale = scale * 0.7f;

            return CheckAabbVsLine(playerLeft, playerTop, playerWidth, playerHeight, centerX, centerY, centerX + rayX * 510f, centerY + rayY * 510f, widthScale * 100f) ||
                   CheckAabbVsLine(playerLeft, playerTop, playerWidth, playerHeight, centerX, centerY, centerX + rayX * 660f, centerY + rayY * 660f, widthScale * 60f) ||
                   CheckAabbVsLine(playerLeft, playerTop, playerWidth, playerHeight, centerX, centerY, centerX + rayX * 800f, centerY + rayY * 800f, widthScale * 10f);
        }

        return Intersects(in projectile, in definition, player);
    }

    private static float GetHostileProjectileCenterX(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        in ProjectileLifecycleState lifecycle)
    {
        if (projectile.Type == VanillaProjectileIds.QueenSlimeSmash && lifecycle.LocalAi.Ai1 > 0f)
            return projectile.PositionX + lifecycle.LocalAi.Ai1 * 0.5f;
        if ((projectile.Type == VanillaProjectileIds.Sharknado || projectile.Type == VanillaProjectileIds.Cthulunado) &&
            lifecycle.LocalAi.Ai1 > 0f)
        {
            return projectile.PositionX + lifecycle.LocalAi.Ai1 * 0.5f;
        }
        return projectile.PositionX + definition.Width * 0.5f;
    }

    private static bool IntersectsDynamicAabb(
        float left,
        float top,
        float width,
        float height,
        float playerLeft,
        float playerTop,
        float playerWidth,
        float playerHeight) =>
        left < playerLeft + playerWidth &&
        left + width > playerLeft &&
        top < playerTop + playerHeight &&
        top + height > playerTop;

    // Allocation-free port of Terraria Collision.CheckAABBvLineCollision's gameplay geometry. The rectangle
    // is transformed into line-local coordinates and tested against the finite strip [0,length] x +/-width/2.
    private static bool CheckAabbVsLine(
        float rectX, float rectY, float rectWidth, float rectHeight,
        float lineStartX, float lineStartY, float lineEndX, float lineEndY, float lineWidth)
    {
        float halfWidth = lineWidth * 0.5f;
        float broadLeft = MathF.Min(lineStartX, lineEndX) - halfWidth;
        float broadTop = MathF.Min(lineStartY, lineEndY) - halfWidth;
        float broadRight = MathF.Max(lineStartX, lineEndX) + halfWidth;
        float broadBottom = MathF.Max(lineStartY, lineEndY) + halfWidth;
        if (!(rectX < broadRight && rectX + rectWidth > broadLeft && rectY < broadBottom && rectY + rectHeight > broadTop))
            return false;

        float dx = lineEndX - lineStartX;
        float dy = lineEndY - lineStartY;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (!(length > 0f) || !float.IsFinite(length))
            return false;
        float cos = dx / length;
        float sin = dy / length;

        Span<float> xs = stackalloc float[4];
        Span<float> ys = stackalloc float[4];
        TransformLineLocal(rectX, rectY, lineStartX, lineStartY, cos, sin, out xs[0], out ys[0]);
        TransformLineLocal(rectX + rectWidth, rectY, lineStartX, lineStartY, cos, sin, out xs[1], out ys[1]);
        TransformLineLocal(rectX + rectWidth, rectY + rectHeight, lineStartX, lineStartY, cos, sin, out xs[2], out ys[2]);
        TransformLineLocal(rectX, rectY + rectHeight, lineStartX, lineStartY, cos, sin, out xs[3], out ys[3]);

        for (int i = 0; i < 4; i++)
        {
            if (MathF.Abs(ys[i]) < halfWidth && xs[i] >= 0f && xs[i] < length)
                return true;
        }

        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) & 3;
            if (SegmentCrossesHorizontalStripEdge(xs[i], ys[i], xs[j], ys[j], halfWidth, length) ||
                SegmentCrossesHorizontalStripEdge(xs[i], ys[i], xs[j], ys[j], -halfWidth, length))
            {
                return true;
            }
        }
        return false;
    }

    private static void TransformLineLocal(
        float x, float y, float originX, float originY, float cos, float sin,
        out float localX, out float localY)
    {
        float rx = x - originX;
        float ry = y - originY;
        localX = rx * cos + ry * sin;
        localY = -rx * sin + ry * cos;
    }

    private static bool SegmentCrossesHorizontalStripEdge(
        float x0, float y0, float x1, float y1, float edgeY, float length)
    {
        float dy = y1 - y0;
        if (dy == 0f)
            return y0 == edgeY && MathF.Max(MathF.Min(x0, x1), 0f) <= MathF.Min(MathF.Max(x0, x1), length);
        float t = (edgeY - y0) / dy;
        if (t < 0f || t > 1f)
            return false;
        float x = x0 + (x1 - x0) * t;
        return x >= 0f && x <= length;
    }

}
