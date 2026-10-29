using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>A source NewProjectile request, before its definition-owned center-to-body conversion.</summary>
public readonly record struct VanillaMartianChildShot1458(
    ProjectileTypeId Type, float CenterX, float CenterY, float VelocityX, float VelocityY, int Damage);

/// <summary>Read-only parent body and motion consumed by attachment AI, without inventing a parent handle.</summary>
public readonly record struct VanillaMartianChildParent1458(float PositionX, float PositionY,
    float VelocityX, float VelocityY, int Width, int Height, ushort Target, int TimeLeft,
    int Direction, int SpriteDirection);

/// <summary>Dedicated-server AI072/073/075 transitions for the three Martian child identities, 1.4.5.8.
/// Parent lookup, NewNPC, Transform, collision and adoption remain with their authoritative owners.</summary>
public static class VanillaMartianChildAi1458
{
    public static bool IsSupported(NpcTypeId type, NpcNetId net) => net.Value == type.Value &&
        (type == VanillaNpcIds.ForceBubble || type == VanillaNpcIds.MartianTurret || type == VanillaNpcIds.ScutlixRider);

    public static NpcSnapshot FollowBubble(in NpcSnapshot source, in VanillaNpcHitboxSize body,
        in VanillaMartianChildParent1458 parent) => source with
        {
            PositionX = parent.PositionX + parent.Width * .5f - body.Width / 2,
            PositionY = parent.PositionY + parent.Height * .5f - body.Height / 2,
            VelocityX = 0f, VelocityY = 0f
        };

    public static bool TryStepTurret(in NpcSnapshot source, in VanillaNpcHitboxSize body,
        in VanillaNpcRawPlayer1458 target, IVanillaNpcRandom random,
        out NpcSnapshot next, out bool force, out VanillaMartianChildShot1458? shot)
    {
        next = default; force = false; shot = null;
        if (source.TypeIdentity != VanillaNpcIds.MartianTurret || !target.IsValid ||
            source.Target != target.Slot || body.Width <= 0 || body.Height <= 0) return false;
        float vx = source.VelocityX * .93f;
        if (vx > -.1f && vx < .1f) vx = 0f;
        var ai = source.Ai;
        var simulation = source.Simulation with { SpriteDirection = source.Simulation.DirectionX };
        if (ai.Ai1 < 120f)
        {
            ai = ai with { Ai1 = ai.Ai1 + 1f };
            simulation = simulation with { Alpha = ai.Ai1 > 60f ? (int)((1f - (ai.Ai1 - 60f) / 60f) * 255f) : 255,
                DontTakeDamage = true, FrameCounter = 0d, FrameIndex = 0 };
        }
        else
        {
            if (ai.Ai1 == 120f) { ai = ai with { Ai1 = 121f }; force = true; }
            simulation = simulation with { DontTakeDamage = false, Alpha = 0 };
            float centerX = source.PositionX + body.Width * .5f;
            float centerY = source.PositionY + body.Height * .5f;
            float targetX = target.PositionX + target.Width * .5f;
            float targetY = target.PositionY + target.Height * .5f;
            bool eligible = target.Active && !target.Dead &&
                VanillaNpcGlobalFiringDistance.Contains(centerX, centerY, targetX, targetY);
            if (ai.Ai0 == 0f && !eligible) ai = ai with { Ai0 = -120f };
            if (ai.Ai0 < 60f) ai = ai with { Ai0 = ai.Ai0 + 1f };
            if (simulation.JustHit) { ai = ai with { Ai0 = -30f }; force = true; }
            // Once a positive wind-up exists, the source does not recheck eligibility at the firing edge.
            if (ai.Ai0 == 60f)
            {
                if (simulation.SpawnDifficulty is not { } difficulty) return false;
                ai = ai with { Ai0 = -120f }; force = true;
                float originY = centerY - 10f;
                var velocity = Spread(targetX - centerX, targetY - originY, 100, 70, 131, 14f, random);
                int damage = (int)(35f + (35f * .8f - 35f) * Math.Clamp(difficulty - 1f, 0f, 1f));
                shot = new(VanillaProjectileIds.MartianTurretBolt, centerX, originY, velocity.X, velocity.Y, damage);
            }
        }
        next = source with { VelocityX = vx, Ai = ai, Simulation = simulation };
        return simulation.IsValid;
    }

