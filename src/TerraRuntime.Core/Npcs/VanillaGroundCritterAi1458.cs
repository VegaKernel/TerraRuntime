using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal readonly record struct GroundCritterTile1458(bool Solid, bool SolidTop, bool TopSlope, bool Dungeon = false, bool StrictSolid = false);
internal readonly record struct GroundCritterStep1458(float X, float Y, float VelocityX, float VelocityY);
internal readonly record struct GroundCritterDanger1458(bool Present, int TowardDirection, bool SelfStinky);

// Core owns the AI state machine. The application supplies the existing world collision algorithms.
internal interface IVanillaGroundCritterTerrain1458
{
    GroundCritterTile1458 Tile(int x, int y);
    bool NavigationObstacle(int left, int right, int top, int bottom);
    bool Drowning(float x, float y, int width, int height);
    GroundCritterStep1458 StepDown(float x, float y, float vx, float vy, int width, int height);
    GroundCritterStep1458 StepUp(float x, float y, float vx, float vy, int width, int height, bool matchPlatforms);
    bool TryGetWaterLine(int centerTileX, int centerTileY, out bool found, out float waterLineHeight)
    { found = false; waterLineHeight = 0f; return false; }
}

/// <summary>
/// Clean implementation of the reachable AI007 body for the explicitly admitted nonresident bunnies. This type is transient
/// planning data; committed NPC state, random state and collision ownership stay with their existing owners.
/// </summary>
internal sealed class VanillaGroundCritterAi1458
{
    internal float X, Y, Vx, Vy;
    internal int Width, Height, Direction, HomeX, HomeY, RestingX, RestingY, Life, LifeMax, WorldWidthTiles;
    internal float Gravity;
    internal bool Wet, Homeless, ShelterWeather, Force, CollideX;
    internal bool Friendly = true;
    internal float OldX, OldY, OldVx, OldVy;
    internal NpcAiState Ai, Local;
    internal VanillaGroundCritterFamily1458 Family = VanillaGroundCritterFamily1458.Bunny;

    private bool Mouse => Family is VanillaGroundCritterFamily1458.Mouse or VanillaGroundCritterFamily1458.Rat;
    private bool Turtle => Family is VanillaGroundCritterFamily1458.Turtle or VanillaGroundCritterFamily1458.SeaTurtle;
    private bool Frog => Family == VanillaGroundCritterFamily1458.Frog;

    internal static bool IsSupported(int type) => VanillaNonresidentGroundCritterCatalog1458.IsSupported(type);

