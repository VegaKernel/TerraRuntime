using TerraRuntime.Gameplay.Npcs;
using static TerraRuntime.Core.Npcs.VanillaMothronYoungAi1458;

namespace TerraRuntime.Core.Npcs;

internal static class VanillaMothronParentAi1458
{
    internal static bool TryStep(VanillaMothronAiState1458 state, bool expert, bool eclipse, bool hit,
        float playerX, float playerY, int actorSlot, IVanillaMothronEnvironment1458 environment, IVanillaNpcRandom random, int youngCount)
    {
        random.NextInt32(0, 600);
        // Genuine outer IdleSounds branch; no extra variant draw.
        int baseDamage = state.BaseDamage;
        state.NoTile = false;
        state.NoGravity = true;
        state.Knockback = .2f * state.KnockbackMultiplier;
        state.Damage = baseDamage;
        float dx = playerX - (state.X + state.Width * .5f);
        float dy = playerY - (state.Y + state.Height * .5f);
        bool BodySolid() => environment.SolidCollision(state.X, state.Y, state.Width, state.Height);
        bool Sight() => environment.CanHit(state.X + state.Width * .5f, state.Y + state.Height * .5f, playerX, playerY);
        if (!eclipse)
        {
            state.Force |= state.A0 != -1f;
            state.A0 = -1f;
        }
        else if (state.A0 > 1f && Length(dx, dy) > 1000f)
        {
            state.Force |= state.A0 != 1f;
            state.A0 = 1f;
        }

        switch (state.A0)
        {
            case -1f:
                state.Vx = state.Vx * 9f / 10f;
                state.Vy = (state.Vy * 9f - 8f) / 10f;
                state.NoTile = true;
                state.Invulnerable = true;
                break;
            case 0f:
                FaceTarget(state, playerX, playerY);
                if (dx > 2f)
                    state.Direction = 1;
                if (dx < -2f)
                    state.Direction = -1;
                state.Sprite = state.Direction;
                state.Rotation = (state.Rotation * 9f + state.Vx * .1f) / 10f;
                Bounce(state);
                float hoverDistance = Length(dx, dy - 200f);
                if (hoverDistance > 800f)
                {
                    Reset(state, 1f);
                    state.Force = true;
                }
                else if (hoverDistance > 80f)
                    Blend(state, Normalize(dx, dy - 200f, 6f), 30f);
                else
                    AdjustIdleSpeed(state);
                state.A1++;
                if (hit)
                    state.A1 += random.NextInt32(10, 30);
                if (state.A1 >= 180f)
                {
                    state.A1 = state.A2 = state.A3 = 0f;
                    state.Force = true;
                    int retries = 0;
                    while (state.A0 == 0f)
                    {
                        if (++retries > 64)
                            return false;
                        int choice = random.NextInt32(0, 3);
                        if (choice == 0 && Sight())
                            state.A0 = 2f;
                        else if (choice == 1)
                            state.A0 = 3f;
                        else if (choice == 2 && youngCount < 7)
                            state.A0 = 4f;
                    }
                }
                break;
            case 1f:
                state.CollideX = state.CollideY = false;
                state.NoTile = true;
                state.Knockback = 0f;
                FaceVelocity(state);
                state.Sprite = state.Direction;
                state.Rotation = (state.Rotation * 9f + state.Vx * .08f) / 10f;
                if (Length(dx, dy) < 300f && !BodySolid())
                {
                    Reset(state, 0f);
                    state.Force = true;
                }
                Blend(state, Normalize(dx, dy, 7f + Length(dx, dy) / 100f), 25f);
                break;
            case 2f:
                state.Damage = (int)(baseDamage * .5d);
                state.Knockback = 0f;
                state.Direction = dx - 10f < 0f ? -1 : dx + 10f > 0f ? 1 : state.Direction;
                state.Sprite = state.Direction;
                state.Rotation = (state.Rotation * 4f + state.Vx * .1f) / 5f;
                Bounce(state);
                state.A2 += 1f / 45f;
                if (expert)
                    state.A2 += 1f / 60f;
                Blend(state, Normalize(dx, dy - 20f, 4f + state.A2 + Length(dx, dy - 20f) / 120f), 20f);
                if (++state.A1 > 240f || !Sight())
                {
                    Reset(state, 0f);
                    state.Force = true;
                }
                break;
            case 3f:
                state.Knockback = 0f;
                state.NoTile = true;
                state.Direction = state.Vx < 0f ? -1 : 1;
                state.Sprite = state.Direction;
                state.Rotation = (state.Rotation * 4f + state.Vx * .07f) / 5f;
                float flankX = dx + (dx < 0f ? 400f : -400f);
                if (Math.Abs(dx) > 350f && Math.Abs(dy) < 20f)
                {
                    state.A0 = 3.1f;
                    state.A1 = 0f;
                    state.Force = true;
                }
                state.A1 += 1f / 30f;
                Blend(state, Normalize(flankX, dy - 12f, 8f + state.A1), 4f);
                break;
            case 3.1f:
                state.Knockback = 0f;
                state.NoTile = true;
                state.Rotation = (state.Rotation * 4f + state.Vx * .07f) / 5f;
                var dash = Normalize(dx, dy - 12f, 16f);
                Blend(state, dash, 8f);
                state.Direction = state.Vx < 0f ? -1 : 1;
                state.Sprite = state.Direction;
                if (++state.A1 > 10f)
                {
                    state.Vx = dash.X;
                    state.Vy = dash.Y;
                    state.Direction = state.Vx < 0f ? -1 : 1;
                    state.A0 = 3.2f;
                    state.A1 = state.Direction;
                    state.Force = true;
                }
                break;
            case 3.2f:
                state.Damage = (int)(baseDamage * 1.3d);
                state.CollideX = state.CollideY = false;
                state.Knockback = 0f;
                state.NoTile = true;
                state.A2 += 1f / 30f;
                state.Vx = (16f + state.A2) * state.A1;
                if ((state.A1 > 0f && dx < -260f) || (state.A1 < 0f && dx > 260f))
                {
                    if (!BodySolid())
                    {
                        Reset(state, 0f);
                        state.Force = true;
                    }
                    else if (Math.Abs(dx) > 800f)
                    {
                        Reset(state, 1f);
                        state.Force = true;
                    }
                }
                state.Rotation = (state.Rotation * 4f + state.Vx * .07f) / 5f;
                break;
            case 4f:
                FaceTarget(state, playerX, playerY);
                FindEggSite(state, playerX, playerY, environment, random);
                state.Force = true;
                break;
            case 4.1f:
                if (state.Vx < -2f)
                    state.Direction = -1;
                else if (state.Vx > 2f)
                    state.Direction = 1;
                state.Sprite = state.Direction;
                state.Rotation = (state.Rotation * 9f + state.Vx * .1f) / 10f;
                state.NoTile = true;
                var approaching = SiteDelta(state);
                float speed = Math.Min(6f + Length(approaching.X, approaching.Y) / 150f, 10f);
                if (Length(approaching.X, approaching.Y) < 10f)
                {
                    state.A0 = 4.2f;
                    state.Force = true;
                }
                var safeAim = Length(approaching.X, approaching.Y) == 0f ? (0f, 0f) : Normalize(approaching.X, approaching.Y, speed);
                Blend(state, safeAim, 10f);
                Cap(state, speed);
                break;
            case 4.2f:
                state.Rotation = (state.Rotation * 9f + state.Vx * .1f) / 10f;
                state.Knockback = 0f;
                state.NoTile = true;
                var holding = SiteDelta(state);
                if (Length(holding.X, holding.Y) < 4f)
                {
                    int layingClock = expert ? 52 : 70;
                    state.A3++;
                    if (state.A3 == layingClock)
                        state.Egg = (actorSlot + 1, (int)state.A1 * 16 + 8 - 17f, (int)state.A2 * 16 - 34f);
                    else if (state.A3 == layingClock * 2)
                    {
                        Reset(state, 0f);
                        state.Force = true;
                        if (youngCount < 7 && random.NextInt32(0, 3) != 0)
                            state.A0 = 4f;
                        else if (BodySolid())
                            state.A0 = 1f;
                    }
                }
                if (Length(holding.X, holding.Y) > 4f)
                    holding = Normalize(holding.X, holding.Y, 4f);
                Blend(state, holding, 2f);
                Cap(state, 4f);
                break;
        }
        return true;
    }

