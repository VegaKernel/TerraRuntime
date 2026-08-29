using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

public enum NpcShopRegistrationResult : byte
{
    Registered = 0,
    InvalidCatalog = 1,
    DuplicateShopId = 2,
    ArchetypeAlreadyHasShop = 3
}

public readonly record struct NpcShopCatalogEntry(
    ShopId ShopId,
    GameplayArchetypeId NpcArchetypeId,
    NpcShopCatalog Catalog);

/// <summary>Immutable hot-path shop catalog view published at an authoritative safe boundary.</summary>
public sealed class RuntimeNpcShopCatalogSnapshot
{
    private readonly NpcShopCatalogEntry[] entriesByArchetype;
    private readonly NpcShopCatalogEntry[] entriesByShop;

    internal RuntimeNpcShopCatalogSnapshot(
        NpcShopCatalogEntry[] entriesByArchetype,
        NpcShopCatalogEntry[] entriesByShop,
        ulong revision)
    {
        this.entriesByArchetype = entriesByArchetype;
        this.entriesByShop = entriesByShop;
        Revision = revision;
    }

    public ulong Revision { get; }
    public int Count => entriesByShop.Length;

    public bool TryGetByArchetype(GameplayArchetypeId archetypeId, out NpcShopCatalog catalog)
    {
        if (!archetypeId.IsAssigned)
        {
            catalog = null!;
            return false;
        }

        int low = 0;
        int high = entriesByArchetype.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            NpcShopCatalogEntry candidate = entriesByArchetype[middle];
            int comparison = candidate.NpcArchetypeId.CompareTo(archetypeId);
            if (comparison == 0)
            {
                catalog = candidate.Catalog;
                return true;
            }

            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        catalog = null!;
        return false;
    }

    public bool TryGetById(ShopId shopId, out NpcShopCatalog catalog)
    {
        if (!shopId.IsAssigned)
        {
            catalog = null!;
            return false;
        }

        int low = 0;
        int high = entriesByShop.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            NpcShopCatalogEntry candidate = entriesByShop[middle];
            int comparison = candidate.ShopId.CompareTo(shopId);
            if (comparison == 0)
            {
                catalog = candidate.Catalog;
                return true;
            }

            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        catalog = null!;
        return false;
    }
}

/// <summary>
/// Control-path NPC shop registry. Registration, catalog replacement and retirement are staged under a lock;
/// authoritative interaction code reads only immutable published snapshots.
/// </summary>
public sealed class RuntimeNpcShopCatalogRegistry
{
    private readonly object gate = new();
    private RuntimeNpcShopCatalogSnapshot published = new([], [], revision: 0);
    private Dictionary<ShopId, NpcShopCatalog>? pendingByShop;
    private Dictionary<GameplayArchetypeId, ShopId>? pendingByArchetype;
    private ulong nextRevision;

    public RuntimeNpcShopCatalogSnapshot Snapshot => Volatile.Read(ref published);

    public NpcShopRegistrationResult TryRegister(
        NpcShopCatalog catalog,
        out NpcShopRegistrationLease? lease)
    {
        if (catalog is null || !catalog.Id.IsAssigned || !catalog.NpcArchetypeId.IsAssigned)
        {
            lease = null;
            return NpcShopRegistrationResult.InvalidCatalog;
        }

        lock (gate)
        {
            EnsurePending();
            if (pendingByShop!.ContainsKey(catalog.Id))
            {
                lease = null;
                return NpcShopRegistrationResult.DuplicateShopId;
            }

            if (pendingByArchetype!.ContainsKey(catalog.NpcArchetypeId))
            {
                lease = null;
                return NpcShopRegistrationResult.ArchetypeAlreadyHasShop;
            }

            pendingByShop.Add(catalog.Id, catalog);
            pendingByArchetype.Add(catalog.NpcArchetypeId, catalog.Id);
            lease = new NpcShopRegistrationLease(this, catalog.Id, catalog.NpcArchetypeId);
            return NpcShopRegistrationResult.Registered;
        }
    }

