namespace TerraRuntime.World;

/// <summary>
/// The mutable owner of the already decoded vanilla tile-entity representation.
/// This is world metadata, not another item balance or a client report history.
/// Only source-qualified tool participants may change pose/remove a loaded row.
/// </summary>
public sealed class RuntimeWorldFurnitureStore1458
{
    private readonly WorldTileEntity?[] entities;
    private readonly Dictionary<int, int> indices;
    private readonly Dictionary<(short X, short Y), int> positions;
    private readonly int[] sourceOrder;
    private ulong revision;
    private bool exhausted;
    internal WorldTileStore Tiles { get; }

    private RuntimeWorldFurnitureStore1458(WorldTileStore tiles, WorldTileEntity[] source)
    {
        Tiles = tiles;
        entities = new WorldTileEntity?[source.Length];
        indices = new(source.Length);
        positions = new(source.Length);
        sourceOrder = new int[source.Length];
        for (int index = 0; index < source.Length; index++)
        {
            var row = Clone(source[index]);
            entities[index] = row;
            indices.Add(row.PersistedId, index);
            positions.Add((row.X, row.Y), row.PersistedId);
            sourceOrder[index] = row.PersistedId;
        }
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
            if (row is null || row.PersistedId < 0 || !ids.Add(row.PersistedId) ||
                !positions.Add((row.X, row.Y)) || row.X < 0 || row.Y < 0 ||
                row.X >= dimensions.WidthTiles || row.Y >= dimensions.HeightTiles ||
                !IsKnownPayload(row.Payload) || !WorldTileEntityItemValidator.HasValidItemTypes(row.Payload) || !PayloadMatchesKind(row.Kind, row.Payload)) return false;
        }
        store = new(tiles, source.ToArray());
        return true;
    }

    public bool TryCaptureById(int id, out WorldTileEntity? row)
    {
        row = null;
        if (!indices.TryGetValue(id, out int index) || Volatile.Read(ref entities[index]) is not { } found) return false;
        row = Clone(found); return true;
    }

    internal IEnumerable<WorldTileEntity> EnumerateRows()
    {
        foreach (int id in sourceOrder)
            if (Volatile.Read(ref entities[indices[id]]) is { } row) yield return row;
    }

    public WorldTileEntity[] CaptureSnapshot()
    {
        var result = new List<WorldTileEntity>(entities.Length);
        foreach (int id in sourceOrder)
            if (Volatile.Read(ref entities[indices[id]]) is { } row) result.Add(Clone(row));
        return result.ToArray();
    }

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

        internal Preparation(RuntimeWorldFurnitureStore1458 owner, ulong before, int maximumChanges) { this.owner = owner; this.before = before; this.maximumChanges = maximumChanges; }
        internal WorldTileStore Tiles => owner.Tiles;
        internal bool HasChanges => changes.Count != 0;
        internal bool TrySeal()
        {
            if (!CanAdoptUnpublished) return false;
            sealedPreparation = true; return true;
        }
        internal IEnumerable<(short X, short Y)> ChangedAnchors => changes.Keys.Select(id => { var row = owner.entities[owner.indices[id]]!; return (row.X, row.Y); });
        internal bool IsCurrentOwned => !failed && !adopted && !owner.exhausted && owner.revision == before;
        internal bool TryGetAt(int x, int y, out WorldTileEntity? row)
        {
            row = null;
            if (!IsCurrentOwned || x < 0 || y < 0 || x > short.MaxValue || y > short.MaxValue) return false;
            if (!owner.positions.TryGetValue(((short)x, (short)y), out int id)) return true;
            if (changes.TryGetValue(id, out var changed)) { row = changed is null ? null : Clone(changed); return true; }
            var original = Volatile.Read(ref owner.entities[owner.indices[id]]);
            row = original is null ? null : Clone(original);
            return true;
        }
        internal bool TryStageRemoval(int x, int y, WorldTileEntityKind expectedKind)
        {
            if (sealedPreparation || !TryGetAt(x, y, out var row)) return Fail();
            if (row is null) return true; // known source absence, not missing runtime ownership
            if (row.Kind != expectedKind || changes.Count == maximumChanges && !changes.ContainsKey(row.PersistedId)) return Fail();
            changes[row.PersistedId] = null;
            return IsCurrentOwned;
        }
        internal bool TryStageDisplayDollPose(int x, int y, int id, byte expectedPose, byte sourcePose)
        {
            if (sealedPreparation || !TryGetAt(x, y, out var row) || row is null || row.PersistedId != id ||
                row.Kind != WorldTileEntityKind.DisplayDoll || row.Payload is not WorldDisplayDollPayload doll ||
                doll.Pose != expectedPose || changes.Count == maximumChanges && !changes.ContainsKey(id)) return Fail();
            changes[id] = row with { Payload = doll with { Pose = sourcePose } };
            return IsCurrentOwned;
        }
        internal bool TryStageWeaponsRackItemClear(int x, int y, in WorldTileEntityItem expected)
        {
            if (sealedPreparation || !TryGetAt(x, y, out var row) || row is null ||
                row.Kind != WorldTileEntityKind.WeaponsRack || row.Payload is not WorldItemTileEntityPayload rack ||
                rack.Item != expected || changes.Count == maximumChanges && !changes.ContainsKey(row.PersistedId)) return Fail();
            changes[row.PersistedId] = row with { Payload = new WorldItemTileEntityPayload(default) };
            return IsCurrentOwned;
        }
        internal bool HasSameSourceEffects(Preparation other)
        {
            if (!ReferenceEquals(owner, other.owner) || before != other.before || failed || adopted || other.failed || other.adopted || changes.Count != other.changes.Count) return false;
            foreach (var (id, row) in changes)
            {
                if (!other.changes.TryGetValue(id, out var otherRow) || (row is null) != (otherRow is null) ||
                    row is not null && !Same(row, otherRow!)) return false;
            }
            return true;
        }
        internal bool CanAdoptUnpublished => IsCurrentOwned && before <= ulong.MaxValue - (ulong)changes.Count;
        internal bool TryAdoptUnpublished()
        {
            if (!CanAdoptUnpublished || !sealedPreparation) return false;
            foreach (var anchor in ChangedAnchors)
                if ((owner.Tiles.GetSectionVersion(TerrariaSectionGeometry.FromTile(owner.Tiles.Dimensions, anchor.X, anchor.Y)) & 1L) == 0)
                    return false; // Only the concrete terrain/metadata fence can adopt these rows.
            foreach (var (id, row) in changes)
            {
                Volatile.Write(ref owner.entities[owner.indices[id]], row);
                owner.revision++;
            }
            if (owner.revision == ulong.MaxValue) owner.exhausted = true;
            adopted = true;
            return true;
        }
        private bool Fail() { failed = true; return false; }
    }
}
