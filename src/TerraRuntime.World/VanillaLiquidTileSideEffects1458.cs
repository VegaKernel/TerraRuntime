using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// One source-backed <c>Liquid.CreateLiquidMergeTile</c> target. <see cref="TargetBefore"/> is captured before
/// LiquidCheck clears the participating liquid cells. <see cref="ContainerOverride"/> corresponds to the lower-cell
/// container exception in TerrariaServer 1.4.5.8 <c>Liquid.LiquidCheck</c>.
/// </summary>
public readonly record struct VanillaLiquidMergeTileRequest1458(
    int X,
    int Y,
    TileTypeId MergeTileType,
    WorldLiquidKind SourceKind,
    WorldLiquidKind MergeKind,
    WorldTile TargetBefore,
    bool ContainerOverride);

/// <summary>
/// Synchronous game-thread boundary for irreversible LiquidCheck tile side effects. The world simulator owns liquid
/// ordering; the application owner owns drops/NPCs/network side effects. A successful prepare reserves everything
/// required for commit without mutating the tile. Commit is called immediately after vanilla-order liquid clears and
/// must not fail. Returning false from prepare is therefore genuinely fail-closed and leaves all participating liquid
/// cells untouched.
/// </summary>
public interface IVanillaLiquidTileSideEffectSink1458
{
    /// <summary>
    /// Performs the source-backed <c>Main.tileCut</c> lower-cell KillTile side effect atomically. False means no
    /// mutation was committed.
    /// </summary>
    bool TryCutTile(int x, int y);

    /// <summary>
    /// Prepares one merge-tile replacement. Implementations may reserve item slots or other bounded resources, but
    /// must not mutate world tiles. At most one preparation is outstanding per simulator/sink pair.
    /// </summary>
    bool TryPrepareMergeTile(in VanillaLiquidMergeTileRequest1458 request);

    /// <summary>
    /// Commits the previously prepared merge target after the simulator has cleared participating liquids. This method
    /// is allowed to throw on violated internal invariants; an ordinary unsupported target must have returned false
    /// from <see cref="TryPrepareMergeTile"/> instead.
    /// </summary>
    void CommitPreparedMergeTile(in VanillaLiquidMergeTileRequest1458 request);

    /// <summary>Releases a preparation when the simulator aborts before commit.</summary>
    void AbortPreparedMergeTile();
}