    /// <summary>Publishes all staged shop changes atomically. Returns false when there is nothing to publish.</summary>
    public bool CommitPending()
    {
        lock (gate)
        {
            if (pendingByShop is null || pendingByArchetype is null)
                return false;
            if (nextRevision == ulong.MaxValue)
                throw new InvalidOperationException("NPC shop catalog revision exhausted.");

            NpcShopCatalogEntry[] byShop = new NpcShopCatalogEntry[pendingByShop.Count];
            int index = 0;
            foreach ((ShopId shopId, NpcShopCatalog catalog) in pendingByShop)
            {
                byShop[index++] = new NpcShopCatalogEntry(shopId, catalog.NpcArchetypeId, catalog);
            }

            Array.Sort(byShop, static (left, right) => left.ShopId.CompareTo(right.ShopId));
            NpcShopCatalogEntry[] byArchetype = (NpcShopCatalogEntry[])byShop.Clone();
            Array.Sort(byArchetype, static (left, right) => left.NpcArchetypeId.CompareTo(right.NpcArchetypeId));

            nextRevision++;
            Volatile.Write(ref published, new RuntimeNpcShopCatalogSnapshot(byArchetype, byShop, nextRevision));
            pendingByShop = null;
            pendingByArchetype = null;
            return true;
        }
    }

    internal bool TryReplace(
        ShopId shopId,
        GameplayArchetypeId archetypeId,
        NpcShopCatalog catalog)
    {
        if (catalog is null || catalog.Id != shopId || catalog.NpcArchetypeId != archetypeId)
            return false;

        lock (gate)
        {
            EnsurePending();
            if (!pendingByShop!.ContainsKey(shopId) ||
                !pendingByArchetype!.TryGetValue(archetypeId, out ShopId mapped) ||
                mapped != shopId)
            {
                return false;
            }

            pendingByShop[shopId] = catalog;
            return true;
        }
    }

    internal void Retire(ShopId shopId, GameplayArchetypeId archetypeId)
    {
        lock (gate)
        {
            EnsurePending();
            if (!pendingByShop!.Remove(shopId))
                return;

            if (pendingByArchetype!.TryGetValue(archetypeId, out ShopId mapped) && mapped == shopId)
                pendingByArchetype.Remove(archetypeId);
        }
    }

    private void EnsurePending()
    {
        if (pendingByShop is not null)
            return;

        RuntimeNpcShopCatalogSnapshot snapshot = Snapshot;
        pendingByShop = new Dictionary<ShopId, NpcShopCatalog>(snapshot.Count);
        pendingByArchetype = new Dictionary<GameplayArchetypeId, ShopId>(snapshot.Count);

        // Published catalogs are immutable. Reconstruct the mutable cold-path maps from the two indexed views.
        // Count is intentionally small/bounded by host registration policy, so this work never belongs to the tick hot path.
        // Iteration by ShopId is deterministic only for publication output; dictionary order is never externally observable.
        foreach (NpcShopCatalogEntry entry in CopyEntries(snapshot))
        {
            pendingByShop.Add(entry.ShopId, entry.Catalog);
            pendingByArchetype.Add(entry.NpcArchetypeId, entry.ShopId);
        }
    }

    private static NpcShopCatalogEntry[] CopyEntries(RuntimeNpcShopCatalogSnapshot snapshot)
    {
        if (snapshot.Count == 0)
            return [];

        var entries = new NpcShopCatalogEntry[snapshot.Count];
        int written = 0;
        // Snapshot intentionally exposes lookup rather than mutable enumeration. Shop IDs are not discoverable from
        // that API, so the registry retains a private copy via the snapshot's internal publication helper below.
        snapshot.CopyByShop(entries, ref written);
        return entries;
    }
}

public sealed class NpcShopRegistrationLease : IDisposable
{
    private RuntimeNpcShopCatalogRegistry? registry;
    private readonly ShopId shopId;
    private readonly GameplayArchetypeId archetypeId;

    internal NpcShopRegistrationLease(
        RuntimeNpcShopCatalogRegistry registry,
        ShopId shopId,
        GameplayArchetypeId archetypeId)
    {
        this.registry = registry;
        this.shopId = shopId;
        this.archetypeId = archetypeId;
    }

    public ShopId ShopId => shopId;
    public GameplayArchetypeId NpcArchetypeId => archetypeId;

    public bool TryReplaceCatalog(NpcShopCatalog catalog)
    {
        RuntimeNpcShopCatalogRegistry? owner = Volatile.Read(ref registry);
        return owner is not null && owner.TryReplace(shopId, archetypeId, catalog);
    }

    public void Dispose()
    {
        RuntimeNpcShopCatalogRegistry? owner = Interlocked.Exchange(ref registry, null);
        owner?.Retire(shopId, archetypeId);
    }
}

internal static class RuntimeNpcShopCatalogSnapshotExtensions
{
    public static void CopyByShop(
        this RuntimeNpcShopCatalogSnapshot snapshot,
        NpcShopCatalogEntry[] destination,
        ref int written)
    {
        ArgumentNullException.ThrowIfNull(destination);
        snapshot.CopyByShopCore(destination, ref written);
    }
}
