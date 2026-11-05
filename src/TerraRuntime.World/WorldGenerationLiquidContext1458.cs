using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>Borrowed generation state and synchronous callbacks; the caller retains every owner.</summary>
internal readonly record struct WorldGenerationLiquidContext1458(
    int WaterLine,
    Func<TileTypeId, bool> IsSolid,
    Action<TileTypeId, bool> SetSolid,
    Func<int, int, ushort, bool> IsDungeonWater,
    Action RemoveWaterAroundShimmer,
    Action CleanupInteractions,
    Action<int, int> FrameSquare,
    Action<int, int> FrameRetiredPlant,
    Func<int, int, bool>? IsRemixLava = null);
