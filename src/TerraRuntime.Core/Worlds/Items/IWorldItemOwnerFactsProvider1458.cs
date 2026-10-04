using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Worlds;

public enum WorldItemCreationSource1458 : byte
{
    NewItem = 0,
    ClientSynchronization = 1
}

/// <summary>One world's generation-owned player/inventory/terrain dependencies for source FindOwner.</summary>
public interface IWorldItemOwnerFactsProvider1458
{
    IWorldItemOwnerFactsSnapshot1458 Capture();
}

public interface IWorldItemOwnerFactsSnapshot1458
{
    bool IsCurrent { get; }
    bool TrySelectOwner(in WorldItemDropStateUpdate drop, int grabDelay, byte grabPlayer, out byte owner);
}