    internal bool TryStep(IVanillaGroundCritterTerrain1458 terrain, GroundCritterDanger1458 danger,
        IVanillaNpcRandom random)
    {
        if (Turtle) danger = danger with { Present = false, TowardDirection = 0 };
        if (Ai.Ai0 is not (0f or 1f or 8f) ||
            (Family == VanillaGroundCritterFamily1458.Squirrel ? Ai.Ai3 is not (0f or 2f) : Ai.Ai3 != 0f) || LifeMax <= 0 ||
            HomeX < -1 || HomeY < -1 || Direction is < -1 or > 1 ||
            danger.Present && danger.TowardDirection is not (-1 or 1))
            return false;
        if (Direction == 0) Direction = 1;
        int originX = (int)(X + Width / 2) / 16;
        int originY = (int)(Y + Height + 1f) / 16;
        bool drowning = !Frog && !Turtle && terrain.Drowning(X, Y, Width, Height);
        // TownCritter disables the cliff-avoidance scan and all resident home/seat/attack offers.
        // Danger can still interrupt a critter, including the blocked flee state 8.
        if (danger.Present)
        {
            if (Ai.Ai0 == 8f)
            {
                if (Direction == -danger.TowardDirection)
                    BeginWalking(300 + random.NextInt32(0, 300));
            }
            else if (Ai.Ai0 != 1f)
            {
                BeginWalking(120 + random.NextInt32(0, 120));
                Direction = -danger.TowardDirection;
            }
            else if (Direction != -danger.TowardDirection)
            {
                Direction = -danger.TowardDirection;
                Force = true;
            }
        }
        if (Ai.Ai0 == 0f)
        {
            if (danger.SelfStinky) Ai = Ai with { Ai0 = 1f };
            if (Local.Ai3 > 0f) Local = Local with { Ai3 = Local.Ai3 - 1f };
            if (Frog && Wet) BeginWalking(200 + random.NextInt32(500, 700));
            else
            {
                if (Mouse) Vx *= .5f;
                Vx = Vx > .1f ? Vx - .1f : Vx < -.1f ? Vx + .1f : 0f;
                if (Ai.Ai1 > 0f) Ai = Ai with { Ai1 = Ai.Ai1 - 1f };
                // The source keeps executing the idle branch after a transition, including its later
                // home-band turn offer. It does not execute the walking branch until the next update.
                if (Wet && drowning) BeginWalking(200 + random.NextInt32(0, 300) + random.NextInt32(200, 400));
                if (Ai.Ai1 <= 0f) BeginWalking(200 + random.NextInt32(0, 300) + random.NextInt32(200, 400));
            }
            if (!ShelterWeather || originX == RestingX && originY == RestingY && !(Frog && Wet))
            {
                if (originX < RestingX - 25 || originX > RestingX + 25)
                {
                    if (Local.Ai3 == 0f &&
                        (originX < RestingX - 50 && Direction == -1 || originX > RestingX + 50 && Direction == 1))
                    {
                        Direction *= -1;
                        Force = true;
                    }
                }
                else if (random.NextInt32(0, 80) == 0 && Local.Ai3 == 0f)
                {
                    Local = Local with { Ai3 = 200f };
                    Direction *= -1;
                    Force = true;
                }
            }
        }
        else if (Ai.Ai0 == 1f)
            Walk(terrain, danger, random, originX, drowning);
        else
        {
            Vx *= .8f;
            Ai = Ai with { Ai1 = Ai.Ai1 - 1f };
            if (Ai.Ai1 < 60f && danger.Present)
            {
                Ai = Ai with { Ai1 = 180f };
                Force = true;
            }
            if (Ai.Ai1 <= 0f)
            {
                Ai = Ai with { Ai0 = 0f, Ai1 = 60 + random.NextInt32(0, 60), Ai2 = 0f };
                Local = Local with { Ai3 = 30 + random.NextInt32(0, 60) };
                Force = true;
            }
        }
        if ((Frog || Turtle) && Wet)
        {
            if (WorldWidthTiles <= 0) return false;
            if (Frog) Ai = Ai with { Ai1 = 50f };
            float centerX = X + Width * .5f, centerY = Y + Height * .5f;
            int centerTileX = (int)(centerX / 16f);
            if (CollideX || centerTileX < 5 && Direction == -1 || centerTileX > WorldWidthTiles - 5 && Direction == 1)
            { Direction *= -1; Vx *= -.25f; Force = true; }
            // Utils.ToTileCoordinates truncates pixels before its arithmetic shift.
            if (!terrain.TryGetWaterLine((int)centerX >> 4, (int)centerY >> 4, out bool found, out float waterLine)) return false;
            if (found)
            {
                if (!float.IsFinite(waterLine)) return false;
                float nextCenter = centerY + 1f;
                if (centerY > waterLine)
                {
                    Vy = MathF.Max(-4f, Vy - .8f);
                    if (nextCenter + Vy < waterLine) Vy = waterLine - nextCenter;
                }
                else Vy = MathF.Min(Vy, waterLine - nextCenter);
            }
            else Vy -= .2f;
        }
        return float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Vx) && float.IsFinite(Vy) &&
            Ai.IsFinite && Local.IsFinite;
    }

    private void BeginWalking(float ticks)
    {
        Ai = Ai with { Ai0 = 1f, Ai1 = ticks, Ai2 = 0f };
        Local = Local with { Ai3 = 0f };
        Force = true;
    }

    private void Walk(IVanillaGroundCritterTerrain1458 terrain, GroundCritterDanger1458 danger,
        IVanillaNpcRandom random, int originX, bool drowning)
    {
        if (!drowning)
        {
            int originY = (int)(Y + Height + 1f) / 16;
            float extra = !Homeless && !terrain.Tile(originX, originY).Dungeon && Math.Abs(originX - RestingX) > 35 &&
                (X < RestingX * 16f && Direction == -1 || X > RestingX * 16f && Direction == 1) ? 5f : 0f;
            Ai = Ai with { Ai1 = Ai.Ai1 - extra - 1f };
        }
        if (Ai.Ai1 <= 0f)
        {
            Ai = Ai with { Ai0 = 0f, Ai1 = 300 + random.NextInt32(0, 300) - random.NextInt32(0, 100), Ai2 = 0f };
            Local = Local with { Ai3 = 60f };
            Force = true;
        }
        // AI007 observes Friendly before SubAI expires the catchable protection later in
        // this update. The ordinary speed returns on the following update after expiry.
        bool fleeingSpeed = Friendly && (danger.Present || drowning);
        float speed = Family == VanillaGroundCritterFamily1458.Squirrel ? 1.5f : 1f;
        float acceleration = .07f;
        if (Turtle)
        {
            speed = Wet ? Family == VanillaGroundCritterFamily1458.SeaTurtle ? 2.5f : 2f
                : Family == VanillaGroundCritterFamily1458.SeaTurtle ? .2f : .5f;
            acceleration = Wet ? 1f : .07f;
        }
        if (Mouse) { speed = 2f; acceleration = 1f; }
        if (fleeingSpeed) { speed = 1.5f + (1f - (float)Life / LifeMax) * .9f; acceleration = .1f; }
        if (Frog && Wet)
        {
            if (MathF.Abs(Vx) < .05f && MathF.Abs(Vy) < .05f) Vx += speed * 10f * Direction;
            else Vx *= .9f;
        }
        else if (Vx < -speed || Vx > speed)
        {
            if (Vy == 0f) { Vx *= .8f; Vy *= .8f; }
        }
        else if (Direction == 1 && Vx < speed) Vx = Math.Min(speed, Vx + acceleration);
        else if (Direction == -1 && Vx > -speed)
        {
            Vx -= acceleration;
            // NPC.AI_007 retains a positive comparison in this negative-facing branch.
            if (Vx > speed) Vx = speed;
        }
        bool platforms = HomeY * 16 - 32 <= Y;
        if (!platforms && Vy == 0f) Apply(terrain.StepDown(X, Y, Vx, Vy, Width, Height));
        if (Vy >= 0f) Apply(terrain.StepUp(X, Y, Vx, Vy, Width, Height, platforms));
        if (Vy == 0f)
        {
            int aheadX = (int)((X + Width / 2 + 15 * Direction) / 16f);
            int feetY = (int)((Y + Height - 16f) / 16f);
            bool keepWalking = drowning;
            if (X == Local.Ai3) { Direction *= -1; Force = true; Local = Local with { Ai3 = 180f }; }
            if (drowning)
                Local = Local with { Ai3 = Local.Ai3 > 180f ? 179f : Local.Ai3 > 0f ? Local.Ai3 - 1f : Local.Ai3 };
            else Local = Local with { Ai3 = -1f };
            if (Vx < 0f && Direction == -1 || Vx > 0f && Direction == 1)
            {
                GroundCritterTile1458 ahead = terrain.Tile(aheadX, feetY);
                GroundCritterTile1458 above = terrain.Tile(aheadX, feetY - 1);
                GroundCritterTile1458 high = terrain.Tile(aheadX, feetY - 2);
                bool turn = false;
                if (Full(high) && (Height / 16 >= 3 || Full(above)))
                {
                    if (!terrain.NavigationObstacle(aheadX - Direction * 2, aheadX - Direction, feetY - 5, feetY - 1) &&
                        !terrain.NavigationObstacle(aheadX, aheadX, feetY - 5, feetY - 3)) Jump(-6f);
                    else if (Mouse)
                    {
                        if (terrain.Tile((int)((X + Width * .5f) / 16f) + Direction,
                                (int)((Y + Height * .5f) / 16f)).StrictSolid)
                        { Direction *= -1; Vx = 0f; Force = true; }
                    }
                    else turn = true;
                }
                else if (Full(above))
                {
                    if (!terrain.NavigationObstacle(aheadX - Direction * 2, aheadX - Direction, feetY - 4, feetY - 1) &&
                        !terrain.NavigationObstacle(aheadX, aheadX, feetY - 4, feetY - 2)) Jump(-5f);
                    else turn = true;
                }
                else if (Y + Height - feetY * 16 > 20f && ahead.Solid && !ahead.TopSlope)
                {
                    if (!terrain.NavigationObstacle(aheadX - Direction * 2, aheadX, feetY - 3, feetY - 1)) Jump(-4.4f);
                    else turn = true;
                }
                if (turn)
                {
                    if (danger.Present)
                    {
                        Vx = 0f;
                        keepWalking = false;
                        Ai = Ai with { Ai0 = 8f, Ai1 = 240f };
                    }
                    Direction *= -1;
                    Vx *= -1f;
                    Force = true;
                }
                if (keepWalking) { Ai = Ai with { Ai1 = 90f }; Force = true; }
                if (Vy < 0f) Local = Local with { Ai3 = X };
                if (Vy < 0f && Wet) Vy *= 1.2f;
                if (Vy < 0f && !Mouse) Vy *= 1.2f;
            }
        }
        if (OldX == X && OldY == Y && OldVx == Vx && OldVy == Vy + Gravity && !danger.Present)
        {
            float stuck = Local.Ai2;
            Local = Local with { Ai2 = stuck + 1f };
            if (stuck >= 30f)
            {
                Local = Local with { Ai2 = 0f };
                Ai = Ai with { Ai0 = 1f, Ai1 = 200 + random.NextInt32(0, 300) + random.NextInt32(200, 400), Ai2 = 0f };
                Direction *= -1;
                Vx *= -1f;
                Force = true;
            }
        }
    }

    private void Apply(GroundCritterStep1458 step) { X = step.X; Y = step.Y; Vx = step.VelocityX; Vy = step.VelocityY; }
    private void Jump(float velocity) { Vy = velocity; Force = true; }
    private static bool Full(GroundCritterTile1458 tile) => tile.Solid && !tile.SolidTop;
}
