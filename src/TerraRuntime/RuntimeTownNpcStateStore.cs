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

internal readonly record struct RuntimeTownNpcIdentityCommit(
    short NpcSlot,
    string GivenName,
    int VariationIndex)
{
    public TerrariaTownNpcIdentityState ToWireState() => new(NpcSlot, GivenName, VariationIndex);
}

/// <summary>
/// Authoritative owner for persisted town-NPC home state and the v326 TownRoomManager mapping. Besides fast lookup by
/// NPC type, room pairs retain Terraria's insertion order: Load appends in file order, SetRoom removes the old pair and
/// appends the replacement, and KickOut removes it. This order is gameplay-visible through AddOccupantsToList.
/// </summary>
internal sealed class RuntimeTownNpcStateStore
{
    public const int MaximumTownNpcs = 200;

    private readonly SortedSet<int> shimmeredTownNpcTypes;
    private readonly WorldPersistentNpc[] persistentNpcs;
    private readonly SortedDictionary<short, WorldTownNpc> townNpcsBySlot = [];
    private readonly Dictionary<int, WorldTownRoom> roomsByNpcType = [];
    private readonly List<int> roomNpcTypeOrder = [];
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
        shimmeredTownNpcTypes = new SortedSet<int>(source.ShimmeredTownNpcIndices);
        persistentNpcs = source.PersistentNpcs.ToArray();
        for (short slot = 0; slot < source.TownNpcs.Length; slot++)
            townNpcsBySlot.Add(slot, source.TownNpcs[slot]);

