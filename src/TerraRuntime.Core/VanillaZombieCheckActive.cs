using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public readonly record struct VanillaZombieCheckActiveResult(
    int TimeLeft,
    bool PlayerInActiveRange,
    bool PlayerInResetRange,
    bool ShouldDespawn);

/// <summary>
/// Source-backed ordinary type-3 subset of TerrariaServer 1.4.5.8 NPC.CheckActive.
/// It models the two vanilla player rectangles, activeTime reset, lifetime decrement and final
/// inactivity despawn decision. Boss/town/special NPC exceptions are intentionally outside this type-3 slice.
/// </summary>
public static class VanillaZombieCheckActive
{
    private const int ScreenWidth = 1920;
    private const int ScreenHeight = 1200;
    private const int ActiveRangeX = 4032;
    private const int ActiveRangeY = 2520;
    private const int PlayerWidth = 20;
    private const int PlayerHeight = 42;

    public static bool TryStep(
        float positionX,
        float positionY,
        int width,
        int height,
        int timeLeft,
        ReadOnlySpan<VanillaNpcTargetCandidate> players,
        out VanillaZombieCheckActiveResult result)
    {
        if (!float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            width <= 0 ||
            height <= 0 ||
            timeLeft < 0)
        {
            result = default;
            return false;
        }

        int centerX = (int)(positionX + width * 0.5f);
        int centerY = (int)(positionY + height * 0.5f);
        var activeRange = new IntRect(
            centerX - ActiveRangeX,
            centerY - ActiveRangeY,
            ActiveRangeX * 2,
            ActiveRangeY * 2);
        var resetRange = new IntRect(
            (int)((double)(positionX + width * 0.5f) - ScreenWidth * 0.5d - width),
            (int)((double)(positionY + height * 0.5f) - ScreenHeight * 0.5d - height),
            ScreenWidth + width * 2,
            ScreenHeight + height * 2);

        bool playerInActiveRange = false;
        bool playerInResetRange = false;
        int nextTimeLeft = timeLeft;

        foreach (VanillaNpcTargetCandidate player in players)
        {
            if (!player.Active ||
                !float.IsFinite(player.CenterX) ||
                !float.IsFinite(player.CenterY))
            {
                continue;
            }

            var hitbox = new IntRect(
                (int)(player.CenterX - PlayerWidth * 0.5f),
                (int)(player.CenterY - PlayerHeight * 0.5f),
                PlayerWidth,
                PlayerHeight);

            if (activeRange.Intersects(hitbox))
                playerInActiveRange = true;

            if (resetRange.Intersects(hitbox))
            {
                nextTimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft;
                playerInResetRange = true;
            }
        }

        nextTimeLeft--;
        bool shouldDespawn = nextTimeLeft <= 0 || !playerInActiveRange;
        if (nextTimeLeft < 0)
            nextTimeLeft = 0;

        result = new VanillaZombieCheckActiveResult(
            nextTimeLeft,
            playerInActiveRange,
            playerInResetRange,
            shouldDespawn);
        return true;
    }

    private readonly record struct IntRect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;

        public bool Intersects(IntRect other) =>
            other.X < Right &&
            X < other.Right &&
            other.Y < Bottom &&
            Y < other.Bottom;
    }
}
