using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct RuntimeTownNpcMoveInConditions1458(
    bool DayTime,
    bool Eclipse,
    bool InvasionActive,
    int WorldUpdateRate)
{
    public bool AllowsMoveIn => DayTime && !Eclipse && !InvasionActive && WorldUpdateRate > 0;
}

internal readonly record struct RuntimeTownNpcArrival1458(
    short NpcSlot,
    NpcTypeId NpcType,
    int HomeTileX,
    int HomeTileY);

internal interface IRuntimeTownNpcArrivalSink1458
{
    void TownNpcArrived(in RuntimeTownNpcArrival1458 arrival);
}

/// <summary>
/// Authoritative bridge from UpdateTime_SpawnTownNPCs into room-aware materialization. Existing housed residents are
/// first revalidated through source-shaped QuickFindHome on the same 7200/worldUpdateRate cadence. Existing homeless
/// residents then retain first move-in priority. New residents preserve the source SpawnTownNPC pre-materialization
/// shape: the tested room is scored for the global prioritized type, IsThereASpawnablePrioritizedTownNPC selects
/// against that room's scored home, and a selected type with a TownManager room gets one guarded recursive attempt
/// that can itself select a different resident. A failed alternate-room attempt falls back to the original tested room.
/// </summary>
internal sealed class RuntimeTownNpcMoveInCoordinator1458
{
    internal const int KickOutLookForHomeTimeout1458 = 3600;

    private readonly RuntimeTownNpcStateStore townNpcs;
    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeNpcReplicationRegistry? replication;
    private readonly RuntimeTownHouseCandidateIndex1458 houses;
    private readonly RuntimeTownNpcQuickFindHome1458 quickFindHome;
    private readonly VanillaTownNpcSpawnCadence1458 cadence = new();
    private readonly VanillaTownSpawnWorldFacts1458 worldFacts;
    private readonly IRuntimeTownNpcArrivalSink1458? arrivals;
    private readonly RuntimeWorldProgressionMutations? progression;
    private readonly Dictionary<short, int> lookForHomeTimeouts = [];
    private readonly Dictionary<short, TerrariaNpcHomeStatus> previousHomeStatuses = [];

    public RuntimeTownNpcMoveInCoordinator1458(
        RuntimeTownNpcStateStore townNpcs,
        RuntimeNpcStore npcs,
        RuntimeTownHouseCandidateIndex1458 houses,
        in VanillaTownSpawnWorldFacts1458 worldFacts,
        RuntimeNpcReplicationRegistry? replication = null,
        IRuntimeTownNpcArrivalSink1458? arrivals = null,
        RuntimeWorldProgressionMutations? progression = null)
    {
        ArgumentNullException.ThrowIfNull(townNpcs);
        ArgumentNullException.ThrowIfNull(npcs);
        ArgumentNullException.ThrowIfNull(houses);
        if (!worldFacts.IsValid)
            throw new ArgumentOutOfRangeException(nameof(worldFacts));
        this.townNpcs = townNpcs;
        this.npcs = npcs;
        this.houses = houses;
        houses.SetTruffleUnlocked(worldFacts.UnlockedTruffleSpawn || townNpcs.ContainsNpcType(VanillaNpcIds.Truffle));
        quickFindHome = new RuntimeTownNpcQuickFindHome1458(townNpcs, npcs, houses.Validator, houses.Tiles);
        this.worldFacts = worldFacts;
        this.replication = replication;
        this.arrivals = arrivals;
        this.progression = progression;
        progression?.SetTruffleSpawnBaseline(worldFacts.UnlockedTruffleSpawn);
        CaptureInitialHomeStatuses();
    }

    public int HouseScanBudgetPerTick { get; init; } = 4096;

    public long SuccessfulMoveIns { get; private set; }

    public long SuccessfulRelocations { get; private set; }

    public long SuccessfulHomeRevalidations { get; private set; }

    public long InvalidatedHomes { get; private set; }

    public void Tick(
        in RuntimeTownNpcMoveInConditions1458 conditions,
        ReadOnlySpan<VanillaTownSpawnPlayerFacts1458> players) =>
        Tick(in conditions, players, default);

