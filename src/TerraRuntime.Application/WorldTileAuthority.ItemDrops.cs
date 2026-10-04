using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private ItemDropRandomPreview CreateItemDropRandomPreview() => new(worldItemSpawnRandom);

    private sealed class ItemDropRandomPreview
    {
        private readonly VanillaUnifiedRandom1458? live;
        private readonly VanillaUnifiedRandom1458? before;
        private readonly VanillaUnifiedRandom1458? after;
        public IWorldItemSpawnRandom Random { get; }

        public ItemDropRandomPreview(IWorldItemSpawnRandom source)
        {
            live = (source as SystemWorldItemSpawnRandom)?.SourceRandom;
            before = live?.Clone(); after = live?.Clone();
            // Explicit custom RNG adapters retain their prior callback policy; they are outside cloned-stream admission.
            Random = after is null ? source : new SystemWorldItemSpawnRandom(after);
        }

        public bool IsCurrent => live is null || live.HasSameState(before!);
        public void Commit()
        {
            if (!IsCurrent) throw new InvalidOperationException("Prepared tile item RNG changed before accepted publication.");
            if (after is not null) live!.CopyStateFrom(after);
        }
    }

    private RuntimeWorldItemStore.AllocationPreview CreateItemAllocationPreview()
    {
        Span<WorldItemAllocationPlayer1458> views = stackalloc WorldItemAllocationPlayer1458[byte.MaxValue];
        int count = 0;
        for (int slot = 0; slot < byte.MaxValue; slot++)
        {
            if (!players.TryCaptureCombatTarget((byte)slot, out var player) || !player.Player.IsAssigned) continue;
            var size = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) :
                (VanillaPlayerHitboxFacts.BaseWidth, VanillaPlayerHitboxFacts.BaseHeight);
            views[count++] = new((byte)slot, player.PositionX, player.PositionY, (int)size.Item1, (int)size.Item2);
        }
        return worldItems.CreateAllocationPreview(views[..count]);
    }

    private bool TryPrepareItemDrop(in WorldItemDropStateUpdate drop, out RuntimeWorldItemStore.AllocationPreview allocation)
    {
        allocation = CreateItemAllocationPreview();
        if (allocation.TrySpawnSource(in drop, 0, out _) && allocation.TryClaim()) return true;
        allocation.Dispose(); return false;
    }

    private static void CommitItemDrop(RuntimeWorldItemStore.AllocationPreview allocation)
    {
        if (!allocation.TryCommitNext(out _, out _))
            throw new InvalidOperationException("Prepared source item allocation changed after authoritative tile mutation.");
    }
}
