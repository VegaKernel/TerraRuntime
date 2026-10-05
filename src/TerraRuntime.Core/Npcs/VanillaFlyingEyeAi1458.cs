using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;
internal readonly record struct VanillaFlyingEyeTarget1458(byte Slot, float X, float Y, int Width, int Height);
/// <summary>Complete bounded AI_002 state ordering; world queries and the trusted random stream are explicit inputs.</summary>
internal sealed class VanillaFlyingEyeAi1458
{
    public int Type;
    public float X, Y, Vx, Vy, OldVx, OldVy, Scale = 1f;
    public int Width, Height, Direction, DirectionY, OldDirection, OldDirectionY, Target, OldTarget;
    public int Life, LifeMax, TimeLeft, Alpha;
    public bool NoTileCollide, CollideX, CollideY, Wet, Confused, DayTime, TargetInGraveyard, Force;
    public bool ShimmerTransparent;
    public bool ClosestDead;
    public double WorldSurfacePixels;
    public float Clock, Phase, Rotation;
    public VanillaFlyingEyeTarget1458 Current, Closest;
    public void Step(IVanillaFlyingEyeEnvironment environment, IVanillaNpcRandom random)
    {
        bool pigron = Type is 170 or 171 or 180;
        if (pigron)
        {
            // UpdateNPC calls IdleSounds before AI, including on the dedicated server.
            if (!ShimmerTransparent && random.NextInt32(0, 600) == 0)
                random.NextInt32(38, 41);
            random.NextInt32(0, 1000);
        }

        if (!NoTileCollide)
        {
            if (CollideX)
            {
                Vx = OldVx * -0.5f;
                if (Direction == -1 && Vx > 0f && Vx < 2f)
                    Vx = 2f;
                if (Direction == 1 && Vx < 0f && Vx > -2f)
                    Vx = -2f;
            }

            if (CollideY)
            {
                Vy = OldVy * -0.5f;
                if (Vy > 0f && Vy < 1f)
                    Vy = 1f;
                if (Vy < 0f && Vy > -1f)
                    Vy = -1f;
            }
        }

        bool retreat = Type is not (116 or 170 or 171 or 180) && DayTime && Y <= WorldSurfacePixels && !TargetInGraveyard;
        if (retreat)
        {
            if (TimeLeft > 10)
                TimeLeft = 10;
            Direction = Vx > 0f ? 1 : -1;
            DirectionY = -1;
        }
        else
            RefreshClosest();
        if (pigron)
        {
            bool sight = environment.CanHit(X, Y, Width, Height, Current.X, Current.Y, Current.Width, Current.Height);
            if (sight)
            {
                if (Phase > 0f && !environment.SolidCollision(X, Y, Width, Height))
                {
                    Phase = Clock = 0f;
                    Force = true;
                }
            }
            else if (Phase == 0f)
                Clock++;
            if (Clock >= 300f)
            {
                Phase = 1f;
                Clock = 0f;
                Force = true;
            }

            NoTileCollide = Phase != 0f;
            Alpha = NoTileCollide ? 200 : 0;
            if (NoTileCollide)
                Wet = false;
            Rotation = Vy * 0.1f * Direction;
            RefreshClosest();
        }
        else if (Type == 116)
            RefreshClosest();
        bool enraged = Type == 133 && (double)Life < (double)LifeMax * 0.5;
        float speedScale = pigron || Type is 116 or 133 ? 1f : 1f + (1f - Scale);
        float maxX = (pigron ? 4f : Type == 116 || enraged ? 6f : 4f) * speedScale;
        float maxY = (pigron || Type == 116 ? 2.5f : enraged ? 4f : 1.5f) * speedScale;
        bool steerX = !pigron || (Direction == -1 ? X > Current.X + Current.Width : X + Width < Current.X);
        bool steerY = !pigron || (DirectionY == -1 ? Y > Current.Y + Current.Height : Y + Height < Current.Y);
        if (steerX)
            Vx = Axis(Vx, Direction, maxX, maxX, pigron ? 0.08f : 0.1f, pigron ? 0.04f : 0.1f, pigron || Type == 116 ? -0.2f : 0.05f);
        if (steerY)
            Vy = Axis(Vy, DirectionY, maxY, Type == 116 ? 1.5f : maxY, pigron || enraged ? 0.1f : 0.04f, enraged ? 0.1f : 0.05f, pigron || Type == 116 ? -0.15f : enraged ? 0.05f : 0.03f);
        // Dust.NewDust returns its server sentinel without further draws. The genuine offer gate remains.
        if (Type is 2 or 116 or 133 or 190 or 191 or 192 or 193 or 194)
            random.NextInt32(0, 40);
        if (Wet && !pigron)
        {
            if (Vy > 0f)
                Vy *= 0.95f;
            Vy -= 0.5f;
            if (Vy < -4f)
                Vy = -4f;
            RefreshClosest();
        }
    }

    private void RefreshClosest()
    {
        Current = Closest;
        Target = Closest.Slot;
        if (!ClosestDead)
        {
            Direction = (int)Current.X + Current.Width / 2 < X + Width / 2 ? -1 : 1;
            DirectionY = (int)Current.Y + Current.Height / 2 < Y + Height / 2 ? -1 : 1;
            if (Confused)
                Direction *= -1;
        }
        if ((Direction != OldDirection || DirectionY != OldDirectionY || Target != OldTarget) && !CollideX && !CollideY)
            Force = true;
    }

    private static float Axis(float value, int direction, float maximum, float positiveEngagement, float acceleration, float overshootAcceleration, float wrongDirectionBrake)
    {
        if (direction == -1 && value > -maximum)
        {
            value -= acceleration;
            if (value > maximum)
                value -= overshootAcceleration;
            else if (value > 0f)
                value += wrongDirectionBrake;
            if (value < -maximum)
                value = -maximum;
        }
        else if (direction == 1 && value < positiveEngagement)
        {
            value += acceleration;
            if (value < -maximum)
                value += overshootAcceleration;
            else if (value < 0f)
                value -= wrongDirectionBrake;
            if (value > maximum)
                value = maximum;
        }

        return value;
    }
}