    public void Tick(
        in RuntimeTownNpcMoveInConditions1458 conditions,
        ReadOnlySpan<VanillaTownSpawnPlayerFacts1458> players,
        ReadOnlySpan<RuntimeTownPlayerBounds1458> playerBounds)
    {
        AdvanceLookForHomeTimeoutsAndObserveKickOuts();
        houses.Scan(HouseScanBudgetPerTick);

        // Main.UpdateTime_SpawnTownNPCs advances checkForSpawns whenever worldUpdateRate > 0. QuickFindHome runs on
        // that cadence before daytime/invasion spawn eligibility is consumed, so existing homes are revalidated at
        // night and during blocked move-in windows too.
        if (!cadence.Advance(conditions.WorldUpdateRate))
            return;

        RefreshExistingHomes();
        if (!conditions.AllowsMoveIn)
            return;

        VanillaHousingOccupant[] occupants = townNpcs.CaptureHousingOccupants();

        if (TryGetRelocatableHomelessResident(out RuntimeTownNpcHomeCommit homeless))
        {
            if (TryRelocateHomelessResident(in homeless, occupants, out RuntimeTownNpcHomeCommit relocated))
            {
                lookForHomeTimeouts.Remove(homeless.NpcSlot);
                previousHomeStatuses[homeless.NpcSlot] = relocated.Status;
                replication?.TryPublishTownHome(in relocated);
                SuccessfulRelocations++;
            }
            return;
        }

        NpcTypeId[] activeTypes = townNpcs.CaptureActiveTownTypes();
        VanillaTownSpawnEligibility1458 eligibility = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in worldFacts,
            players,
            activeTypes);
        if (eligibility.EligibleTypes.Length == 0 ||
            !TrySelectNewResident(
                eligibility,
                activeTypes,
                occupants,
                out NpcTypeId type,
                out VanillaHousingPlacement placement))
        {
            return;
        }

        RuntimeTownNpcPhysicalSpawn1458 physicalSpawn =
            new RuntimeTownNpcPhysicalSpawnResolver1458(houses.Tiles).Resolve(
                placement.HomeTileX,
                placement.HomeTileY,
                playerBounds);
        if (!townNpcs.TryAddResident(
                type,
                in placement,
                in physicalSpawn,
                npcs,
                out NpcSnapshot snapshot,
                out RuntimeTownNpcHomeCommit home))
        {
            return;
        }
        if (!townNpcs.TryGetIdentity(home.NpcSlot, out RuntimeTownNpcIdentityCommit identity))
            throw new InvalidOperationException("Committed Town NPC move-in did not expose its authoritative identity.");

        if (type == VanillaNpcIds.Truffle)
        {
            houses.SetTruffleUnlocked(true);
            progression?.MarkTruffleSpawnUnlocked();
        }

