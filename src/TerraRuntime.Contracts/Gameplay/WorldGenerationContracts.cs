namespace TerraRuntime.Contracts.Gameplay;

/// <summary>Stable, namespaced identity for one selectable world-generation profile.</summary>
public readonly record struct WorldGeneratorId : IComparable<WorldGeneratorId>
{
    public const int MaxLength = 128;
    private readonly string? value;

    public WorldGeneratorId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, MaxLength);
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
                throw new ArgumentException("World-generator IDs cannot contain whitespace or control characters.", nameof(value));
        }

        this.value = value;
    }

    public string Value => value ?? string.Empty;
    public bool IsAssigned => value is not null;
    public int CompareTo(WorldGeneratorId other) => StringComparer.Ordinal.Compare(Value, other.Value);
    public override string ToString() => Value;
}

public enum WorldGenerationGameMode : byte
{
    Classic = 0,
    Expert = 1,
    Master = 2,
    Journey = 3
}

public enum WorldGenerationEvil : byte
{
    Corruption = 0,
    Crimson = 1
}

/// <summary>
/// Gameplay-visible world options that affect both generation and the persisted vanilla header state. Keeping these
/// on the request prevents a host adapter from silently changing world semantics after custom generation completed.
/// </summary>
public readonly record struct WorldGenerationOptions(
    WorldGenerationGameMode GameMode,
    WorldGenerationEvil Evil)
{
    public static WorldGenerationOptions Default =>
        new(WorldGenerationGameMode.Classic, WorldGenerationEvil.Corruption);

    public void Validate()
    {
        if (!Enum.IsDefined(GameMode))
            throw new ArgumentOutOfRangeException(nameof(GameMode));
        if (!Enum.IsDefined(Evil))
            throw new ArgumentOutOfRangeException(nameof(Evil));
    }
}

/// <summary>Immutable request used to build and execute one isolated candidate world.</summary>
public readonly record struct WorldGenerationRequest(
    WorldGeneratorId GeneratorId,
    string WorldName,
    ulong Seed,
    int WidthTiles,
    int HeightTiles)
{
    public const int MaxWorldNameLength = 128;

    /// <summary>
    /// Supported world options visible to every pass. The default value is Classic + Corruption, preserving the
    /// semantics of existing callers that predate the explicit options surface.
    /// </summary>
    public WorldGenerationOptions Options { get; init; } = WorldGenerationOptions.Default;

    public void Validate()
    {
        if (!GeneratorId.IsAssigned)
            throw new ArgumentException("World-generator ID must be assigned.", nameof(GeneratorId));
        ArgumentException.ThrowIfNullOrWhiteSpace(WorldName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(WorldName.Length, MaxWorldNameLength);
        ArgumentOutOfRangeException.ThrowIfLessThan(WidthTiles, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(HeightTiles, 1);
        Options.Validate();
        _ = checked((long)WidthTiles * HeightTiles);
    }
}

/// <summary>
/// Host-supplied selectable generator. Providers build a deterministic pass plan; they never receive a live world
/// while the plan is being constructed.
/// </summary>
public interface IWorldGenerationProvider
{
    WorldGeneratorId Id { get; }
    void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder);
}

/// <summary>Cold-path plan builder owned by TerraRuntime.</summary>
public interface IWorldGenerationPlanBuilder
{
    void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass);
}

/// <summary>One synchronous world-generation pass executed against an isolated candidate workspace.</summary>
public interface IWorldGenerationPass
{
    void Execute(IWorldGenerationContext context);
}

/// <summary>Runtime-owned execution context for one generation pass.</summary>
public interface IWorldGenerationContext
{
    WorldGenerationRequest Request { get; }
    IWorldGenerationWorkspace Workspace { get; }

    /// <summary>
    /// Optional semantic world metadata surface. TerraRuntime's production candidate workspace supplies it so a
    /// complete custom generator can declare required world anchors without depending on the internal .wld header.
    /// </summary>
    IWorldGenerationMetadataWorkspace? Metadata { get; }

    IWorldGenerationRandom Random { get; }
    CancellationToken CancellationToken { get; }
    void ReportProgress(double fraction, string? message = null);
}

/// <summary>
/// Narrow mutable candidate-world surface. Implementations are isolated from the authoritative live world and are
/// published only after the complete generation job succeeds and higher-level validation accepts the candidate.
/// </summary>
public interface IWorldGenerationWorkspace
{
    int WidthTiles { get; }
    int HeightTiles { get; }

    bool TryGetTile(int x, int y, out WorldGenerationTile tile);
    bool TrySetTile(int x, int y, in WorldGenerationTile tile);
}

/// <summary>
/// Semantic metadata required to turn generated tiles into a complete world. This intentionally exposes gameplay
/// concepts rather than raw Terraria .wld fields so the runtime remains responsible for format-specific defaults,
/// validation and serialization.
/// </summary>
public interface IWorldGenerationMetadataWorkspace
{
    bool TryGetSpawn(out WorldGenerationPoint spawn);
    bool TrySetSpawn(int x, int y);

    bool TryGetDungeon(out WorldGenerationPoint dungeon);
    bool TrySetDungeon(int x, int y);

    bool TryGetLayers(out WorldGenerationLayers layers);
    bool TrySetLayers(double worldSurface, double rockLayer);
}

public readonly record struct WorldGenerationPoint(int X, int Y);

public readonly record struct WorldGenerationLayers(double WorldSurface, double RockLayer);

/// <summary>Deterministic RNG surface supplied independently to each isolated custom pass.</summary>
public interface IWorldGenerationRandom
{
    ulong NextUInt64();
    uint NextUInt32();
    int NextInt32(int exclusiveMax);
}

/// <summary>Optional bounded progress sink supplied by the caller.</summary>
public interface IWorldGenerationProgressSink
{
    void Report(in WorldGenerationProgress progress);
}

public readonly record struct WorldGenerationProgress(
    WorldGenerationPassId PassId,
    int PassIndex,
    int PassCount,
    double Fraction,
    string? Message);

[Flags]
public enum WorldGenerationTileFlags : ushort
{
    None = 0,
    Active = 1 << 0,
    WireRed = 1 << 1,
    WireBlue = 1 << 2,
    WireGreen = 1 << 3,
    WireYellow = 1 << 4,
    Actuator = 1 << 5,
    Inactive = 1 << 6,
    InvisibleBlock = 1 << 7,
    InvisibleWall = 1 << 8,
    FullbrightBlock = 1 << 9,
    FullbrightWall = 1 << 10
}

public enum WorldGenerationLiquidKind : byte
{
    Water = 0,
    Lava = 1,
    Honey = 2,
    Shimmer = 3
}

/// <summary>
/// Normalized generator-facing tile state. Content IDs are validated by the runtime workspace before mutation is
/// accepted, keeping host code independent from TerraRuntime.World implementation types.
/// </summary>
public readonly record struct WorldGenerationTile(
    ushort Type,
    ushort Wall,
    short FrameX,
    short FrameY,
    WorldGenerationTileFlags Flags,
    byte LiquidAmount,
    byte TileColor,
    byte WallColor,
    byte Shape,
    WorldGenerationLiquidKind LiquidKind);