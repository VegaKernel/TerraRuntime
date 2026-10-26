namespace TerraRuntime.World;

/// <summary>
/// The mutable owner of the already decoded vanilla tile-entity representation.
/// This is world metadata, not another item balance or a client report history.
/// Source-qualified participants own insertion, pose changes and removal in this same store.
/// </summary>
public sealed class RuntimeWorldFurnitureStore1458
{
    // TileEntity.ByID uses dictionary entry-slot enumeration and LIFO deleted
    // slots. Those slots affect source save order and therefore restart IDs.
    // They are bounded allocation metadata in this same canonical owner.
    private sealed record Image(WorldTileEntity?[] Slots, Dictionary<int, int> Indices,
        Dictionary<(short X, short Y), int> Positions, int NextId, int[] FreeSlots);

    private Image image;
    private readonly int maximumEntities;
    private ulong revision;
    private bool exhausted;
    internal WorldTileStore Tiles { get; }

    private RuntimeWorldFurnitureStore1458(WorldTileStore tiles, WorldTileEntity[] source, int maximumEntities)
    {
        Tiles = tiles;
        this.maximumEntities = maximumEntities;
        var rows = source.Select(Clone).ToArray();
        image = CreateImage(rows, rows.Length == 0 ? 0 : rows.Max(row => row.PersistedId) + 1);
    }

    private static Image CreateImage(WorldTileEntity?[] rows, int nextId, int[]? freeSlots = null)
    {
        var ids = new Dictionary<int, int>(rows.Length);
        var positions = new Dictionary<(short, short), int>(rows.Length);
        for (int index = 0; index < rows.Length; index++)
        {
            if (rows[index] is not { } row) continue;
            ids.Add(row.PersistedId, index);
            positions.Add((row.X, row.Y), row.PersistedId);
        }
        return new(rows, ids, positions, nextId, freeSlots ?? []);
    }

    internal static bool TryImport(WorldTileStore tiles, ReadOnlySpan<WorldTileEntity> source,
        int maximumEntities, out RuntimeWorldFurnitureStore1458? store)
    {
        store = null;
        ArgumentNullException.ThrowIfNull(tiles);
        var dimensions = tiles.Dimensions;
        if (maximumEntities < 0 || source.Length > maximumEntities) return false;
        var ids = new HashSet<int>(); var positions = new HashSet<(short, short)>();
        foreach (var row in source)
        {
            if (row is null || row.PersistedId < 0 || row.PersistedId == int.MaxValue || !ids.Add(row.PersistedId) ||
                !positions.Add((row.X, row.Y)) || row.X < 0 || row.Y < 0 ||
                row.X >= dimensions.WidthTiles || row.Y >= dimensions.HeightTiles ||
                !IsKnownPayload(row.Payload) || !WorldTileEntityItemValidator.HasValidItemTypes(row.Payload) || !PayloadMatchesKind(row.Kind, row.Payload)) return false;
        }
        store = new(tiles, source.ToArray(), maximumEntities);
        return true;
    }

    public bool TryCaptureById(int id, out WorldTileEntity? row)
    {
        var current = Volatile.Read(ref image);
        row = null;
        if (!current.Indices.TryGetValue(id, out int index)) return false;
        row = Clone(current.Slots[index]!); return true;
    }

    internal IEnumerable<WorldTileEntity> EnumerateRows() => Volatile.Read(ref image).Slots.OfType<WorldTileEntity>();

    public WorldTileEntity[] CaptureSnapshot() => Volatile.Read(ref image).Slots.OfType<WorldTileEntity>().Select(Clone).ToArray();

    internal bool TryPrepare(int maximumChanges, out Preparation? preparation)
    {
        preparation = null;
        if (maximumChanges < 1 || exhausted || revision == ulong.MaxValue) return false;
        preparation = new(this, revision, maximumChanges);
        return true;
    }

