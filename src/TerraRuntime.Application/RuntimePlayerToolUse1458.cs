using TerraRuntime.Gameplay.Players;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Application;

// Local-owner tool clocks and HitTile belong to the existing player item phase.
// Dedicated remote presentation clocks remain independently retained in Selected.
internal readonly record struct RuntimePlayerToolStagePose1458(
    PlayerStateSnapshot Player, int Width, int Height, float GravityDirection);

internal sealed record RuntimePlayerToolUse1458(ToolClockState1458 Clocks,
    ToolHitState1458 Hits, ulong ActionSerial, long LastActionUpdate)
{
    // Written only by the joint source-use/held-birth adoption, never by raw27.
    internal RuntimePlayerToolInput1458? Input { get; init; }
    // Publication ownership moves here if its observation cohort retires.
    internal ToolHealthPublication1458? DeferredHealthPublication { get; init; }
    internal RuntimePlayerToolStagePose1458? SourceStagePose { get; init; }
    internal RuntimeToolBuildingCapture1458? BuildingPermission { get; init; }
    internal RuntimePlayerToolRelease1458? ReleaseWitness { get; init; }
    internal RuntimePlayerToolReach1458? Reach { get; init; }
    internal bool SelectionInitialized { get; init; }
    internal RuntimePlayerWeaponShoot1458? WeaponShoot { get; init; }
    internal FishingItemAction1458? FishingAction { get; init; }
    // Retained local mana caller role; selection changes do not make it remote.
    internal bool LocalManaFeedbackOwned { get; init; }
    internal bool LocalClocksKnown { get; init; } = true;
    internal byte? DeferredSelectionSlot { get; init; }
    internal TerraRuntime.Gameplay.Items.ToolDefinition1458? SourceDefinition { get; init; }
    internal LocalItemClockDefinition1458? SourceLocalDefinition { get; init; }
    internal ulong PlacementHistoryEpoch { get; init; }
    internal RuntimePlayerPlacementReceipt1458? PlacementReceipt { get; init; }
    internal ToolHitState1458 ReplacementHits { get; init; } = ToolHitState1458.Constructor;
    internal RuntimePlayerPlacementAlternative1458? PlacementAlternative { get; init; }
    internal TerraRuntime.Gameplay.Items.LocalPlacementDefinition1458? SourcePlacement { get; init; }
    internal TerraRuntime.Gameplay.Items.WiringToolDefinition1458? SourceWiring { get; init; }
    internal RuntimePlayerWiringReceipt1458? WiringReceipt { get; init; }
    internal float PlacementSpeed { get; init; } = 1f;
    internal RuntimePlayerPlacementReady1458? PlacementReady { get; init; }
    internal bool RequiresFreshUse { get; init; }
    internal ItemTypeId SourceItem { get; init; }
    internal PrefixId SourcePrefix { get; init; }
    internal byte SourceSlot { get; init; }
    internal bool LastUseSuccess { get; init; }
    internal ulong UseEpoch { get; init; }
    internal long LastFrameUpdate { get; init; } = long.MinValue;
    internal bool ShootEligible { get; init; }
    internal bool ToolStagePending { get; init; }
    internal ProjectileHandle? HeldProjectile { get; init; }
    internal int HeldProjectileType { get; init; }
    internal ulong HeldUseEpoch { get; init; }
    internal ItemTypeId HeldItem { get; init; }
    internal PrefixId HeldPrefix { get; init; }
    internal byte HeldSlot { get; init; }

    internal static RuntimePlayerToolUse1458 Constructor => new(
        new(new(0, 0, 0, 0, false, 0, 0), 0, false, false),
        ToolHitState1458.Constructor, 0, long.MinValue);
}
