using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct ToolWorldWireEffect1458(ToolWorldPublicationKind1458 Kind,
    int X, int Y, WorldTile Tile, TerrariaTreeEffectsState1458 Tree)
{
    internal TerrariaTileManipulationState Manipulation { get; init; }
    internal TerrariaChestUpdatesState1458 Chest { get; init; }
}

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldMutationPlan
    {
        internal readonly List<ToolWorldWireEffect1458> WireEffects = [];
        private readonly List<(int X, int Y)> treeShakeMarks = [];
        private int beforeTreeShakeCount;
        private long beforeTreeShakeRevision;
        private void CaptureTreeShakeBefore()
        {
            beforeTreeShakeCount = owner.sourceTreeShakeCount;
            beforeTreeShakeRevision = owner.sourceTreeShakeRevision;
            if (owner.sourceTreeShakeRevisionExhausted) failed = true;
        }
        private bool IsTreeShakeBeforeCurrent => !owner.sourceTreeShakeRevisionExhausted &&
            owner.sourceTreeShakeRevision == beforeTreeShakeRevision && owner.sourceTreeShakeCount == beforeTreeShakeCount;
        internal bool CanAttemptSourceTreeShake => beforeTreeShakeCount + treeShakeMarks.Count < MaximumSourceTreeShakes;
        internal bool TryReserveSourceTreeShake(int x, int y, out bool fresh)
        {
            fresh = false;
            if (!IsCurrentOwned || sealedPlan) return Fail();
            if (!CanAttemptSourceTreeShake) return true;
            for (int index = 0; index < beforeTreeShakeCount; index++)
                if (owner.sourceTreeShakes[index] == (x, y)) return true;
            if (treeShakeMarks.Contains((x, y))) return true;
            if (beforeTreeShakeRevision > long.MaxValue - treeShakeMarks.Count - 1) return Fail();
            treeShakeMarks.Add((x, y)); fresh = true;
            return true;
        }
        private void AdoptTreeShakeMarks()
        {
            foreach (var mark in treeShakeMarks)
            {
                owner.sourceTreeShakes[owner.sourceTreeShakeCount++] = mark;
                owner.sourceTreeShakeRevision++;
            }
        }
        internal bool TryStageSourceTileSquare(int x, int y)
        {
            if (!IsCurrentOwned || sealedPlan || publicationOrder.Count == ToolWorldTileOverlay1458.MaximumChangedCells ||
                !Overlay.TryRead(x, y, out var tile) || !TerrariaTileSquareCodec.TryEncodeSingle(in tile, x, y, out _)) return Fail();
            WireEffects.Add(new(ToolWorldPublicationKind1458.TileSquare, x, y, tile, default));
            publicationOrder.Add(ToolWorldPublicationKind1458.TileSquare);
            return true;
        }
        internal bool TryStageSourceTileManipulation(int x, int y, byte action, short data = 0)
        {
            if (!IsCurrentOwned || sealedPlan || publicationOrder.Count == ToolWorldTileOverlay1458.MaximumChangedCells ||
                !Overlay.TryRead(x, y, out var tile)) return Fail();
            var state = new TerrariaTileManipulationState(action, checked((short)x), checked((short)y), data, 0);
            WireEffects.Add(new(ToolWorldPublicationKind1458.TileManipulation, x, y, tile, default) { Manipulation = state });
            publicationOrder.Add(ToolWorldPublicationKind1458.TileManipulation);
            return true;
        }
        internal bool TryStageSourceContainerResponse(int x, int y, byte action, short chestId)
        {
            if (!IsCurrentOwned || sealedPlan || containerRemovals is null ||
                publicationOrder.Count == ToolWorldTileOverlay1458.MaximumChangedCells ||
                !Overlay.TryRead(x, y, out var tile)) return Fail();
            var state = new TerrariaChestUpdatesState1458(action, checked((short)x), checked((short)y), 0, chestId);
            WireEffects.Add(new(ToolWorldPublicationKind1458.ChestUpdate, x, y, tile, default) { Chest = state });
            publicationOrder.Add(ToolWorldPublicationKind1458.ChestUpdate);
            return true;
        }
        internal bool TryStageSourceTreeEffect(int x, int y, short style)
        {
            if (!IsCurrentOwned || sealedPlan || style < 0 || publicationOrder.Count == ToolWorldTileOverlay1458.MaximumChangedCells ||
                !Overlay.TryRead(x, y, out var tile)) return Fail();
            var effect = new TerrariaTreeEffectsState1458(TerrariaTreeEffectsCodec1458.ShakeKind, x, y, 1, style, 1);
            WireEffects.Add(new(ToolWorldPublicationKind1458.TreeEffect, x, y, tile, effect));
            publicationOrder.Add(ToolWorldPublicationKind1458.TreeEffect);
            return true;
        }
    }

    internal sealed partial class ToolWorldPublication1458
    {
        private WorldTileAuthority wireOwner = null!;
        private ToolWorldWireEffect1458[] wire = [];
        private WorldTile[] acceptedWireTargets = [];
        private int nextWire;
        private RuntimeChestStore.ToolObjectRemovalPreparation? acceptedContainers;
        private void CaptureWirePublication(ToolWorldMutationPlan source)
        {
            wireOwner = source.Owner;
            acceptedContainers = source.SourceContainerRemovals;
            wire = source.WireEffects.ToArray();
            acceptedWireTargets = new WorldTile[wire.Length];
            for (int index = 0; index < wire.Length; index++) acceptedWireTargets[index] = wireOwner.tiles!.Get(wire[index].X, wire[index].Y);
        }
        private bool TryPublishWire(ToolWorldPublicationKind1458 kind)
        {
            if (nextWire == wire.Length || wire[nextWire].Kind != kind) return false;
            int index = nextWire++;
            var effect = wire[index];
            if (wireOwner.tiles is null || !wireOwner.tiles.Get(effect.X, effect.Y).Equals(acceptedWireTargets[index])) return true;
            if (wireOwner.replication is null) return true;
            if (kind == ToolWorldPublicationKind1458.ChestUpdate)
                return acceptedContainers is not null && (!acceptedContainers.IsAcceptedImageCurrent ||
                    wireOwner.replication.TryPublishToolChestUpdate(effect.Chest));
            if (kind == ToolWorldPublicationKind1458.TileManipulation)
                return wireOwner.TryPublishToolTile(GameCommandSourceId.System, default(PlayerHandle), effect.Manipulation);
            if (kind == ToolWorldPublicationKind1458.TreeEffect)
                return wireOwner.replication.TryPublishToolTreeEffect(effect.Tree);
            return TerrariaTileSquareCodec.TryEncodeSingle(effect.Tile, effect.X, effect.Y, out byte[] encoded) &&
                wireOwner.replication.TryPublishToolTileSnapshot(wireOwner.tiles, effect.X, effect.Y, encoded);
        }
    }
}