    public static bool TryStepRider(in NpcSnapshot source, in VanillaNpcHitboxSize body,
        in VanillaMartianChildParent1458 mount, in VanillaNpcRawPlayer1458 target,
        bool canHit, IVanillaNpcRandom random,
        out NpcSnapshot next, out bool force, out VanillaMartianChildShot1458? shot)
    {
        next = default; force = false; shot = null;
        if (source.TypeIdentity != VanillaNpcIds.ScutlixRider || !target.IsValid ||
            mount.Target != target.Slot || mount.Width <= 0 || mount.Height <= 0 ||
            body.Width <= 0 || body.Height <= 0) return false;
        float x = mount.PositionX + mount.Width * .5f - body.Width / 2;
        float y = mount.PositionY + mount.Height * .5f - body.Height / 2 - 14f;
        float vx = mount.VelocityX;
        var ai = source.Ai;
        if (ai.Ai1 < 60f) ai = ai with { Ai1 = ai.Ai1 + 1f };
        if (source.Simulation.JustHit) ai = ai with { Ai1 = -30f };
        if (canHit)
        {
            float dx = target.PositionX + target.Width * .5f - (x + body.Width * .5f);
            float dy = target.PositionY + target.Height * .5f - (y + body.Height * .5f);
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            if (distance < 700f)
            {
                if (ai.Ai1 == 60f && Math.Sign(dx) == mount.Direction)
                {
                    ai = ai with { Ai1 = -60f }; force = true;
                    float originX = x + body.Width * .5f, originY = y + body.Height * .5f - 4f;
                    var velocity = Spread(target.PositionX + target.Width * .5f - originX,
                        target.PositionY + target.Height * .5f - originY, 50, 80, 121, 7f, random);
                    shot = new(VanillaProjectileIds.RayGunnerLaser, originX, originY, velocity.X, velocity.Y, 30);
                }
                else
                {
                    float reciprocal = 1f / distance;
                    float aimX = dx * reciprocal, aimY = dy * reciprocal;
                    float pose = Math.Abs(aimY) > Math.Abs(aimX) * 2f ? aimY > 0f ? 1f : 5f :
                        Math.Abs(aimX) > Math.Abs(aimY) * 2f ? 3f : aimY > 0f ? 2f : 4f;
                    force = ai.Ai2 != pose;
                    ai = ai with { Ai2 = pose };
                    vx *= .5f;
                }
            }
        }
        var simulation = source.Simulation with { TimeLeft = mount.TimeLeft,
            DirectionX = mount.Direction, SpriteDirection = mount.SpriteDirection };
        next = source with { PositionX = x, PositionY = y, VelocityX = vx, VelocityY = mount.VelocityY,
            Target = mount.Target, Ai = ai, Simulation = simulation };
        return simulation.IsValid;
    }

    private static (float X, float Y) Spread(float x, float y, int offset, int minimumScale,
        int exclusiveMaximumScale, float speed, IVanillaNpcRandom random)
    {
        x += random.NextInt32(-offset, offset + 1);
        y += random.NextInt32(-offset, offset + 1);
        x *= random.NextInt32(minimumScale, exclusiveMaximumScale) * .01f;
        y *= random.NextInt32(minimumScale, exclusiveMaximumScale) * .01f;
        float reciprocal = 1f / (float)Math.Sqrt(x * x + y * y);
        x *= reciprocal; y *= reciprocal;
        if (float.IsNaN(x) || float.IsNaN(y)) { x = 0f; y = -1f; }
        return (x * speed, y * speed);
    }
}