    private static void FindEggSite(VanillaMothronAiState1458 state, float playerX, float playerY,
        IVanillaMothronEnvironment1458 environment, IVanillaNpcRandom random)
    {
        bool aboveSurface = (state.Y + state.Height * .5f) / 16f < environment.WorldSurfaceTiles;
        state.A0 = state.A1 = state.A2 = 0f;
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            int x = (int)playerX / 16 + random.NextInt32(-30 - attempt / 50, 31 + attempt / 50);
            int y = (int)playerY / 16 + random.NextInt32(-20 - attempt / 75, 21 + attempt / 75);
            if (Solid(environment, x, y))
                continue;
            int remaining = 50;
            bool invalid = false;
            while (remaining > 0)
            {
                remaining--;
                if (x < 5 || x >= environment.WidthTiles - 5 || y < 5 || y >= environment.HeightTiles - 5)
                {
                    invalid = true;
                    break;
                }
                if (!environment.TryReadTile(x, y, out _, out bool lava) || lava ||
                    !environment.TryReadTile(x, y - 1, out _, out bool lavaAbove) || lavaAbove)
                {
                    invalid = true;
                    break;
                }
                if (Solid(environment, x, y) || (aboveSurface && y > environment.WorldSurfaceTiles))
                    break;
                y++;
            }
            if (remaining <= 0 || invalid || Length(x * 16 + 8 - playerX, y * 16 + 8 - playerY) >= 600f)
                continue;
            state.A0 = 4.1f;
            state.A1 = x;
            state.A2 = y;
            break;
        }
    }

    private static bool Solid(IVanillaMothronEnvironment1458 environment, int x, int y) => environment.TryReadTile(x, y, out bool solid, out _) && solid;
    private static (float X, float Y) SiteDelta(VanillaMothronAiState1458 state) =>
        ((int)state.A1 * 16 + 8 - (state.X + state.Width * .5f), (int)state.A2 * 16 - 20 - (state.Y + state.Height * .5f));
    private static void Reset(VanillaMothronAiState1458 state, float phase)
    {
        state.A0 = phase;
        state.A1 = state.A2 = state.A3 = 0f;
    }
    private static void Bounce(VanillaMothronAiState1458 state)
    {
        if (state.CollideX)
            state.Vx = Math.Clamp(state.Vx * (-state.OldVx * .5f), -4f, 4f);
        if (state.CollideY)
            state.Vy = Math.Clamp(state.Vy * (-state.OldVy * .5f), -4f, 4f);
    }
    private static void AdjustIdleSpeed(VanillaMothronAiState1458 state)
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
    private static void Cap(VanillaMothronAiState1458 state, float maximum)
    {
        if (Length(state.Vx, state.Vy) <= maximum)
            return;
        var capped = Normalize(state.Vx, state.Vy, maximum);
        state.Vx = capped.X;
        state.Vy = capped.Y;
    }
}
