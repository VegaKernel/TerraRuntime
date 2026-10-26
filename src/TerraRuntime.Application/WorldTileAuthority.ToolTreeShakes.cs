namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    // Terraria1.4.5.8 WorldGen.maxTreeShakes: source transient world state,
    // cleared by Main.UpdateTime_StartDay and world initialization.
    private const int MaximumSourceTreeShakes = 500;
    private readonly (int X, int Y)[] sourceTreeShakes = new (int, int)[MaximumSourceTreeShakes];
    private int sourceTreeShakeCount;
    private long sourceTreeShakeRevision;
    private bool sourceTreeShakeRevisionExhausted;

    internal bool TryPrepareTreeShakeMark(int bottomX, int bottomY, out TreeShakeMarkPreparation? preparation)
    {
        preparation = null;
        if (!ContainsTile(bottomX, bottomY) || sourceTreeShakeRevisionExhausted || sourceTreeShakeRevision == long.MaxValue)
            return false;
        bool canShake = sourceTreeShakeCount < MaximumSourceTreeShakes;
        if (canShake)
            for (int index = 0; index < sourceTreeShakeCount; index++)
                if (sourceTreeShakes[index] == (bottomX, bottomY)) { canShake = false; break; }
        preparation = new(this, bottomX, bottomY, canShake);
        return true;
    }

    /// <summary>Called only for the committed authoritative StartDay transition, never from client time reports.</summary>
    internal void ResetSourceTreeShakesAtDawn()
    {
        if (sourceTreeShakeRevisionExhausted || sourceTreeShakeRevision == long.MaxValue)
        {
            sourceTreeShakeRevisionExhausted = true;
            return;
        }
        sourceTreeShakeRevision++;
        sourceTreeShakeCount = 0;
    }

    internal sealed class TreeShakeMarkPreparation
    {
        private readonly WorldTileAuthority owner;
        private readonly long revision;
        private readonly int count;
        private readonly int x;
        private readonly int y;
        private bool adopted;

        internal TreeShakeMarkPreparation(WorldTileAuthority owner, int x, int y, bool canShake)
        {
            this.owner = owner;
            this.x = x;
            this.y = y;
            revision = owner.sourceTreeShakeRevision;
            count = owner.sourceTreeShakeCount;
            CanShake = canShake;
        }

        public bool CanShake { get; }
        public bool IsCurrent => !adopted && !owner.sourceTreeShakeRevisionExhausted &&
            owner.sourceTreeShakeRevision == revision && owner.sourceTreeShakeCount == count && revision < long.MaxValue;

        public bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            adopted = true;
            if (CanShake)
            {
                owner.sourceTreeShakes[count] = (x, y);
                owner.sourceTreeShakeCount++;
                owner.sourceTreeShakeRevision++;
            }
            return true;
        }
    }
}