    private static bool IsKnownPayload(WorldTileEntityPayload payload) => payload switch
    {
        WorldDisplayDollPayload doll => doll.Equipment is { Length: 9 } && doll.Dyes is { Length: 9 },
        WorldHatRackPayload hats => hats.Items is { Length: 2 } && hats.Dyes is { Length: 2 },
        WorldTrainingDummyPayload or WorldItemTileEntityPayload or WorldLogicSensorPayload or
            WorldEmptyTileEntityPayload or WorldLeashedAnchorPayload => true,
        _ => false
    };

    private static bool PayloadMatchesKind(WorldTileEntityKind kind, WorldTileEntityPayload payload) => (kind, payload) switch
    {
        (WorldTileEntityKind.TrainingDummy, WorldTrainingDummyPayload) => true,
        (WorldTileEntityKind.ItemFrame or WorldTileEntityKind.WeaponsRack or WorldTileEntityKind.FoodPlatter or
            WorldTileEntityKind.DeadCellsDisplayJar, WorldItemTileEntityPayload) => true,
        (WorldTileEntityKind.LogicSensor, WorldLogicSensorPayload) => true,
        (WorldTileEntityKind.DisplayDoll, WorldDisplayDollPayload) => true,
        (WorldTileEntityKind.HatRack, WorldHatRackPayload) => true,
        (WorldTileEntityKind.TeleportationPylon, WorldEmptyTileEntityPayload) => true,
        (WorldTileEntityKind.KiteAnchor or WorldTileEntityKind.CritterAnchor, WorldLeashedAnchorPayload) => true,
        _ => false
    };

    private static WorldTileEntity Clone(WorldTileEntity row) => row with { Payload = row.Payload switch
    {
        WorldDisplayDollPayload doll => doll with { Equipment = doll.Equipment.ToArray(), Dyes = doll.Dyes.ToArray() },
        WorldHatRackPayload hats => hats with { Items = hats.Items.ToArray(), Dyes = hats.Dyes.ToArray() },
        _ => row.Payload
    } };

    private static bool Same(WorldTileEntity first, WorldTileEntity second)
    {
        if (first.PersistedId != second.PersistedId || first.X != second.X || first.Y != second.Y || first.Kind != second.Kind) return false;
        return (first.Payload, second.Payload) switch
        {
            (WorldDisplayDollPayload a, WorldDisplayDollPayload b) => a.Pose == b.Pose && a.Misc == b.Misc &&
                a.Equipment.AsSpan().SequenceEqual(b.Equipment) && a.Dyes.AsSpan().SequenceEqual(b.Dyes),
            (WorldHatRackPayload a, WorldHatRackPayload b) => a.Items.AsSpan().SequenceEqual(b.Items) && a.Dyes.AsSpan().SequenceEqual(b.Dyes),
            _ => first.Payload == second.Payload
        };
    }

    internal sealed class Preparation
    {
        private readonly RuntimeWorldFurnitureStore1458 owner;
        private readonly ulong before;
        private readonly int maximumChanges;
        private readonly Dictionary<int, WorldTileEntity?> changes = [];
        private bool failed;
        private bool adopted;
        private bool sealedPreparation;
        private readonly Image original;
        private int nextId;
        private Image? prepared;
        private readonly Dictionary<(short X, short Y), int> addedPositions = [];
        private readonly Dictionary<int, (short X, short Y)> createdAnchors = [];
        private readonly Dictionary<int, int> createdSlots = [];
        private List<WorldTileEntity?>? workingSlots;
        private List<int>? workingFreeSlots;
        private void EnsureWorkingImage()
        {
            workingSlots ??= original.Slots.ToList();
            workingFreeSlots ??= original.FreeSlots.ToList();
        }
        private int SlotOf(int id) => original.Indices.TryGetValue(id, out int slot) ? slot : createdSlots[id];
        private void SetWorkingRow(WorldTileEntity row)
        { EnsureWorkingImage(); workingSlots![SlotOf(row.PersistedId)] = row; }

