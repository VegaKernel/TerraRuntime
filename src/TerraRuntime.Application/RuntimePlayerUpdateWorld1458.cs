using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>Owned source environment for an ordinary remote Player.Update phase.</summary>
internal readonly record struct RuntimePlayerUpdateWorld1458(
    int MaxTilesX, int MaxTilesY, double WorldSurface, bool RemixWorld, bool SkyblockWorld)
{
    // Official Windows CLR4/x86 retains the Item.Prefix product at greater precision
    // than the Linux/CoreCLR reference. Bind this arithmetic once with the environment.
    internal bool WindowsItemPrefixArithmetic { get; init; }

    // Unbound standalone components keep these unknown. Canonical composition
    // supplies the exact raw flags; neither difficulty nor client buffs infer them.
    internal bool? GetGoodWorld { get; init; }
    internal bool? InfectedSeed { get; init; }
    internal bool? NotTheBeesWorld { get; init; }
    internal bool? NoTrapsWorld { get; init; }
    internal bool? TenthAnniversaryWorld { get; init; }
    internal bool? DontStarveWorld { get; init; }
    internal bool? DrunkWorld { get; init; }
    internal bool? JourneyMode { get; init; }
    internal bool? OnlyShimmerOceanWorlds { get; init; }
    internal int? HallowBackground { get; init; }

    internal static RuntimePlayerUpdateWorld1458 FromMetadata(WorldDimensions dimensions,
        WorldFileRuntimeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(metadata);
        return new(dimensions.WidthTiles, dimensions.HeightTiles, metadata.WorldSurface,
            metadata.RemixWorld, metadata.SkyblockWorld)
        {
            GetGoodWorld = metadata.GetGoodWorld,
            InfectedSeed = metadata.InfectedSeed,
            NotTheBeesWorld = metadata.NotTheBeesWorld,
            NoTrapsWorld = metadata.NoTrapsWorld,
            TenthAnniversaryWorld = metadata.TenthAnniversaryWorld,
            DontStarveWorld = metadata.DontStarveWorld,
            DrunkWorld = metadata.DrunkWorld,
            JourneyMode = metadata.GameMode == 3,
            // Main.onlyShimmerOceanWorlds, Terraria1458: derived from raw seed flags.
            OnlyShimmerOceanWorlds = metadata.DrunkWorld && metadata.TenthAnniversaryWorld &&
                !metadata.RemixWorld && !metadata.ZenithWorld && !metadata.NotTheBeesWorld,
            HallowBackground = metadata.HallowBackground
        };
    }

    internal bool TryCaptureToolDamageWorld(out ToolDamageWorld1458 world)
    {
        world = default;
        if (!IsValid || GetGoodWorld is not { } good || InfectedSeed is not { } infected ||
            NotTheBeesWorld is not { } bees || NoTrapsWorld is not { } traps ||
            TenthAnniversaryWorld is not { } tenth) return false;
        // Main.UnderworldLayer (1458) derives directly from the world height.
        world = new(MaxTilesX, MaxTilesY, WorldSurface, MaxTilesY - 200,
            good, infected, bees, traps, tenth);
        return true;
    }

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
