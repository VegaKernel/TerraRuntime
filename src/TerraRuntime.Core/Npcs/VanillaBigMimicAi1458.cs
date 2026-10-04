using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;
internal sealed class VanillaBigMimicAi1458
{
    public float Phase, Clock, Repeat, Hops, Vx, Vy, TargetX, TargetY;
    public float X, Y;
    public int Width = 28, Height = 44, Dir = 1, DirY = 1, Sprite = 1, Life = 3500, LifeMax = 3500, Damage = 90, Defense = 34, Alpha = 3;
    public int Type, Target, OldTarget, OldDir = 1, OldDirY = 1;
    public int TargetWidth = 20, TargetHeight = 42;
    public float Difficulty = 1f;
    public bool TargetDead, JustHit, Confused, CollideX, CollideY, Tenth;
    public VanillaBigMimicTarget1458? Closest;
    public float Knockback;
    public bool Expert, NoGravity, NoTile, Invulnerable, Reflects, Sync;
    private IVanillaBigMimicEnvironment1458 environment = null!;
    private bool Sight => environment.CanHit(CenterX, CenterY, TargetX, TargetY);
    private bool BodySolid => environment.SolidCollision(X, Y, Width, Height);
    private float CenterX => X + Width * .5f;
    private float CenterY => Y + Height * .5f;

    private void Face()
    {
        if (Closest is { } closest)
        {
            Target = closest.Slot;
            TargetX = closest.CenterX;
            TargetY = closest.CenterY;
            TargetWidth = closest.Width;
            TargetHeight = closest.Height;
            TargetDead = closest.Dead;
        }

        if (!TargetDead)
        {
            Dir = (int)(TargetX - TargetWidth * .5f) + TargetWidth / 2 < X + Width / 2 ? -1 : 1;
            DirY = (int)(TargetY - TargetHeight * .5f) + TargetHeight / 2 < Y + Height / 2 ? -1 : 1;
        }

        if (Confused)
            Dir *= -1;
        if ((Dir != OldDir || DirY != OldDirY || Target != OldTarget) && !CollideX && !CollideY)
            Sync = true;
    }

    private void Restart(float phase)
    {
        Phase = phase;
        Clock = Repeat = Hops = 0f;
        Sync = true;
    }

    private static float Length(float x, float y) => (float)Math.Sqrt(x * x + y * y);
    private static void Normalize(ref float x, ref float y)
    {
        float inverse = 1f / Length(x, y);
        x *= inverse;
        y *= inverse;
    }