        internal Preparation(RuntimeWorldFurnitureStore1458 owner, ulong before, int maximumChanges)
        {
            this.owner = owner; this.before = before; this.maximumChanges = maximumChanges;
            original = owner.image; nextId = original.NextId;
        }
        internal WorldTileStore Tiles => owner.Tiles;
        internal bool HasChanges => changes.Count != 0;
        internal bool TrySeal()
        {
            if (!CanAdoptUnpublished) return false;
            if (sealedPreparation) return true;
            // All bounded arrays/lookups are allocated before joint adoption.
            // Preserve dictionary slots/free-list order, not just the live set.
            if (changes.Count == 0) prepared = original;
            else
            {
                EnsureWorkingImage();
                if (workingSlots!.Count - workingFreeSlots!.Count > owner.maximumEntities) return Fail();
                prepared = CreateImage(workingSlots.ToArray(), nextId, workingFreeSlots.ToArray());
            }
            sealedPreparation = true; return true;
        }
        internal IEnumerable<(short X, short Y)> ChangedAnchors => changes.Select(change =>
        {
            if (change.Value is { } row) return (row.X, row.Y);
            if (original.Indices.TryGetValue(change.Key, out int index))
                return (original.Slots[index]!.X, original.Slots[index]!.Y);
            return createdAnchors[change.Key];
        });
        internal bool IsCurrentOwned => !failed && !adopted && !owner.exhausted && owner.revision == before && ReferenceEquals(owner.image, original);
        internal bool TryGetAt(int x, int y, out WorldTileEntity? row)
        {
            row = null;
            if (!IsCurrentOwned || x < 0 || y < 0 || x > short.MaxValue || y > short.MaxValue) return false;
            if (!addedPositions.TryGetValue(((short)x, (short)y), out int id) &&
                !original.Positions.TryGetValue(((short)x, (short)y), out id)) return true;
            if (changes.TryGetValue(id, out var changed)) { row = changed is null ? null : Clone(changed); return true; }
            row = Clone(original.Slots[original.Indices[id]]!);
            return true;
        }
        internal bool TryStageRemoval(int x, int y, WorldTileEntityKind expectedKind)
        {
            if (sealedPreparation || !TryGetAt(x, y, out var row)) return Fail();
            if (row is null) return true; // known source absence, not missing runtime ownership
            if (row.Kind != expectedKind || changes.Count == maximumChanges && !changes.ContainsKey(row.PersistedId)) return Fail();
            changes[row.PersistedId] = null;
            EnsureWorkingImage(); int slot = SlotOf(row.PersistedId);
            workingSlots![slot] = null; workingFreeSlots!.Add(slot);
            return IsCurrentOwned;
        }
        internal bool TryStageDisplayDollPose(int x, int y, int id, byte expectedPose, byte sourcePose)
        {
            if (sealedPreparation || !TryGetAt(x, y, out var row) || row is null || row.PersistedId != id ||
                row.Kind != WorldTileEntityKind.DisplayDoll || row.Payload is not WorldDisplayDollPayload doll ||
                doll.Pose != expectedPose || changes.Count == maximumChanges && !changes.ContainsKey(id)) return Fail();
            changes[id] = row with { Payload = doll with { Pose = sourcePose } };
            SetWorkingRow(changes[id]!);
            return IsCurrentOwned;
        }
        internal bool TryStageWeaponsRackItemClear(int x, int y, in WorldTileEntityItem expected)
        {
            if (sealedPreparation || !TryGetAt(x, y, out var row) || row is null ||
                row.Kind != WorldTileEntityKind.WeaponsRack || row.Payload is not WorldItemTileEntityPayload rack ||
                rack.Item != expected || changes.Count == maximumChanges && !changes.ContainsKey(row.PersistedId)) return Fail();
            changes[row.PersistedId] = row with { Payload = new WorldItemTileEntityPayload(default) };
            SetWorkingRow(changes[row.PersistedId]!);
            return IsCurrentOwned;
        }
        // Terraria 1.4.5.8 TileEntity.Place/AssignNewID: session IDs advance
        // monotonically, including remove-then-create at the same anchor. Only
        // a funded placement participant may stage these initially empty kinds.
        internal bool TryStageEmptyPlacement(int x, int y, WorldTileEntityKind kind, out int id)
        {
            id = -1;
            if (sealedPreparation || !TryGetAt(x, y, out var existing) || existing is not null ||
                x >= owner.Tiles.Dimensions.WidthTiles || y >= owner.Tiles.Dimensions.HeightTiles ||
                nextId == int.MaxValue || changes.Count == maximumChanges) return Fail();
            WorldTileEntityPayload? payload = kind switch
            {
                WorldTileEntityKind.DisplayDoll => new WorldDisplayDollPayload(0,
                    new WorldTileEntityItem?[9], new WorldTileEntityItem?[9], null),
                WorldTileEntityKind.WeaponsRack => new WorldItemTileEntityPayload(default),
                WorldTileEntityKind.HatRack => new WorldHatRackPayload(
                    new WorldTileEntityItem?[2], new WorldTileEntityItem?[2]),
                _ => null
            };
            if (payload is null) return Fail();
            EnsureWorkingImage();
            int live = workingSlots!.Count - workingFreeSlots!.Count;
            if (live == owner.maximumEntities) return Fail();
            int slot;
            if (workingFreeSlots.Count != 0)
            {
                slot = workingFreeSlots[^1]; workingFreeSlots.RemoveAt(workingFreeSlots.Count - 1);
            }
            else { slot = workingSlots.Count; workingSlots.Add(null); }
            id = nextId++;
            changes.Add(id, new(id, checked((short)x), checked((short)y), kind, payload));
            addedPositions[((short)x, (short)y)] = id;
            createdAnchors.Add(id, ((short)x, (short)y)); createdSlots.Add(id, slot);
            workingSlots[slot] = changes[id];
            return IsCurrentOwned;
        }

