using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

// Mutable accepted-state scratch; no speculative store or irreversible publication.
internal sealed class VanillaMothronAiState1458
{
    internal int Type = 478;
    internal float X, Y, Vx, Vy, OldVx, OldVy, Rotation;
    internal float A0, A1, A2 = .5f, A3;
    internal int Width = 34, Height = 34, Life = 100, LifeMax = 200;
    internal int Damage, Defense = 30, Direction = 1, DirectionY = 1, Sprite = 1, TimeLeft = 750;
    internal float Knockback = 1f;
    internal bool NoGravity, NoTile, Invulnerable, CollideX, CollideY, Force;
    internal int BaseDamage, HatchLifeMax, HatchDamage;
    internal float KnockbackMultiplier = 1f;
    internal int OldDirection = 1, OldDirectionY = 1;
    internal ushort Target, OldTarget;
    internal float FacingX, FacingY;
    internal bool Confused;
    internal (int Slot, float X, float Y)? Egg;
}

internal static class VanillaMothronYoungAi1458
{
    internal static void StepEgg(VanillaMothronAiState1458 state, bool expert, bool hit,
        float playerCenterX, float playerCenterY, IVanillaNpcRandom random)
    {
        bool grounded = state.Vy == 0f;
        state.Vx *= grounded ? .9f : .99f;
        state.Rotation += state.Vx * (grounded ? .02f : .04f);
        int hatchClock = expert ? 600 : 900;
        if (hit)
        {
            state.A0 -= random.NextInt32(10, 21);
            if (!expert)
                state.A0 -= random.NextInt32(10, 21);
        }
        state.A0++;
        if (state.A0 >= hatchClock)
            Hatch(state, expert, playerCenterX, playerCenterY, random);

        // The threshold is the egg's old difficulty threshold even after transformation.
        if (state.Vy != 0f || Math.Abs(state.Vx) >= .2d || state.A0 < hatchClock * .75d)
            return;

        float progress = (state.A0 - hatchClock * .75f) / (hatchClock * .25f);
        if (random.NextInt32(-10, 120) >= progress * 100f)
            return;

        state.Vy -= random.NextInt32(20, 40) * .025f;
        state.Vx += random.NextInt32(-20, 20) * .025f;
        float speedMultiplier = 1f + progress * 2f;
        state.Vx *= speedMultiplier;
        state.Vy *= speedMultiplier;
        state.Force = true;
    }

    private static void Hatch(VanillaMothronAiState1458 state, bool expert, float playerX, float playerY,
        IVanillaNpcRandom random)
    {
        int previousLife = state.Life;
        int previousLifeMax = state.LifeMax;
        float bottom = state.Y + state.Height;
        state.Type = 479;
        state.Width = 46;
        state.Height = 30;
        state.Y = bottom - state.Height;
        state.LifeMax = state.HatchLifeMax;
        state.Life = Math.Max(1, previousLife * state.LifeMax / previousLifeMax);
        state.Damage = state.HatchDamage;
        state.Defense = 14;
        state.Knockback = .3f * state.KnockbackMultiplier;
        state.NoGravity = false;
        state.NoTile = false;
        state.Invulnerable = false;
        state.CollideX = false;
        state.CollideY = false;
        state.A0 = state.A1 = state.A2 = state.A3 = 0f;
        state.Rotation = 0f;
        FaceTarget(state, playerX, playerY);
        state.Sprite = state.Direction;
        // Original dedicated-server TransformVisuals still executes these random argument/branch calls.
        for (int dust = 0; dust < 30; dust++)
            random.NextInt32(0, 2);
        random.NextInt32(0, 3);
        state.Force = true;
    }

