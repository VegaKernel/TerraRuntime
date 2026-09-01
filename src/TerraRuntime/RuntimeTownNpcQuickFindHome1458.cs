using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

internal enum RuntimeTownNpcQuickFindHomeResult1458 : byte
{
    NotApplicable = 0,
    Unchanged = 1,
    Reassigned = 2,
    BecameHomeless = 3
}

/// <summary>
/// Server-owned TerrariaServer 1.4.5.8 WorldGen.QuickFindHome vertical. The search preserves the source seed order:
/// current home-1, then the 3x3 neighborhood, then the +/-10 even-step fallback. Search continues only while
/// StartRoomCheck itself fails. Once a geometrically accepted room is found, furniture/special/score failure makes the
/// resident homeless instead of silently trying a different room. Tile 379 receives vanilla's temporary solid status
/// through the validator's QuickFind mode without mutating global collision state.
/// </summary>
internal sealed class RuntimeTownNpcQuickFindHome1458
{
    private const int OldManType = 37;
    private const int TravelingMerchantType = 368;
    private const int SkeletonMerchantType = 453;
    private const int FallbackRadius = 10;

    private readonly RuntimeTownNpcStateStore townNpcs;
    private readonly RuntimeNpcStore npcs;
    private readonly VanillaHousingValidator1458 validator;
    private readonly WorldTileStore tiles;

    public RuntimeTownNpcQuickFindHome1458(
        RuntimeTownNpcStateStore townNpcs,
        RuntimeNpcStore npcs,
        VanillaHousingValidator1458 validator,
        WorldTileStore tiles)
    {
        this.townNpcs = townNpcs ?? throw new ArgumentNullException(nameof(townNpcs));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
    }

    public RuntimeTownNpcQuickFindHomeResult1458 Refresh(
        short slot,
        out RuntimeTownNpcHomeCommit commit)
    {
        commit = default;
        if (!townNpcs.TryGet(slot, out WorldTownNpc npc) ||
            npc.Homeless ||
            !NpcTypeId.TryCreate(npc.NetId, out NpcTypeId type) ||
            !VanillaTownNpcFacts1458.IsHousingEligible(type) ||
            type.Value is OldManType or TravelingMerchantType or SkeletonMerchantType ||
            (uint)slot > byte.MaxValue ||
            !npcs.TryGetActive(checked((byte)slot), out NpcSnapshot active) ||
            active.Type != type.Value)
        {
            return RuntimeTownNpcQuickFindHomeResult1458.NotApplicable;
        }

        WorldDimensions dimensions = tiles.Dimensions;
        // Exact outer guard from QuickFindHome. A corrupt/out-of-range persisted home is left untouched here just as
        // vanilla does; the normal persistence/recovery layer owns malformed state rather than housing inventing it.
        if (npc.HomeTileX <= 10 ||
            npc.HomeTileY <= 10 ||
            npc.HomeTileX >= dimensions.WidthTiles - 10 ||
            npc.HomeTileY >= dimensions.HeightTiles)
        {
            return RuntimeTownNpcQuickFindHomeResult1458.NotApplicable;
        }

        VanillaHousingOccupant[] occupants = townNpcs.CaptureHousingOccupants(slot);
        bool acceptedSpread = TrySeed(
            npc.HomeTileX,
            npc.HomeTileY - 1,
            type,
            occupants,
            out VanillaHousingPlacement placement);

        if (!acceptedSpread)
        {
            for (int x = npc.HomeTileX - 1; x < npc.HomeTileX + 2 && !acceptedSpread; x++)
            {
                for (int y = npc.HomeTileY - 1; y < npc.HomeTileY + 2 && !acceptedSpread; y++)
                    acceptedSpread = TrySeed(x, y, type, occupants, out placement);
            }
        }

        if (!acceptedSpread)
        {
            for (int x = npc.HomeTileX - FallbackRadius;
                 x <= npc.HomeTileX + FallbackRadius && !acceptedSpread;
                 x += 2)
            {
                for (int y = npc.HomeTileY - FallbackRadius;
                     y <= npc.HomeTileY + FallbackRadius && !acceptedSpread;
                     y += 2)
                {
                    acceptedSpread = TrySeed(x, y, type, occupants, out placement);
                }
            }
        }

        if (!acceptedSpread || !placement.IsValid)
        {
            return townNpcs.TryMarkQuickFindHomeless(slot, out commit)
                ? RuntimeTownNpcQuickFindHomeResult1458.BecameHomeless
                : RuntimeTownNpcQuickFindHomeResult1458.NotApplicable;
        }

        bool changed = npc.HomeTileX != placement.HomeTileX ||
                       npc.HomeTileY != placement.HomeTileY ||
                       npc.HomelessDespawn;
        if (!townNpcs.TryApplyQuickFindHome(slot, in placement, out commit))
            return RuntimeTownNpcQuickFindHomeResult1458.NotApplicable;

        return changed
            ? RuntimeTownNpcQuickFindHomeResult1458.Reassigned
            : RuntimeTownNpcQuickFindHomeResult1458.Unchanged;
    }

    private bool TrySeed(
        int x,
        int y,
        NpcTypeId type,
        ReadOnlySpan<VanillaHousingOccupant> occupants,
        out VanillaHousingPlacement placement)
    {
        placement = validator.ValidateQuickFindHome(x, y, type, occupants);
        return !VanillaHousingValidator1458.IsStartRoomCheckFailure(placement.Result);
    }
}