        internal bool HasSameSourceEffects(Preparation other)
        {
            if (!ReferenceEquals(owner, other.owner) || before != other.before || failed || adopted || other.failed || other.adopted || changes.Count != other.changes.Count || nextId != other.nextId) return false;
            foreach (var (id, row) in changes)
            {
                if (!other.changes.TryGetValue(id, out var otherRow) || (row is null) != (otherRow is null) ||
                    row is not null && !Same(row, otherRow!)) return false;
            }
            IReadOnlyList<WorldTileEntity?> first = workingSlots is null ? original.Slots : workingSlots;
            IReadOnlyList<WorldTileEntity?> second = other.workingSlots is null ? other.original.Slots : other.workingSlots;
            if (first.Count != second.Count) return false;
            for (int slot = 0; slot < first.Count; slot++)
                if ((first[slot] is null) != (second[slot] is null) || first[slot] is { } row && !Same(row, second[slot]!)) return false;
            IEnumerable<int> free = workingFreeSlots is null ? original.FreeSlots : workingFreeSlots;
            IEnumerable<int> otherFree = other.workingFreeSlots is null ? other.original.FreeSlots : other.workingFreeSlots;
            return free.SequenceEqual(otherFree);
        }
        internal bool CanAdoptUnpublished => IsCurrentOwned && before <= ulong.MaxValue - (ulong)changes.Count;
        internal bool TryAdoptUnpublished()
        {
            if (!CanAdoptUnpublished || !sealedPreparation) return false;
            foreach (var anchor in ChangedAnchors)
                if ((owner.Tiles.GetSectionVersion(TerrariaSectionGeometry.FromTile(owner.Tiles.Dimensions, anchor.X, anchor.Y)) & 1L) == 0)
                    return false; // Only the concrete terrain/metadata fence can adopt these rows.
            if (prepared is null) return false;
            Volatile.Write(ref owner.image, prepared);
            owner.revision += (ulong)changes.Count;
            if (owner.revision == ulong.MaxValue) owner.exhausted = true;
            adopted = true;
            return true;
        }
        private bool Fail() { failed = true; return false; }
    }
}