        foreach (WorldTownRoom room in rooms)
        {
            if (!IsInWorld(room.X, room.Y) || !NpcTypeId.TryCreate(room.NpcType, out _))
                throw new InvalidDataException("Loaded town-room state is outside the current world/catalog bounds.");
            if (!roomsByNpcType.TryAdd(room.NpcType, room))
                throw new InvalidDataException($"Loaded town-room state contains duplicate NPC type {room.NpcType}.");
            roomNpcTypeOrder.Add(room.NpcType);
        }
    }

    public int Count => townNpcsBySlot.Count;

    public bool TryGet(short slot, out WorldTownNpc npc) =>
        townNpcsBySlot.TryGetValue(slot, out npc!);

    public bool TryGetRoom(NpcTypeId type, out WorldTownRoom room) =>
        roomsByNpcType.TryGetValue(type.Value, out room);

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

    public NpcTypeId[] CaptureRoomOccupantsInManagerOrder(int homeTileX, int homeTileY)
    {
        var result = new List<NpcTypeId>();
        foreach (int npcType in roomNpcTypeOrder)
        {
            if (!roomsByNpcType.TryGetValue(npcType, out WorldTownRoom room) ||
                room.X != homeTileX || room.Y != homeTileY ||
                !NpcTypeId.TryCreate(npcType, out NpcTypeId type))
            {
                continue;
            }
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

        RemoveRoom(type.Value);
        townNpcsBySlot[slot] = npc with { Homeless = true };
        commit = new RuntimeTownNpcHomeCommit(
            slot,
            type,
            npc.HomeTileX,
            npc.HomeTileY,
            TerrariaNpcHomeStatus.Homeless);
        return true;
    }

    /// <summary>
    /// Applies WorldGen.QuickFindHome's successful NPC-local home coordinates without rewriting TownRoomManager.
    /// Vanilla QuickFindHome mutates NPC.homeTileX/Y/homeless/homelessDespawn only; it deliberately does not call
    /// TownManager.SetRoom, so the persisted manager assignment remains an independent source of future preference.
    /// </summary>
    public bool TryApplyQuickFindHome(
        short slot,
        in VanillaHousingPlacement placement,
        out RuntimeTownNpcHomeCommit commit)
    {
        if (!placement.IsValid ||
            !TryGetEligible(slot, out WorldTownNpc npc, out NpcTypeId type) ||
            !IsInWorld(placement.HomeTileX, placement.HomeTileY) ||
            placement.HomeTileX > short.MaxValue ||
            placement.HomeTileY > short.MaxValue)
        {
            commit = default;
            return false;
        }

        townNpcsBySlot[slot] = npc with
        {
            Homeless = false,
            HomeTileX = placement.HomeTileX,
            HomeTileY = placement.HomeTileY,
            HomelessDespawn = false
        };
        commit = new RuntimeTownNpcHomeCommit(
            slot,
            type,
            placement.HomeTileX,
            placement.HomeTileY,
            TerrariaNpcHomeStatus.HasRoom);
        return true;
    }

    /// <summary>
    /// Applies QuickFindHome failure without kicking the NPC from TownRoomManager. Vanilla sets only NPC.homeless here;
    /// manual kickOut is the separate operation that removes the manager room and arms lookForHomeTimeout.
    /// </summary>
    public bool TryMarkQuickFindHomeless(short slot, out RuntimeTownNpcHomeCommit commit)
    {
        if (!TryGetEligible(slot, out WorldTownNpc npc, out NpcTypeId type))
        {
            commit = default;
            return false;
        }

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
        SetRoom(new WorldTownRoom(type.Value, homeTileX, homeTileY));
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
        var physicalSpawn = new RuntimeTownNpcPhysicalSpawn1458(
            placement.HomeTileX,
            placement.HomeTileY,
            DirectionX: 0,
            SafeFromPlayers: true,
            UsedFallbackSearch: false);
        return TryAddResident(type, in placement, in physicalSpawn, npcStore, out snapshot, out homeCommit);
    }

    public bool TryAddResident(
        NpcTypeId type,
        in VanillaHousingPlacement placement,
        in RuntimeTownNpcPhysicalSpawn1458 physicalSpawn,
        RuntimeNpcStore npcStore,
        out NpcSnapshot snapshot,
        out RuntimeTownNpcHomeCommit homeCommit)
    {
        ArgumentNullException.ThrowIfNull(npcStore);
        if (!placement.IsValid ||
            !physicalSpawn.IsValid ||
            Count >= MaximumTownNpcs ||
            ContainsNpcType(type) ||
            !VanillaTownNpcFacts1458.TryGetDefinition(type, out VanillaNpcDefinition definition))
        {
            snapshot = default;
            homeCommit = default;
            return false;
        }

        // NPC.NewNPC receives bottom-center X/Y, then SetDefaults establishes the final hitbox before Bottom is set.
        float positionX = physicalSpawn.TileX * 16f - definition.BaseWidth / 2f;
        float positionY = physicalSpawn.TileY * 16f - definition.BaseHeight;
        NpcSimulationState simulation = NpcSimulationState.Initial with
        {
            DirectionX = physicalSpawn.DirectionX
        };
        var update = new NpcStateUpdate(
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: positionX,
            PositionY: positionY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: simulation);
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
        SetRoom(new WorldTownRoom(type.Value, placement.HomeTileX, placement.HomeTileY));
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
        RemoveRoom(type.Value);
        return true;
    }

    public bool TryUpdatePosition(short slot, in NpcSnapshot snapshot)
    {
        if (!townNpcsBySlot.TryGetValue(slot, out WorldTownNpc? npc))
            return false;
        townNpcsBySlot[slot] = npc with { X = snapshot.PositionX, Y = snapshot.PositionY };
        return true;
    }

    public bool TryToggleShimmerVariation(
        short slot,
        NpcTypeId type,
        in NpcSnapshot snapshot,
        out RuntimeTownNpcIdentityCommit commit)
    {
        if (!townNpcsBySlot.TryGetValue(slot, out WorldTownNpc? npc) ||
            npc.NetId != type.Value ||
            snapshot.Handle.Slot != slot ||
            snapshot.Type != type.Value ||
            !VanillaTownNpcShimmerCatalog1458.CanTogglePersistentTownVariant(type))
        {
            commit = default;
            return false;
        }

        int current = npc.TownNpcVariationIndex ?? 0;
        int next = current == 1 ? 0 : 1;
        townNpcsBySlot[slot] = npc with
        {
            X = snapshot.PositionX,
            Y = snapshot.PositionY,
            TownNpcVariationIndex = next
        };
        if (next == 1)
            shimmeredTownNpcTypes.Add(type.Value);
        else
            shimmeredTownNpcTypes.Remove(type.Value);

        commit = new RuntimeTownNpcIdentityCommit(slot, npc.GivenName, next);
        return true;
    }

    public WorldNpcPersistence CaptureNpcPersistence() => new(
        shimmeredTownNpcTypes.ToArray(),
        townNpcsBySlot.Values.ToArray(),
        persistentNpcs.ToArray());

    public WorldTownRoom[] CaptureTownRooms()
    {
        var result = new WorldTownRoom[roomNpcTypeOrder.Count];
        int written = 0;
        foreach (int npcType in roomNpcTypeOrder)
        {
            if (roomsByNpcType.TryGetValue(npcType, out WorldTownRoom room))
                result[written++] = room;
        }
        return written == result.Length ? result : result.AsSpan(0, written).ToArray();
    }

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

    public RuntimeTownNpcIdentityCommit[] CaptureIdentityBaselines()
    {
        var result = new RuntimeTownNpcIdentityCommit[townNpcsBySlot.Count];
        int index = 0;
        foreach ((short slot, WorldTownNpc npc) in townNpcsBySlot)
            result[index++] = new RuntimeTownNpcIdentityCommit(slot, npc.GivenName, npc.TownNpcVariationIndex ?? 0);
        return result;
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

    private void SetRoom(in WorldTownRoom room)
    {
        RemoveRoom(room.NpcType);
        roomsByNpcType.Add(room.NpcType, room);
        roomNpcTypeOrder.Add(room.NpcType);
    }

    private void RemoveRoom(int npcType)
    {
        roomsByNpcType.Remove(npcType);
        roomNpcTypeOrder.Remove(npcType);
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
