namespace TerraRuntime.Schematics;

/// <summary>Relative tile rectangle captured into a schematic.</summary>
public readonly record struct SchematicBounds(int X, int Y, int Width, int Height)
{
    public void Validate()
    {
        if (Width <= 0 || Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(Width), "Schematic bounds must have positive dimensions.");

        long tileCount = checked((long)Width * Height);
        if (Width > SchematicLimits.MaxWidthTiles || Height > SchematicLimits.MaxHeightTiles || tileCount > SchematicLimits.MaxTileCount)
            throw new ArgumentOutOfRangeException(nameof(Width), "Schematic bounds exceed the supported limits.");
    }
}

/// <summary>Destination tile origin used when restoring a schematic into another world/editor canvas.</summary>
public readonly record struct SchematicPlacement(int X, int Y);

/// <summary>
/// Neutral capture boundary implemented by a runtime/editor. File I/O is intentionally separate so capture can run
/// under the correct world ownership rules and the resulting document can then be saved by <see cref="SchematicFile"/>.
/// </summary>
public interface ISchematicCaptureSource
{
    SchematicDocument Capture(in SchematicBounds bounds);
}

/// <summary>
/// Neutral restore boundary implemented by a runtime/editor. A TerraRuntime implementation must enter authoritative
/// world mutation through its normal command/ownership boundary rather than mutating live state from arbitrary callers.
/// </summary>
public interface ISchematicRestoreTarget
{
    void Restore(SchematicDocument schematic, in SchematicPlacement placement);
}
