using System.Runtime.InteropServices;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldMutationPlan
    {
        // Equivalence is not admission. The caller still seals and validates ONE
        // chosen complete plan. Compare alternatives before acquiring item claims.
        internal bool HasSameSourceEffects(ToolWorldMutationPlan other) =>
            ReferenceEquals(owner, other.owner) && !failed && !adopted && !disposed &&
            !other.failed && !other.adopted && !other.disposed &&
            npcBeforeSerial == other.npcBeforeSerial && HasSameLarvaBosses(other) && HasSameContainerMetadata(other) &&
            beforeTreeShakeCount == other.beforeTreeShakeCount && beforeTreeShakeRevision == other.beforeTreeShakeRevision &&
            SourceRandom.HasSameState(other.SourceRandom) && Overlay.HasSameAfterimage(other.Overlay) &&
            Mutations.SequenceEqual(other.Mutations) && ShapeEffects.SequenceEqual(other.ShapeEffects) &&
            BreakOutcomes.SequenceEqual(other.BreakOutcomes) &&
            CollectionsMarshal.AsSpan(publicationOrder).SequenceEqual(CollectionsMarshal.AsSpan(other.publicationOrder)) &&
            CollectionsMarshal.AsSpan(WireEffects).SequenceEqual(CollectionsMarshal.AsSpan(other.WireEffects)) &&
            CollectionsMarshal.AsSpan(treeShakeMarks).SequenceEqual(CollectionsMarshal.AsSpan(other.treeShakeMarks)) &&
            (allocation is null ? other.allocation is null : other.allocation is not null && allocation.HasSameSourceEffects(other.allocation)) &&
            (npcSpawns is null ? other.npcSpawns is null : other.npcSpawns is not null && npcSpawns.HasSameSourceEffects(other.npcSpawns)) &&
            (projectileSpawns is null ? other.projectileSpawns is null : other.projectileSpawns is not null && projectileSpawns.HasSameSourceEffects(other.projectileSpawns));
    }
}
