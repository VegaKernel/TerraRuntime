using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application.Bots;

internal enum RuntimeBotResourceKind : byte { WorldItem, TileTarget }
internal readonly record struct RuntimeBotResourceKey(
    WorldRuntimeIdentity World, RuntimeBotResourceKind Kind, WorldItemHandle Item, int X, int Y)
{
    public static RuntimeBotResourceKey ForItem(WorldRuntimeIdentity world, WorldItemHandle item) =>
        new(world, RuntimeBotResourceKind.WorldItem, item, 0, 0);
    public bool IsValid => World.IsAssigned && Kind switch
    {
        RuntimeBotResourceKind.WorldItem => Item.IsAssigned && X == 0 && Y == 0,
        RuntimeBotResourceKind.TileTarget => !Item.IsAssigned && X >= 0 && Y >= 0,
        _ => false
    };
}
internal readonly record struct RuntimeBotLeaseOwner(int BotId, PlayerHandle Player, WorldRuntimeIdentity World)
{
    public bool IsAssigned => BotId > 0 && Player.IsAssigned && World.IsAssigned;
}
internal readonly record struct RuntimeBotResourceLease(
    RuntimeBotLeaseOwner Owner, RuntimeBotResourceKey Resource, long IssuedAtTick, long ExpiresAtTick);

/// <summary>Single-writer, bounded, generation-scoped coordination. A lease never grants mutation permission.</summary>
internal sealed class RuntimeBotResourceLeases
{
    internal const long DefaultTtlTicks = 180;
    internal const long MaximumTtlTicks = 3_600;
    private const int Capacity = 1_024;
    private readonly Dictionary<RuntimeBotResourceKey, RuntimeBotResourceLease> leases = [];
    private readonly List<RuntimeBotResourceKey> expired = [];
    public int Count => leases.Count;

    public bool IsAvailable(RuntimeBotLeaseOwner owner, RuntimeBotResourceKey resource, long tick) =>
        owner.IsAssigned && resource.IsValid && owner.World == resource.World &&
        (!leases.TryGetValue(resource, out var lease) || tick >= lease.ExpiresAtTick || lease.Owner == owner);

    public bool TryAcquire(RuntimeBotLeaseOwner owner, RuntimeBotResourceKey resource, long tick,
        long ttl = DefaultTtlTicks)
    {
        if (!owner.IsAssigned || !resource.IsValid || owner.World != resource.World || tick < 0 ||
            ttl <= 0 || ttl > MaximumTtlTicks || tick > long.MaxValue - ttl) return false;
        Cleanup(tick);
        if (leases.TryGetValue(resource, out var current))
        {
            if (current.Owner != owner || tick < current.IssuedAtTick) return false;
            leases[resource] = current with { ExpiresAtTick = tick + ttl };
            return true;
        }
        if (leases.Count >= Capacity) return false;
        leases.Add(resource, new(owner, resource, tick, tick + ttl));
        return true;
    }

    public bool Release(RuntimeBotLeaseOwner owner, RuntimeBotResourceKey resource) =>
        leases.TryGetValue(resource, out var current) && current.Owner == owner && leases.Remove(resource);

    public void ReleaseBot(RuntimeBotLeaseOwner owner)
    {
        expired.Clear();
        foreach (var pair in leases)
            if (pair.Value.Owner == owner) expired.Add(pair.Key);
        foreach (var key in expired) leases.Remove(key);
    }

    public void Cleanup(long tick)
    {
        expired.Clear();
        foreach (var pair in leases)
            if (tick >= pair.Value.ExpiresAtTick) expired.Add(pair.Key);
        foreach (var key in expired) leases.Remove(key);
    }
}
