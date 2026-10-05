namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Ordinary Blue/Lava Slime subset of the final source NPC.CheckActive phase.</summary>
public static class VanillaOrdinarySlimeCheckActive1458
{
    public static bool TryStep(float x, float y, int width, int height, int timeLeft,
        ReadOnlySpan<VanillaNpcRawPlayer1458> players,
        out int nextTimeLeft, out bool despawn)
    {
        nextTimeLeft = timeLeft;
        despawn = false;
        if (!float.IsFinite(x) || !float.IsFinite(y) || width <= 0 || height <= 0 || timeLeft < 0)
            return false;

        int activeX = (int)(x + width / 2 - 4032f);
        int activeY = (int)(y + height / 2 - 2520f);
        int resetX = (int)((double)(x + width / 2) - 960d - width);
        int resetY = (int)((double)(y + height / 2) - 600d - height);
        bool inRange = false;
        foreach (var player in players)
        {
            if (!player.Active)
                continue;
            if (!player.IsValid)
                return false;
            int playerX = (int)player.PositionX;
            int playerY = (int)player.PositionY;
            if (Intersects(activeX, activeY, 8064, 5040, playerX, playerY, player.Width, player.Height))
                inRange = true;
            if (Intersects(resetX, resetY, 1920 + width * 2, 1200 + height * 2,
                playerX, playerY, player.Width, player.Height))
                nextTimeLeft = 750;
        }
        nextTimeLeft--;
        despawn = !inRange || nextTimeLeft <= 0;
        if (nextTimeLeft < 0)
            nextTimeLeft = 0;
        return true;
    }

    private static bool Intersects(int x, int y, int width, int height,
        int otherX, int otherY, int otherWidth, int otherHeight) =>
        otherX < x + width && x < otherX + otherWidth &&
        otherY < y + height && y < otherY + otherHeight;
}
