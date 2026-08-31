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
    private readonly WorldTownNpc[] townNpcs;
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
        townNpcs = source.TownNpcs.ToArray();

        foreach (WorldTownRoom room in rooms)
        {
            if (!IsInWorld(room.X, room.Y) || !NpcTypeId.TryCreate(room.NpcType, out _))
                throw new InvalidDataException("Loaded town-room state is outside the current world/catalog bounds.");
            if (!roomsByNpcType.TryAdd(room.NpcType, room))
                throw new InvalidDataException($"Loaded town-room state contains duplicate NPC type {room.NpcType}.");
        }
    }

    public int Count => townNpcs.Length;

    public bool TryGet(short slot, out WorldTownNpc npc)
    {
        if ((uint)slot >= (uint)townNpcs.Length)
        {
            npc = default!;
            return false;
        }

        npc = townNpcs[slot];
        return true;
    }

    public bool TryReserveRuntimeSlots(RuntimeNpcStore npcStore)
    {
        ArgumentNullException.ThrowIfNull(npcStore);
        if (townNpcs.Length > npcStore.Capacity)
            return false;

        for (int slot = 0; slot < townNpcs.Length; slot++)
        {
            WorldTownNpc npc = townNpcs[slot];
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
            if (!npcStore.TrySpawn(checked((byte)slot), in update, out _))
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
        townNpcs[slot] = npc with { Homeless = true };
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
        townNpcs[slot] = npc with
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

    public WorldNpcPersistence CaptureNpcPersistence() => new(
        shimmeredTownNpcIndices.ToArray(),
        townNpcs.ToArray(),
        persistentNpcs.ToArray());

    public WorldTownRoom[] CaptureTownRooms() =>
        roomsByNpcType.Values
            .OrderBy(static room => room.NpcType)
            .ToArray();

    public RuntimeTownNpcHomeCommit[] CaptureHomeBaselines()
    {
        var result = new RuntimeTownNpcHomeCommit[townNpcs.Length];
        for (short slot = 0; slot < townNpcs.Length; slot++)
        {
            WorldTownNpc npc = townNpcs[slot];
            if (!NpcTypeId.TryCreate(npc.NetId, out NpcTypeId type))
                continue;

            TerrariaNpcHomeStatus status = npc.Homeless
                ? TerrariaNpcHomeStatus.Homeless
                : roomsByNpcType.ContainsKey(type.Value)
                    ? TerrariaNpcHomeStatus.HasRoom
                    : TerrariaNpcHomeStatus.None;
            result[slot] = new RuntimeTownNpcHomeCommit(slot, type, npc.HomeTileX, npc.HomeTileY, status);
        }
        return result;
    }

    private VanillaHousingOccupant[] CaptureOccupantsExcept(short ignoredSlot)
    {
        var occupants = new List<VanillaHousingOccupant>(townNpcs.Length);
        for (short slot = 0; slot < townNpcs.Length; slot++)
        {
            if (slot == ignoredSlot)
                continue;

            WorldTownNpc npc = townNpcs[slot];
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