        previousHomeStatuses[home.NpcSlot] = home.Status;
        replication?.TryPublishTownIdentity(in identity);
        replication?.TryPublishTownHome(in home);
        replication?.TryPublishTownArrival(type, identity.GivenName);
        var arrival = new RuntimeTownNpcArrival1458(
            checked((short)snapshot.Handle.Slot),
            type,
            placement.HomeTileX,
            placement.HomeTileY);
        arrivals?.TownNpcArrived(in arrival);
        SuccessfulMoveIns++;
    }

    internal int GetLookForHomeTimeout(short slot) =>
        lookForHomeTimeouts.TryGetValue(slot, out int timeout) ? timeout : 0;

    internal bool TrySelectNewResident(
        VanillaTownSpawnEligibility1458 eligibility,
        ReadOnlySpan<NpcTypeId> activeTypes,
        ReadOnlySpan<VanillaHousingOccupant> occupants,
        out NpcTypeId selectedType,
        out VanillaHousingPlacement selectedPlacement)
    {
        ArgumentNullException.ThrowIfNull(eligibility);
        if (!eligibility.PrioritizedType.IsAssigned)
        {
            selectedType = default;
            selectedPlacement = default;
            return false;
        }

        foreach (RuntimeTownHouseCandidate1458 candidate in houses.CaptureCandidates())
        {
            if (TryResolveSpawnCandidate(
                    in candidate,
                    allowAlternateHousingSpot: true,
                    eligibility,
                    activeTypes,
                    occupants,
                    out selectedType,
                    out selectedPlacement))
            {
                return true;
            }
        }

        selectedType = default;
        selectedPlacement = default;
        return false;
    }

    private void RefreshExistingHomes()
    {
        Span<RuntimeTownNpcHomeCommit> homes = stackalloc RuntimeTownNpcHomeCommit[RuntimeTownNpcStateStore.MaximumTownNpcs];
        int count = townNpcs.CopyHomeBaselines(homes);
        for (int i = 0; i < count; i++)
        {
            RuntimeTownNpcHomeCommit current = homes[i];
            if (current.Status == TerrariaNpcHomeStatus.Homeless)
                continue;

            RuntimeTownNpcQuickFindHomeResult1458 result = quickFindHome.Refresh(
                current.NpcSlot,
                out RuntimeTownNpcHomeCommit commit);
            if (result is not RuntimeTownNpcQuickFindHomeResult1458.Reassigned and
                not RuntimeTownNpcQuickFindHomeResult1458.BecameHomeless)
            {
                continue;
            }

            // QuickFindHome homelessness is not a manual kickout and must not arm the 3600-tick timeout on the next
            // observation pass. Update the shadow immediately so AdvanceLookForHomeTimeouts sees the source transition.
            previousHomeStatuses[current.NpcSlot] = commit.Status;
            replication?.TryPublishTownHome(in commit);
            if (result == RuntimeTownNpcQuickFindHomeResult1458.Reassigned)
                SuccessfulHomeRevalidations++;
            else
                InvalidatedHomes++;
        }
    }

    private bool TryResolveSpawnCandidate(
        in RuntimeTownHouseCandidate1458 candidate,
        bool allowAlternateHousingSpot,
        VanillaTownSpawnEligibility1458 eligibility,
        ReadOnlySpan<NpcTypeId> activeTypes,
        ReadOnlySpan<VanillaHousingOccupant> occupants,
        out NpcTypeId selectedType,
        out VanillaHousingPlacement selectedPlacement)
    {
        // SpawnTownNPC calls ScoreRoom(-1, prioritizedTownNPCType) before IsThereASpawnablePrioritizedTownNPC.
        // The room therefore succeeds or fails against the global prioritized housing category, not the type that
        // IsThereASpawnablePrioritizedTownNPC may subsequently choose for this room.
        if (!houses.TryValidateCandidate(
                in candidate,
                eligibility.PrioritizedType,
                occupants,
                out VanillaHousingPlacement testedPlacement))
        {
            selectedType = default;
            selectedPlacement = default;
            return false;
        }

        if (!TrySelectTypeForCandidate(
                in candidate,
                in testedPlacement,
                eligibility,
                activeTypes,
                occupants,
                out NpcTypeId type))
        {
            selectedType = default;
            selectedPlacement = default;
            return false;
        }

        // Vanilla recursively calls SpawnTownNPC at the selected type's TownManager seed with a one-level guard.
        // The recursive call reruns room scoring and resident selection, so it may successfully materialize a type
        // other than the one that caused the alternate-room attempt. Only a Successful result short-circuits outer
        // fallback; any blocked/house-only result returns to the original room.
        if (allowAlternateHousingSpot && townNpcs.TryGetRoom(type, out WorldTownRoom assigned))
        {
            int seedY = assigned.Y - 2;
            if ((uint)assigned.X < (uint)houses.Tiles.Dimensions.WidthTiles &&
                (uint)seedY < (uint)houses.Tiles.Dimensions.HeightTiles)
            {
                var assignedCandidate = new RuntimeTownHouseCandidate1458(
                    assigned.X,
                    seedY,
                    assigned.X,
                    assigned.Y);
                if (TryResolveSpawnCandidate(
                        in assignedCandidate,
                        allowAlternateHousingSpot: false,
                        eligibility,
                        activeTypes,
                        occupants,
                        out selectedType,
                        out selectedPlacement))
                {
                    return true;
                }
            }
        }

        // The final SpawnTownNPC guard intentionally asks compatibility for prioritizedTownNPCType, even if the
        // actual selected spawn type differs. Preserve that quirk rather than "fixing" it into selected-type logic.
        if (IsRoomConsideredAlreadyOccupied(
                testedPlacement.HomeTileX,
                testedPlacement.HomeTileY,
                eligibility.PrioritizedType,
                occupants))
        {
            selectedType = default;
            selectedPlacement = default;
            return false;
        }

        selectedType = type;
        selectedPlacement = testedPlacement;
        return true;
    }

    private bool TrySelectTypeForCandidate(
        in RuntimeTownHouseCandidate1458 candidate,
        in VanillaHousingPlacement testedPlacement,
        VanillaTownSpawnEligibility1458 eligibility,
        ReadOnlySpan<NpcTypeId> activeTypes,
        ReadOnlySpan<VanillaHousingOccupant> occupants,
        out NpcTypeId selectedType)
    {
        // AddOccupantsToList receives the room's scored bestX/bestY, not the discovery index's cached home anchor.
        foreach (NpcTypeId occupantType in townNpcs.CaptureRoomOccupantsInManagerOrder(
                     testedPlacement.HomeTileX,
                     testedPlacement.HomeTileY))
        {
            if (CanSpawnIntoCandidate(occupantType, in candidate, eligibility, activeTypes, occupants))
            {
                selectedType = occupantType;
                return true;
            }
        }

        NpcTypeId prioritizedFallback = default;
        NpcTypeId[] eligibleById = eligibility.EligibleTypes.OrderBy(static type => type.Value).ToArray();
        foreach (NpcTypeId type in eligibleById)
        {
            if (!CanSpawnIntoCandidate(type, in candidate, eligibility, activeTypes, occupants))
                continue;

            if (townNpcs.TryGetRoom(type, out _))
            {
                selectedType = type;
                return true;
            }

            if (IsTownPet(type))
            {
                selectedType = type;
                return true;
            }

            if (type == eligibility.PrioritizedType)
                prioritizedFallback = type;
        }

        selectedType = prioritizedFallback;
        return selectedType.IsAssigned;
    }

    private bool CanSpawnIntoCandidate(
        NpcTypeId type,
        in RuntimeTownHouseCandidate1458 candidate,
        VanillaTownSpawnEligibility1458 eligibility,
        ReadOnlySpan<NpcTypeId> activeTypes,
        ReadOnlySpan<VanillaHousingOccupant> occupants)
    {
        if (!eligibility.CanSpawn(type) || Contains(activeTypes, type))
            return false;

        // CheckSpecialTownNPCSpawningConditions is unconditional for every supported type except Truffle.
        // Truffle shares the ordinary town housing category, so revalidation adds its mushroom/surface predicate while
        // preserving the same room geometry/scoring category already used by the prioritized baseline.
        return type != VanillaNpcIds.Truffle ||
               houses.TryValidateCandidate(in candidate, type, occupants, out _);
    }

    internal static bool IsRoomConsideredAlreadyOccupied(
        int spawnTileX,
        int spawnTileY,
        NpcTypeId prioritizedType,
        ReadOnlySpan<VanillaHousingOccupant> occupants)
    {
        foreach (VanillaHousingOccupant occupant in occupants)
        {
            if (occupant.HomeTileX == spawnTileX &&
                occupant.HomeTileY == spawnTileY &&
                !VanillaTownNpcFacts1458.CanShareRoom(prioritizedType, occupant.Type))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsTownPet(NpcTypeId type) =>
        type == VanillaNpcIds.TownCat ||
        type == VanillaNpcIds.TownDog ||
        type == VanillaNpcIds.TownBunny ||
        type == VanillaNpcIds.TownSlimeBlue ||
        type == VanillaNpcIds.TownSlimeGreen ||
        type == VanillaNpcIds.TownSlimeOld ||
        type == VanillaNpcIds.TownSlimePurple ||
        type == VanillaNpcIds.TownSlimeRainbow ||
        type == VanillaNpcIds.TownSlimeRed ||
        type == VanillaNpcIds.TownSlimeYellow ||
        type == VanillaNpcIds.TownSlimeCopper;

    private static bool Contains(ReadOnlySpan<NpcTypeId> values, NpcTypeId type)
    {
        foreach (NpcTypeId value in values)
        {
            if (value == type)
                return true;
        }
        return false;
    }

    private void CaptureInitialHomeStatuses()
    {
        Span<RuntimeTownNpcHomeCommit> homes = stackalloc RuntimeTownNpcHomeCommit[RuntimeTownNpcStateStore.MaximumTownNpcs];
        int count = townNpcs.CopyHomeBaselines(homes);
        for (int i = 0; i < count; i++)
            previousHomeStatuses[homes[i].NpcSlot] = homes[i].Status;
    }

    private void AdvanceLookForHomeTimeoutsAndObserveKickOuts()
    {
        Span<short> timeoutSlots = stackalloc short[RuntimeTownNpcStateStore.MaximumTownNpcs];
        int timeoutCount = 0;
        foreach (short slot in lookForHomeTimeouts.Keys)
            timeoutSlots[timeoutCount++] = slot;
        for (int i = 0; i < timeoutCount; i++)
        {
            short slot = timeoutSlots[i];
            int remaining = lookForHomeTimeouts[slot] - 1;
            if (remaining <= 0)
                lookForHomeTimeouts.Remove(slot);
            else
                lookForHomeTimeouts[slot] = remaining;
        }

        Span<RuntimeTownNpcHomeCommit> homes = stackalloc RuntimeTownNpcHomeCommit[RuntimeTownNpcStateStore.MaximumTownNpcs];
        int homeCount = townNpcs.CopyHomeBaselines(homes);
        Span<bool> seen = stackalloc bool[RuntimeTownNpcStateStore.MaximumTownNpcs];
        for (int i = 0; i < homeCount; i++)
        {
            RuntimeTownNpcHomeCommit home = homes[i];
            if ((uint)home.NpcSlot < (uint)seen.Length)
                seen[home.NpcSlot] = true;

            if (previousHomeStatuses.TryGetValue(home.NpcSlot, out TerrariaNpcHomeStatus previous) &&
                previous != TerrariaNpcHomeStatus.Homeless &&
                home.Status == TerrariaNpcHomeStatus.Homeless)
            {
                lookForHomeTimeouts[home.NpcSlot] = KickOutLookForHomeTimeout1458;
            }
            else if (home.Status != TerrariaNpcHomeStatus.Homeless)
            {
                lookForHomeTimeouts.Remove(home.NpcSlot);
            }

            previousHomeStatuses[home.NpcSlot] = home.Status;
        }

        Span<short> staleSlots = stackalloc short[RuntimeTownNpcStateStore.MaximumTownNpcs];
        int staleCount = 0;
        foreach (short slot in previousHomeStatuses.Keys)
        {
            if ((uint)slot >= (uint)seen.Length || !seen[slot])
                staleSlots[staleCount++] = slot;
        }
        for (int i = 0; i < staleCount; i++)
        {
            short slot = staleSlots[i];
            previousHomeStatuses.Remove(slot);
            lookForHomeTimeouts.Remove(slot);
        }
    }

    private bool TryGetRelocatableHomelessResident(out RuntimeTownNpcHomeCommit homeless)
    {
        Span<RuntimeTownNpcHomeCommit> homes = stackalloc RuntimeTownNpcHomeCommit[RuntimeTownNpcStateStore.MaximumTownNpcs];
        int count = townNpcs.CopyHomeBaselines(homes);
        for (int i = 0; i < count; i++)
        {
            RuntimeTownNpcHomeCommit current = homes[i];
            if (current.Status != TerrariaNpcHomeStatus.Homeless ||
                current.NpcType == VanillaNpcIds.Truffle ||
                !VanillaTownNpcFacts1458.IsHousingEligible(current.NpcType) ||
                GetLookForHomeTimeout(current.NpcSlot) != 0)
            {
                continue;
            }

            homeless = current;
            return true;
        }

        homeless = default;
        return false;
    }

    private bool TryRelocateHomelessResident(
        in RuntimeTownNpcHomeCommit homeless,
        ReadOnlySpan<VanillaHousingOccupant> occupants,
        out RuntimeTownNpcHomeCommit relocated)
    {
        if (townNpcs.TryGetRoom(homeless.NpcType, out WorldTownRoom assigned) &&
            houses.TryValidateAssignedRoom(homeless.NpcType, in assigned, occupants, out _) &&
            townNpcs.TryAssignRoom(
                homeless.NpcSlot,
                assigned.X,
                assigned.Y - 2,
                houses.Validator,
                out relocated,
                out _))
        {
            return true;
        }

        if (houses.TryFindRoom(
                homeless.NpcType,
                occupants,
                out RuntimeTownHouseCandidate1458 candidate,
                out _) &&
            townNpcs.TryAssignRoom(
                homeless.NpcSlot,
                candidate.SeedTileX,
                candidate.SeedTileY,
                houses.Validator,
                out relocated,
                out _))
        {
            return true;
        }

        relocated = default;
        return false;
    }
}
