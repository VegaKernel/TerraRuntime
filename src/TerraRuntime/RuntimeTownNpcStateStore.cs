using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

internal readonly record struct RuntimeTownNpcHomeCommit(
    short NpcSlot,
    NpcTypeId NpcType,
    int HomeTileX,
    int HomeTileY,
    TerrariaNpcHomeStatus Status)
{
    public TerrariaNpcHomeState ToWireState() => new(
        NpcSlot,
        checked((short)HomeTileX),
        checked((short)HomeTileY),
        (byte)Status);
}

/// <summary>
/// Authoritative owner for persisted town-NPC home state and the v326 TownRoomManager mapping. The store keeps the
/// original NPC/persistent metadata detached from the loaded WorldFileData, supports generation-safe runtime slot
/// reservation for the persisted town roster, and captures immutable save snapshots on the game-loop owner.
/// </summary>
internal sealed class RuntimeTownNpcStateStore
{
    public const int MaximumTownNpcs = 200;

    private readonly int[] shimmeredTownNpcIndices;
    private readonly WorldPersistentNpc[] persistentNpcs;
    private readonly SortedDictionary<short, WorldTownNpc> townNpcsBySlot = [];
    private readonly Dictionary<int, WorldTownRoom> roomsByNpcType = [];
    private readonly WorldDimensions dimensions;

