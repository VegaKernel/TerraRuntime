namespace TerraRuntime.WorldGeneration.Runtime;

internal enum VanillaLandmassKind1458 { Round, SkyblockIsland, ExtraLiquidBubbleSquare }
internal readonly record struct VanillaLandmass1458(VanillaLandmassKind1458 Kind, float X, float Y, int RadiusOrHalfSize, int Style, int ExtraData);
public sealed partial class Workspace
{
    private readonly List<VanillaLandmass1458> vanillaLandmasses = [];
    internal bool VanillaSpawnHasBeenRandomized { get; private set; }
    internal VanillaLandmass1458[] CaptureVanillaLandmasses() => vanillaLandmasses.ToArray();
    internal void ResetVanillaLandmasses()
    { vanillaLandmasses.Clear(); VanillaSpawnHasBeenRandomized = false; }
    internal void AddVanillaLandmass(VanillaLandmass1458 value)
    {
        if (!Enum.IsDefined(value.Kind) || !float.IsFinite(value.X) || !float.IsFinite(value.Y) || value.RadiusOrHalfSize < 0) throw new ArgumentOutOfRangeException(nameof(value));
        vanillaLandmasses.Add(value);
    }
    internal void MarkVanillaSpawnRandomized() => VanillaSpawnHasBeenRandomized = true;
}
