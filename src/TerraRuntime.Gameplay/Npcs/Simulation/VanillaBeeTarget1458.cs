namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaBeeTargetBody1458(
    ushort Target, float X, float Y, int Width, int Height, bool Eligible, int Aggro = 0)
{
    public float CenterX => X + Width * .5f;
    public float CenterY => Y + Height * .5f;
    public bool IsValid => float.IsFinite(X) && float.IsFinite(Y) && Width > 0 && Height > 0;
}

public readonly record struct VanillaBeeTargetSelection1458(
    bool Found, ushort Target, int DirectionX, int DirectionY);

/// <summary>NPCUtils.TargetClosestNonBees search and facing, TerrariaServer 1.4.5.8.</summary>
public static class VanillaBeeTarget1458
{
    public static bool TrySelect(float x, float y, int width, int height, ushort oldTarget,
        int directionX, int directionY, ReadOnlySpan<VanillaBeeTargetBody1458> bodies,
        out VanillaBeeTargetSelection1458 selection)
    {
        selection = default;
        if (!float.IsFinite(x) || !float.IsFinite(y) || width <= 0 || height <= 0 ||
            directionX is < -1 or > 1 || directionY is < -1 or > 1)
            return false;

        float centerX = x + width * .5f;
        float centerY = y + height * .5f;
        float npcDistanceSquared = float.MaxValue;
        float playerDistance = float.MaxValue;
        int nearestNpc = -1;
        int nearestPlayer = -1;
        for (int index = 0; index < bodies.Length; index++)
        {
            var body = bodies[index];
            if (!body.IsValid || body.Target is >= 255 and < 300 || body.Target >= 500)
                return false;
            if (!body.Eligible)
                continue;
            float dx = centerX - body.CenterX;
            float dy = centerY - body.CenterY;
            float squared = dx * dx + dy * dy;
            if (body.Target >= 300)
            {
                if (squared < npcDistanceSquared)
                {
                    npcDistanceSquared = squared;
                    nearestNpc = index;
                }
            }
            else
            {
                float distance = MathF.Sqrt(squared) - body.Aggro;
                if (distance < playerDistance)
                {
                    playerDistance = distance;
                    nearestPlayer = index;
                }
            }
        }

        int selected = nearestNpc >= 0 &&
            (nearestPlayer < 0 || (float)Math.Sqrt(npcDistanceSquared) < playerDistance)
            ? nearestNpc : nearestPlayer;
        if (selected < 0)
        {
            selection = new(false, oldTarget, directionX, directionY);
            return true;
        }
        var target = bodies[selected];
        selection = new(true, target.Target,
            (int)target.X + target.Width / 2 < centerX ? -1 : 1,
            (int)target.Y + target.Height / 2 < centerY ? -1 : 1);
        return true;
    }
}