    public int Step(IVanillaBigMimicEnvironment1458 world, IVanillaNpcRandom random, Span<VanillaBigMimicCannonItem1458> cannon)
    {
        environment = world;
        int cannonCount = 0;
        float multiplier = 1f + (Math.Clamp(Difficulty, 1f, 3f) - 1f) * (.8f - 1f) / 2f;
        Knockback = .2f * multiplier;
        NoTile = NoGravity = Invulnerable = Reflects = false;
        if (Phase != 7f && TargetDead)
        {
            Face();
            if (TargetDead)
                Restart(7f);
        }

        float dx = TargetX - CenterX, dy = TargetY - CenterY;
        switch (Phase)
        {
            case 0f:
                Face();
                if (Vx != 0f || Vy > 100f || JustHit || Length(TargetX - CenterX, TargetY - CenterY) < 80f)
                {
                    Phase = 1f;
                    Clock = 0f;
                    Sync = true;
                }

                break;
            case 1f:
                Clock++;
                if (Clock > 36f)
                {
                    Phase = 2f;
                    Clock = 0f;
                    Sync = true;
                }

                break;
            case 2f:
                if (Length(dx, dy) > 600f)
                    Restart(5f);
                if (Vy == 0f)
                {
                    Face();
                    Vx *= .85f;
                    Clock++;
                    float health = (float)Life / LifeMax;
                    float wait = 15f + 30f * health;
                    float speed = 3f + 4f * (1f - health);
                    float lift = Sight ? 4f : 6f;
                    if (Clock > wait)
                    {
                        Hops++;
                        if (Hops >= 3f)
                        {
                            Hops = 0f;
                            lift *= 2f;
                            speed /= 2f;
                        }

                        Clock = 0f;
                        Vy -= lift;
                        Vx = speed * Dir;
                        Sync = true;
                    }
                }
                else
                {
                    Knockback = 0f;
                    Vx *= .99f;
                    if (Dir < 0 && Vx > -1f)
                        Vx = -1f;
                    if (Dir > 0 && Vx < 1f)
                        Vx = 1f;
                }

                Repeat++;
                if (Repeat > 210f && Vy == 0f)
                {
                    int choice = random.NextInt32(0, 3);
                    Restart(choice == 0 ? 3f : choice == 1 ? 4f : 6f);
                    if (choice == 1)
                    {
                        NoTile = true;
                        Vy = -8f;
                    }

                    if (Tenth && Type == 476 && Phase == 3f && random.NextInt32(0, 2) == 0)
                        Phase = 8f;
                }

                break;
            case 3f:
                Vx *= .85f;
                Invulnerable = true;
                Clock++;
                if (Clock >= 180f)
                {
                    Phase = 2f;
                    Clock = 0f;
                    Sync = true;
                }

                Reflects = Expert;
                break;
            case 4f:
                NoTile = NoGravity = true;
                Knockback = 0f;
                Dir = Vx < 0f ? -1 : 1;
                Sprite = Dir;
                Face();
                dx = TargetX - CenterX;
                dy = TargetY - CenterY;
                if (Repeat == 1f)
                {
                    Clock++;
                    Normalize(ref dx, ref dy);
                    dx *= 8f;
                    dy *= 8f;
                    Vx = (Vx * 4f + dx) / 5f;
                    Vy = (Vy * 4f + dy) / 5f;
                    if (Clock > 6f)
                    {
                        Clock = 0f;
                        Phase = 4.1f;
                        Repeat = 0f;
                        Vx = dx;
                        Vy = dy;
                        Sync = true;
                    }
                }
                else if (Math.Abs(dx) < 40f && CenterY < TargetY - 300f)
                {
                    Clock = 0f;
                    Repeat = 1f;
                    Sync = true;
                }
                else
                {
                    dy -= 350f;
                    Normalize(ref dx, ref dy);
                    dx *= 12f;
                    dy *= 12f;
                    Vx = (Vx * 5f + dx) / 6f;
                    Vy = (Vy * 5f + dy) / 6f;
                }

                break;
            case 4.1f:
                Knockback = 0f;
                if (Repeat == 0f && Sight && !BodySolid)
                    Repeat = 1f;
                if (Y + Height >= TargetY - TargetHeight * .5f || Vy <= 0f)
                {
                    Clock++;
                    if (Clock > 10f)
                        Restart(BodySolid ? 5f : 2f);
                }
                else if (Repeat == 0f)
                {
                    NoTile = NoGravity = true;
                    Knockback = 0f;
                }

                Vy += .2f;
                if (Vy > 16f)
                    Vy = 16f;
                break;
            case 5f:
                Dir = Vx > 0f ? 1 : -1;
                Sprite = Dir;
                NoTile = NoGravity = true;
                Knockback = 0f;
                dy -= 4f;
                if (Length(dx, dy) < 200f && !BodySolid)
                    Restart(2f);
                if (Length(dx, dy) > 10f)
                {
                    Normalize(ref dx, ref dy);
                    dx *= 10f;
                    dy *= 10f;
                }

                Vx = (Vx * 4f + dx) / 5f;
                Vy = (Vy * 4f + dy) / 5f;
                break;
            case 6f:
                Knockback = 0f;
                if (Vy == 0f)
                {
                    Face();
                    Vx *= .8f;
                    Clock++;
                    if (Clock > 5f)
                    {
                        Clock = 0f;
                        Vy -= 4f;
                        float bottom = TargetY + TargetHeight * .5f;
                        float[] heights = [0f, 40f, 80f, 120f, 160f, 200f];
                        float[] boosts = [1.25f, 1.5f, 1.75f, 2f, 2.25f, 2.5f];
                        for (int index = 0; index < heights.Length; index++)
                            if (bottom < CenterY - heights[index])
                                Vy -= boosts[index];
                        if (!Sight)
                            Vy -= 2f;
                        Vx = 12 * Dir;
                        Repeat++;
                        Sync = true;
                    }
                }
                else
                {
                    Vx *= .98f;
                    if (Dir < 0 && Vx > -8f)
                        Vx = -8f;
                    if (Dir > 0 && Vx < 8f)
                        Vx = 8f;
                }

                if (Repeat >= 3f && Vy == 0f)
                    Restart(2f);
                break;
            case 7f:
                Damage = 0;
                Life = LifeMax;
                Defense = 9999;
                NoTile = true;
                Alpha = Math.Min(255, Alpha + 7);
                Vx *= .98f;
                break;
            case 8f:
                Vx *= .85f;
                Clock++;
                if (!Tenth || Clock >= 180f)
                {
                    Phase = 2f;
                    Clock = 0f;
                    Sync = true;
                }
                else if (Clock % 20f == 0f)
                {
                    if (cannon.Length < 10)
                        return -1;
                    var items = VanillaBigMimicNpcCatalog1458.StuffCannonItems;
                    for (int index = 0; index < 10; index++)
                    {
                        int item = items[random.NextInt32(0, items.Length)];
                        float speed = random.NextInt32(10, 26);
                        float aimX = TargetX - CenterX;
                        float aimY = TargetY - 120f - CenterY;
                        aimX += random.NextInt32(-50, 51) * .1f;
                        aimY += random.NextInt32(-50, 51) * .1f;
                        float scale = speed / Length(aimX, aimY);
                        aimX *= scale;
                        aimY *= scale;
                        aimX += random.NextInt32(-50, 51) * .1f;
                        aimY += random.NextInt32(-50, 51) * .1f;
                        cannon[index] = new(item, aimX, aimY);
                    }

                    cannonCount = 10;
                }

                break;
        }

        return cannonCount;
    }
}

internal readonly record struct VanillaBigMimicTarget1458(byte Slot, float CenterX, float CenterY, int Width, int Height, bool Dead);
internal readonly record struct VanillaBigMimicCannonItem1458(int Item, float VelocityX, float VelocityY);
