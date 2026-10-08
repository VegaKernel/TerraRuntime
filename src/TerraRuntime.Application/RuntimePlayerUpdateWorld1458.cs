namespace TerraRuntime.Application;

/// <summary>Owned source world fields for a remote ordinary Player.Update movement phase.</summary>
internal readonly record struct RuntimePlayerUpdateWorld1458(
    int MaxTilesX, int MaxTilesY, double WorldSurface, bool RemixWorld, bool SkyblockWorld)
{
    internal bool IsValid => MaxTilesX > 0 && MaxTilesY > 0 &&
        MaxTilesX <= int.MaxValue / 16 && MaxTilesY <= int.MaxValue / 16 &&
        double.IsFinite(WorldSurface) && WorldSurface > 0;

    internal float ResolveGravityMultiplier(float positionY)
    {
        float widthScale = MaxTilesX / 4200f;
        widthScale *= widthScale;
        float factor = (float)((positionY / 16f - (60f + 10f * widthScale)) /
            (WorldSurface / (RemixWorld ? 1.0 : 6.0)));
        return Math.Clamp(factor, RemixWorld ? 0.1f : 0.25f, 1f);
    }

    internal bool TryApplyBorders(ref float x, ref float y, ref float vx, ref float vy, bool godMode)
    {
        if (x < 640f) { x = 640f; vx = 0f; }
        float right = MaxTilesX * 16f - 640f - VanillaServerPlayerDryPhysicsStepper.PlayerWidth;
        if (x > right) { x = right; vx = 0f; }
        if (y < 640f)
        {
            // The source Remix top and Skyblock bottom can call KillMe. Their death producer
            // is not part of this ordinary motion proposal; refuse before any owner adopts it.
            if (RemixWorld && y < 640f - VanillaServerPlayerDryPhysicsStepper.PlayerHeight) return false;
            if (!RemixWorld) { y = 640f; vy = Math.Max(vy, 0.11f); }
        }
        float bottom = MaxTilesY * 16f - 640f;
        if (y > bottom)
        {
            if (SkyblockWorld) return false;
            y = bottom; vy = 0f;
        }
        if ((godMode || !SkyblockWorld) && y > bottom - VanillaServerPlayerDryPhysicsStepper.PlayerHeight)
        { y = bottom - VanillaServerPlayerDryPhysicsStepper.PlayerHeight; vy = 0f; }
        return true;
    }
}
