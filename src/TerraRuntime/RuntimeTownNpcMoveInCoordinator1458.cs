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
/// Authoritative bridge from the source-backed UpdateTime_SpawnTownNPCs candidate pass to room-aware runtime
/// materialization. Existing homeless residents have the same priority as WorldGen.UpdatePrioritizedTownNPC:
/// they are relocated before a new resident may materialize. TownManager-assigned rooms get the pinned first attempt,
/// then the bounded discovered-room index is used as fallback. Physical off-screen spawn fallback and localized
/// arrival text remain separate work.
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
        if (eligibility.EligibleTypes.Length == 0)
            return;

        foreach (NpcTypeId type in eligibility.EligibleTypes)
        {
            if (townNpcs.ContainsNpcType(type) ||
                !TryFindRoomForNewResident(type, occupants, out VanillaHousingPlacement placement))
            {
                continue;
            }

            if (!townNpcs.TryAddResident(type, in placement, npcs, out NpcSnapshot snapshot, out RuntimeTownNpcHomeCommit home))
                continue;

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
            return;
        }
    }

    internal int GetLookForHomeTimeout(short slot) =>
        lookForHomeTimeouts.TryGetValue(slot, out int timeout) ? timeout : 0;

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
        if (TryGetAssignedRoom(homeless.NpcType, out WorldTownRoom assigned) &&
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

    private bool TryFindRoomForNewResident(
        NpcTypeId type,
        ReadOnlySpan<VanillaHousingOccupant> occupants,
        out VanillaHousingPlacement placement)
    {
        if (TryGetAssignedRoom(type, out WorldTownRoom assigned) &&
            houses.TryValidateAssignedRoom(type, in assigned, occupants, out placement))
        {
            return true;
        }

        return houses.TryFindRoom(type, occupants, out placement);
    }

    private bool TryGetAssignedRoom(NpcTypeId type, out WorldTownRoom room)
    {
        foreach (WorldTownRoom candidate in townNpcs.CaptureTownRooms())
        {
            if (candidate.NpcType == type.Value)
            {
                room = candidate;
                return true;
            }
        }

        room = default;
        return false;
    }
}