    public RuntimeTownNpcStateStore(
        WorldNpcPersistence source,
        IReadOnlyList<WorldTownRoom> rooms,
        WorldDimensions dimensions)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rooms);
        if (source.TownNpcs.Length > MaximumTownNpcs)
            throw new InvalidDataException($"Town NPC count {source.TownNpcs.Length} exceeds vanilla Main.maxNPCs {MaximumTownNpcs}.");

        this.dimensions = dimensions;
        shimmeredTownNpcIndices = source.ShimmeredTownNpcIndices.ToArray();
        persistentNpcs = source.PersistentNpcs.ToArray();
        for (short slot = 0; slot < source.TownNpcs.Length; slot++)
            townNpcsBySlot.Add(slot, source.TownNpcs[slot]);

        foreach (WorldTownRoom room in rooms)
        {
            if (!IsInWorld(room.X, room.Y) || !NpcTypeId.TryCreate(room.NpcType, out _))
                throw new InvalidDataException("Loaded town-room state is outside the current world/catalog bounds.");
            if (!roomsByNpcType.TryAdd(room.NpcType, room))
                throw new InvalidDataException($"Loaded town-room state contains duplicate NPC type {room.NpcType}.");
        }
    }

    public int Count => townNpcsBySlot.Count;

    public bool TryGet(short slot, out WorldTownNpc npc) =>
        townNpcsBySlot.TryGetValue(slot, out npc!);

    public bool ContainsNpcType(NpcTypeId type) =>
        townNpcsBySlot.Values.Any(npc => npc.NetId == type.Value);

    public NpcTypeId[] CaptureActiveTownTypes()
    {
        var result = new List<NpcTypeId>(townNpcsBySlot.Count);
        foreach (WorldTownNpc npc in townNpcsBySlot.Values)
        {
            if (NpcTypeId.TryCreate(npc.NetId, out NpcTypeId type))
                result.Add(type);
        }
        return result.ToArray();
    }

    public VanillaHousingOccupant[] CaptureHousingOccupants(short ignoredSlot = -1) =>
        CaptureOccupantsExcept(ignoredSlot);

    public bool TryReserveRuntimeSlots(RuntimeNpcStore npcStore)
    {
        ArgumentNullException.ThrowIfNull(npcStore);
        if (townNpcsBySlot.Count > npcStore.Capacity)
            return false;

        foreach ((short slot, WorldTownNpc npc) in townNpcsBySlot)
        {
            if (!NpcTypeId.TryCreate(npc.NetId, out NpcTypeId type) || npc.NetId > short.MaxValue)
                return false;

            var update = new NpcStateUpdate(
                Type: type.Value,
                NetId: checked((short)npc.NetId),
                PositionX: npc.X,
                PositionY: npc.Y,
                VelocityX: 0f,
                VelocityY: 0f,
                Target: VanillaNpcDefinitionCatalog.DefaultTarget,
                Ai: default,
                Simulation: NpcSimulationState.Initial);
            if ((uint)slot > byte.MaxValue || !npcStore.TrySpawn(checked((byte)slot), in update, out _))
                return false;
        }

        return true;
    }

    public bool TryKickOut(short slot, out RuntimeTownNpcHomeCommit commit)
    {
        if (!TryGetEligible(slot, out WorldTownNpc npc, out NpcTypeId type))
        {
            commit = default;
            return false;
        }

        roomsByNpcType.Remove(type.Value);
        townNpcsBySlot[slot] = npc with { Homeless = true };
        commit = new RuntimeTownNpcHomeCommit(
            slot,
            type,
            npc.HomeTileX,
            npc.HomeTileY,
            TerrariaNpcHomeStatus.Homeless);
        return true;
    }

    public bool TryAssignRoom(
        short slot,
        int requestedTileX,
        int requestedTileY,
        VanillaHousingValidator1458 validator,
        out RuntimeTownNpcHomeCommit commit,
        out VanillaHousingValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validator);
        if (!TryGetEligible(slot, out WorldTownNpc npc, out NpcTypeId type) ||
            !IsInWorld(requestedTileX, requestedTileY))
        {
            commit = default;
            validationResult = VanillaHousingValidationResult.SpecialNpcConditionFailed;
            return false;
        }

        VanillaHousingOccupant[] occupants = CaptureOccupantsExcept(slot);
        VanillaHousingPlacement placement = validator.Validate(requestedTileX, requestedTileY, type, occupants);
        validationResult = placement.Result;
        if (!placement.IsValid ||
            placement.HomeTileX > short.MaxValue ||
            placement.HomeTileY > short.MaxValue)
        {
            commit = default;
            return false;
        }

        int homeTileX = placement.HomeTileX;
        int homeTileY = placement.HomeTileY;
        roomsByNpcType[type.Value] = new WorldTownRoom(type.Value, homeTileX, homeTileY);
        townNpcsBySlot[slot] = npc with
        {
            Homeless = false,
            HomeTileX = homeTileX,
            HomeTileY = homeTileY,
            HomelessDespawn = false
        };
        commit = new RuntimeTownNpcHomeCommit(
            slot,
            type,
            homeTileX,
            homeTileY,
            TerrariaNpcHomeStatus.HasRoom);
        return true;
    }

    public bool TryAddResident(
        NpcTypeId type,
        in VanillaHousingPlacement placement,
        RuntimeNpcStore npcStore,
        out NpcSnapshot snapshot,
        out RuntimeTownNpcHomeCommit homeCommit)
    {
        ArgumentNullException.ThrowIfNull(npcStore);
        if (!placement.IsValid ||
            Count >= MaximumTownNpcs ||
            ContainsNpcType(type) ||
            !VanillaTownNpcFacts1458.TryGetDefinition(type, out VanillaNpcDefinition definition))
        {
            snapshot = default;
            homeCommit = default;
            return false;
        }

        float positionX = placement.HomeTileX * 16f + 8f - definition.BaseWidth / 2f;
        float positionY = placement.HomeTileY * 16f - definition.BaseHeight - 0.1f;
        var update = new NpcStateUpdate(
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: positionX,
            PositionY: positionY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
        if (!npcStore.TrySpawnVanilla(in update, out snapshot))
        {
            homeCommit = default;
            return false;
        }

        short slot = snapshot.Handle.Slot;
        townNpcsBySlot[slot] = new WorldTownNpc(
            type.Value,
            string.Empty,
            snapshot.PositionX,
            snapshot.PositionY,
            Homeless: false,
            placement.HomeTileX,
            placement.HomeTileY,
            TownNpcVariationIndex: null,
            HomelessDespawn: false);
        roomsByNpcType[type.Value] = new WorldTownRoom(type.Value, placement.HomeTileX, placement.HomeTileY);
        homeCommit = new RuntimeTownNpcHomeCommit(
            slot, type, placement.HomeTileX, placement.HomeTileY, TerrariaNpcHomeStatus.HasRoom);
        return true;
    }

    public bool CanAdoptRescuedResident(short slot, NpcTypeId type) =>
        slot >= 0 &&
        !townNpcsBySlot.ContainsKey(slot) &&
        !ContainsNpcType(type) &&
        VanillaTownNpcFacts1458.IsHousingEligible(type);

    public bool TryAdoptRescuedResident(short slot, NpcTypeId type, in NpcSnapshot snapshot)
    {
        if (!CanAdoptRescuedResident(slot, type) ||
            snapshot.Handle.Slot != slot ||
            snapshot.Type != type.Value ||
            !VanillaTownNpcFacts1458.TryGetDefinition(type, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int homeTileX = (int)(snapshot.PositionX + definition.Width / 2f) / 16;
        int homeTileY = (int)(snapshot.PositionY + definition.Height) / 16;
        if (!IsInWorld(homeTileX, homeTileY))
            return false;

        townNpcsBySlot.Add(slot, new WorldTownNpc(
            type.Value,
            string.Empty,
            snapshot.PositionX,
            snapshot.PositionY,
            Homeless: true,
            homeTileX,
            homeTileY,
            TownNpcVariationIndex: null,
            HomelessDespawn: false));
        roomsByNpcType.Remove(type.Value);
        return true;
    }

    public bool TryUpdatePosition(short slot, in NpcSnapshot snapshot)
    {
        if (!townNpcsBySlot.TryGetValue(slot, out WorldTownNpc? npc))
            return false;
        townNpcsBySlot[slot] = npc with { X = snapshot.PositionX, Y = snapshot.PositionY };
        return true;
    }

    public WorldNpcPersistence CaptureNpcPersistence() => new(
        shimmeredTownNpcIndices.ToArray(),
        townNpcsBySlot.Values.ToArray(),
        persistentNpcs.ToArray());

    public WorldTownRoom[] CaptureTownRooms() =>
        roomsByNpcType.Values
            .OrderBy(static room => room.NpcType)
            .ToArray();

    public int CopyHomeBaselines(Span<RuntimeTownNpcHomeCommit> destination)
    {
        if (destination.Length < townNpcsBySlot.Count)
            throw new ArgumentException("Destination is smaller than the town NPC roster.", nameof(destination));

        int written = 0;
        foreach ((short slot, WorldTownNpc npc) in townNpcsBySlot)
        {
            if (!NpcTypeId.TryCreate(npc.NetId, out NpcTypeId type))
                continue;

            TerrariaNpcHomeStatus status = npc.Homeless
                ? TerrariaNpcHomeStatus.Homeless
                : roomsByNpcType.ContainsKey(type.Value)
                    ? TerrariaNpcHomeStatus.HasRoom
                    : TerrariaNpcHomeStatus.None;
            destination[written++] = new RuntimeTownNpcHomeCommit(slot, type, npc.HomeTileX, npc.HomeTileY, status);
        }
        return written;
    }

    public RuntimeTownNpcHomeCommit[] CaptureHomeBaselines()
    {
        var result = new RuntimeTownNpcHomeCommit[townNpcsBySlot.Count];
        int count = CopyHomeBaselines(result);
        return count == result.Length ? result : result.AsSpan(0, count).ToArray();
    }

    private VanillaHousingOccupant[] CaptureOccupantsExcept(short ignoredSlot)
    {
        var occupants = new List<VanillaHousingOccupant>(townNpcsBySlot.Count);
        foreach ((short slot, WorldTownNpc npc) in townNpcsBySlot)
        {
            if (slot == ignoredSlot)
                continue;
            if (npc.Homeless ||
                !roomsByNpcType.ContainsKey(npc.NetId) ||
                !NpcTypeId.TryCreate(npc.NetId, out NpcTypeId type))
            {
                continue;
            }

            occupants.Add(new VanillaHousingOccupant(type, npc.HomeTileX, npc.HomeTileY));
        }

        return occupants.ToArray();
    }

    private bool TryGetEligible(short slot, out WorldTownNpc npc, out NpcTypeId type)
    {
        if (!TryGet(slot, out npc) ||
            !NpcTypeId.TryCreate(npc.NetId, out type) ||
            !VanillaTownNpcFacts1458.IsHousingEligible(type))
        {
            npc = default!;
            type = default;
            return false;
        }
        return true;
    }

    private bool IsInWorld(int x, int y) =>
        x >= 0 && y >= 0 && x < dimensions.WidthTiles && y < dimensions.HeightTiles;
}