    internal static void StepBaby(VanillaMothronAiState1458 state, bool expert, bool eclipse,
        float playerX, float playerY, bool bodySolid, (float X, float Y)? repelledVelocity = null)
    {
        state.NoTile = false;
        state.Knockback = .4f * state.KnockbackMultiplier;
        state.NoGravity = true;
        state.Rotation = (state.Rotation * 9f + state.Vx * .1f) / 10f;
        if (!eclipse)
        {
            state.TimeLeft = Math.Min(state.TimeLeft, 5);
            state.Vy = Math.Max(state.Vy - .2f, -8f);
            state.NoTile = true;
            return;
        }

        if (repelledVelocity is { } repelled)
            (state.Vx, state.Vy) = repelled;
        float dx = playerX - (state.X + state.Width * .5f);
        float dy = playerY - (state.Y + state.Height * .5f);
        float distance = Length(dx, dy);
        if (state.A0 > 1f && distance > 1000f)
            state.A0 = 1f;

        if (state.A0 == -1f)
        {
            state.Vx = state.Vx * 9f / 10f;
            state.Vy = (state.Vy * 9f - 8f) / 10f;
            state.NoTile = true;
            state.Invulnerable = true;
        }
        else if (state.A0 == 0f)
        {
            FaceTarget(state, playerX, playerY);
            state.Sprite = state.Direction;
            if (state.CollideX)
                state.Vx = Math.Clamp(state.Vx * (-state.OldVx * .5f), -4f, 4f);
            if (state.CollideY)
                state.Vy = Math.Clamp(state.Vy * (-state.OldVy * .5f), -4f, 4f);
            if (distance > 800f)
            {
                state.A0 = 1f;
                state.A1 = state.A2 = state.A3 = 0f;
            }
            else if (distance > 200f)
                Blend(state, Normalize(dx, dy, 5.5f + distance / 100f + state.A1 / 15f), 40f);
            else
            {
                float speed = Length(state.Vx, state.Vy);
                if (speed > 2f)
                {
                    state.Vx *= .95f;
                    state.Vy *= .95f;
                }
                else if (speed < 1f)
                {
                    state.Vx *= 1.05f;
                    state.Vy *= 1.05f;
                }
            }
            if (++state.A1 >= 90f)
            {
                state.A1 = 0f;
                state.A0 = 2f;
            }
        }
        else if (state.A0 == 1f)
        {
            state.CollideX = state.CollideY = false;
            state.NoTile = true;
            state.Knockback = 0f;
            FaceVelocity(state);
            state.Sprite = state.Direction;
            state.Rotation = (state.Rotation * 9f + state.Vx * .08f) / 10f;
            if (distance < 300f && !bodySolid)
                state.A0 = state.A1 = state.A2 = state.A3 = 0f;
            state.A2 += 1f / 60f;
            Blend(state, Normalize(dx, dy, 5.5f + state.A2 + distance / 150f), 35f);
        }
        else if (state.A0 == 2f)
        {
            FaceVelocity(state);
            state.Sprite = state.Direction;
            state.Rotation = (state.Rotation * 7f + state.Vx * .1f) / 8f;
            state.Knockback = 0f;
            state.NoTile = true;
            var charge = Normalize(dx, dy - 8f, 9f);
            Blend(state, charge, 8f);
            state.Direction = state.Vx < 0f ? -1 : 1;
            state.Sprite = state.Direction;
            if (++state.A1 > 10f)
            {
                state.Vx = charge.X;
                state.Vy = charge.Y;
                state.Direction = state.Vx < 0f ? -1 : 1;
                state.A0 = 2.1f;
                state.A1 = 0f;
            }
        }
        else if (state.A0 == 2.1f)
        {
            FaceVelocity(state);
            state.Sprite = state.Direction;
            state.Vx *= 1.01f;
            state.Vy *= 1.01f;
            state.Knockback = 0f;
            state.NoTile = true;
            state.A1++;
            if (state.A1 > 45f && !bodySolid)
                state.A0 = state.A1 = state.A2 = 0f;
            else if (state.A1 > 90f && bodySolid)
            {
                state.A0 = 1f;
                state.A1 = state.A2 = 0f;
            }
        }
    }

    internal static void FaceTarget(VanillaMothronAiState1458 state, float playerX, float playerY)
    {
        state.Direction = state.FacingX < state.X + state.Width / 2 ? -1 : 1;
        state.DirectionY = state.FacingY < state.Y + state.Height / 2 ? -1 : 1;
        if (state.Confused)
            state.Direction *= -1;
        if ((state.Direction != state.OldDirection || state.DirectionY != state.OldDirectionY || state.Target != state.OldTarget) && !state.CollideX && !state.CollideY)
            state.Force = true;
    }

    internal static void FaceVelocity(VanillaMothronAiState1458 state)
    {
        if (state.Vx < 0f)
            state.Direction = -1;
        else if (state.Vx > 0f)
            state.Direction = 1;
    }

    internal static float Length(float x, float y) => (float)Math.Sqrt(x * x + y * y);

    internal static (float X, float Y) Normalize(float x, float y, float speed)
    {
        float reciprocal = 1f / Length(x, y);
        return (x * reciprocal * speed, y * reciprocal * speed);
    }

    internal static void Blend(VanillaMothronAiState1458 state, (float X, float Y) aim, float inertia)
    {
        state.Vx = (state.Vx * (inertia - 1f) + aim.X) / inertia;
        state.Vy = (state.Vy * (inertia - 1f) + aim.Y) / inertia;
    }
}
