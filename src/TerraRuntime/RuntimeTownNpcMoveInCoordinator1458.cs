using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

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
/// Authoritative bridge from UpdateTime_SpawnTownNPCs into room-aware materialization. Existing homeless residents
/// retain first priority. New residents are selected with the source-shaped IsThereASpawnablePrioritizedTownNPC order:
/// occupants assigned to the tested room first, then eligible types with any TownManager room, then town pets, then
/// the global prioritized type. A selected type gets its own assigned-room attempt before the tested candidate room.
/// </summary>
internal sealed class RuntimeTownNpcMoveInCoordinator1458
{
    internal const int KickOutLookForHomeTimeout1458 = 3600;

    private readonly RuntimeTownNpcStateStore townNpcs;
    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeNpcReplicationRegistry? replication;
    private readonly RuntimeTownHouseCandidateIndex1458 houses;
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

    public void Tick(
        in RuntimeTownNpcMoveInConditions1458 conditions,
        ReadOnlySpan<VanillaTownSpawnPlayerFacts1458> players)
    {
        AdvanceLookForHomeTimeoutsAndObserveKickOuts();
        houses.Scan(HouseScanBudgetPerTick);
        if (!conditions.AllowsMoveIn || !cadence.Advance(conditions.WorldUpdateRate))
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

        if (!townNpcs.TryAddResident(type, in placement, npcs, out NpcSnapshot snapshot, out RuntimeTownNpcHomeCommit home))
            return;

        if (type == VanillaNpcIds.Truffle)
        {
            houses.SetTruffleUnlocked(true);
            progression?.MarkTruffleSpawnUnlocked();
        }

        previousHomeStatuses[home.NpcSlot] = home.Status;
        replication?.TryPublishTownHome(in home);
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
        foreach (RuntimeTownHouseCandidate1458 candidate in houses.CaptureCandidates())
        {
            if (!TrySelectTypeForCandidate(
                    in candidate,
                    eligibility,
                    activeTypes,
                    occupants,
                    out NpcTypeId type))
            {
                continue;
            }

            if (townNpcs.TryGetRoom(type, out WorldTownRoom assigned) &&
                houses.TryValidateAssignedRoom(type, in assigned, occupants, out selectedPlacement))
            {
                selectedType = type;
                return true;
            }

            if (houses.TryValidateCandidate(in candidate, type, occupants, out selectedPlacement))
            {
                selectedType = type;
                return true;
            }
        }

        selectedType = default;
        selectedPlacement = default;
        return false;
    }

    private bool TrySelectTypeForCandidate(
        in RuntimeTownHouseCandidate1458 candidate,
        VanillaTownSpawnEligibility1458 eligibility,
        ReadOnlySpan<NpcTypeId> activeTypes,
        ReadOnlySpan<VanillaHousingOccupant> occupants,
        out NpcTypeId selectedType)
    {
        foreach (NpcTypeId occupantType in townNpcs.CaptureRoomOccupantsInManagerOrder(
                     candidate.HomeTileX,
                     candidate.HomeTileY))
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
        // For Truffle the existing source-shaped validator owns the surface/mushroom gate, so fail closed here.
        return type != VanillaNpcIds.Truffle ||
               houses.TryValidateCandidate(in candidate, type, occupants, out _);
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
