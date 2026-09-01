using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
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
/// materialization. It owns only the verified move-in slice: bounded house discovery, live revalidation, duplicate
/// suppression, runtime NPC allocation and packet-60 home publication. Vanilla randomized fallback placement,
/// localized arrival text and exact WorldGen room-priority randomness remain outside this slice.
/// </summary>
internal sealed class RuntimeTownNpcMoveInCoordinator1458
{
    private readonly RuntimeTownNpcStateStore townNpcs;
    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeNpcReplicationRegistry? replication;
    private readonly RuntimeTownHouseCandidateIndex1458 houses;
    private readonly VanillaTownNpcSpawnCadence1458 cadence = new();
    private readonly VanillaTownSpawnWorldFacts1458 worldFacts;
    private readonly IRuntimeTownNpcArrivalSink1458? arrivals;
    private readonly RuntimeWorldProgressionMutations? progression;

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
    }

    public int HouseScanBudgetPerTick { get; init; } = 4096;

    public long SuccessfulMoveIns { get; private set; }

    public void Tick(
        in RuntimeTownNpcMoveInConditions1458 conditions,
        ReadOnlySpan<VanillaTownSpawnPlayerFacts1458> players)
    {
        houses.Scan(HouseScanBudgetPerTick);
        if (!conditions.AllowsMoveIn || !cadence.Advance(conditions.WorldUpdateRate))
            return;

        NpcTypeId[] activeTypes = townNpcs.CaptureActiveTownTypes();
        VanillaTownSpawnEligibility1458 eligibility = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in worldFacts,
            players,
            activeTypes);
        if (eligibility.EligibleTypes.Length == 0)
            return;

        VanillaHousingOccupant[] occupants = townNpcs.CaptureHousingOccupants();
        foreach (NpcTypeId type in eligibility.EligibleTypes)
        {
            if (townNpcs.ContainsNpcType(type) ||
                !houses.TryFindRoom(type, occupants, out VanillaHousingPlacement placement))
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
}
