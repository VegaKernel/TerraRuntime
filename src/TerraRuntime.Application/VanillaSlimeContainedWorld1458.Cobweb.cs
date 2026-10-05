using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class VanillaSlimeContainedWorld1458
{
    private sealed partial class CapturedWorld
    {
        private const int MinimumSourceLiquidCapacity = 5000;
        private const int SpiderWall = 62;
        private const int MaximumCobwebFrameCalls = 4096;
        private const int SourceReframeDepthLimit = 25;
        private enum CobwebOperationKind : byte { Tile, Liquid, Item, Kill }
        private readonly record struct CobwebOperation(CobwebOperationKind Kind, int X, int Y,
            WorldTile Tile = default, byte? FrameNumber = null);
        private readonly List<CobwebOperation> webOperations = [];
        private readonly Dictionary<(int X, int Y), WorldTile> webWorking = [];
        private readonly Dictionary<(int X, int Y), byte> webBanks = [];
        private readonly Dictionary<(int X, int Y), byte> webBankClaims = [];
        private readonly Dictionary<(int X, int Y), bool> webLiquidClaims = [];
        private readonly Dictionary<WorldSectionId, long> webSectionClaims = [];
        private readonly HashSet<(int X, int Y)> webLiquidAdds = [];
        private RuntimeWorldItemStore.AllocationPreview? webAllocation;
        private (int Active, int Buffered)? webLiquidCounts;
        private (int X, int Y) webCenter;
        private bool webProtectionClaim;
        private int webFrameCalls;
        private int webReframeDepth;
        private bool webReadsCurrent = true;

        private bool CobwebPlanIsCurrent()
        {
            if (!webReadsCurrent) return false;
            foreach (var section in webSectionClaims)
                if (tiles.GetSectionVersion(section.Key) != section.Value) return false;
            if (webAllocation?.IsCurrent == false || webProtectionClaim && !HasKnownClearTileProtection()) return false;
            if (webLiquidCounts is { } counts &&
                (tiles.LiquidUpdates.ActiveCount != counts.Active || tiles.LiquidUpdates.BufferedCount != counts.Buffered)) return false;
            foreach (var claim in webLiquidClaims)
                if (tiles.LiquidUpdates.IsCheckingLiquid(claim.Key.X, claim.Key.Y) != claim.Value) return false;
            foreach (var claim in webBankClaims)
            {
                bool known = claim.Key == webCenter
                    ? tiles.TryGetCobwebPlacementFrameNumber(claim.Key.X, claim.Key.Y, out byte current)
                    : tiles.TryGetCobwebFrameNumber(claim.Key.X, claim.Key.Y, out current);
                if (!known || current != claim.Value) return false;
            }
            return true;
        }

        public bool TryClaimTileProducer() => IsCurrent && (webAllocation is null || webAllocation.TryClaim());
        public void CancelTileProducer() { webAllocation?.Dispose(); webAllocation = null; }

        private bool TryPlanCobwebSquare(int x, int y, in WorldTile before, IVanillaNpcRandom random)
        {
            // A skipped center would preserve an unknown pre-placement frameNumber. Its reset must be owned.
            if (!FrameInterior(x, y)) return false;
            webCenter = (x, y);
            var placed = before;
            placed.Type = (ushort)VanillaTileIds.Cobweb.Value; placed.FrameX = placed.FrameY = 0;
            placed.Shape = 0; placed.TileColor = 0;
            placed.Flags = (placed.Flags & ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock)) | WorldTileFlags.Active;
            StageWebTile(x, y, in placed);
            return PlanWebSquare(x, y, random);
        }

        private bool FrameInterior(int x, int y) => x > 5 && y > 5 &&
            x < tiles.Dimensions.WidthTiles - 5 && y < tiles.Dimensions.HeightTiles - 5;

        private WorldTile ReadWebTile(int x, int y)
        {
            // Recursive cosmetics can leave the actor's initial motion sections. Capture every
            // actual read section, including attachments that the accepted frame does not mutate.
            var section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, x, y);
            long version = tiles.GetSectionVersion(section);
            if ((version & 1) != 0 || webSectionClaims.TryGetValue(section, out long before) && before != version)
                webReadsCurrent = false;
            else webSectionClaims.TryAdd(section, version);
            return webWorking.TryGetValue((x, y), out var staged) ? staged : tiles.Get(x, y);
        }

        private void StageWebTile(int x, int y, in WorldTile cell, byte? bank = null)
        {
            if (bank is null && webBanks.TryGetValue((x, y), out byte retained)) bank = retained;
            webWorking[(x, y)] = cell;
            if (bank is { } number) webBanks[(x, y)] = number;
            webOperations.Add(new(CobwebOperationKind.Tile, x, y, cell, bank));
        }

        private bool PlanWebSquare(int x, int y, IVanillaNpcRandom random)
        {
            // Source SquareTileFrame: x-major, center reset in the fifth call, including recursive liquid deaths.
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                if (!PlanWebFrame(x + dx, y + dy, dx == 0 && dy == 0, random)) return false;
            return true;
        }

        private bool PlanWebFrame(int x, int y, bool reset, IVanillaNpcRandom random)
        {
            if (++webFrameCalls > MaximumCobwebFrameCalls) return false;
            if (!FrameInterior(x, y)) return true;
            var cell = ReadWebTile(x, y);
            if (!cell.IsActive)
            {
                cell.Shape = 0; cell.TileColor = 0;
                cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
                StageWebTile(x, y, in cell);
            }
            if (cell.LiquidAmount > 0)
            {
                if (!webLiquidClaims.TryGetValue((x, y), out bool checking))
                { checking = tiles.LiquidUpdates.IsCheckingLiquid(x, y); webLiquidClaims[(x, y)] = checking; }
                if (!checking && !webLiquidAdds.Contains((x, y)))
                {
                    // Other active objects have independent solidity, destruction and frame callbacks.
                    if (cell.IsActive && cell.Type != VanillaTileIds.Cobweb.Value) return false;
                    webLiquidCounts ??= (tiles.LiquidUpdates.ActiveCount, tiles.LiquidUpdates.BufferedCount);
                    if (webLiquidCounts.Value.Active + webLiquidAdds.Count >= MinimumSourceLiquidCapacity - 1) return false;
                    webLiquidAdds.Add((x, y)); webOperations.Add(new(CobwebOperationKind.Liquid, x, y));
                    if (cell.IsActive)
                    {
                        if (!PlanWebLiquidDeath(x, y, in cell, random)) return false;
                        cell = ReadWebTile(x, y);
                    }
                }
            }
            if (!cell.IsActive) return true;
            // Outer TileFrame can destroy or validate frame-important objects. Only the cosmetic
            // recursive function below owns their early return, not this outer callback.
            if (cell.Type != VanillaTileIds.Cobweb.Value) return false;
            return PlanWebCosmetic(x, y, reset, random);
        }

        private bool PlanWebCosmetic(int x, int y, bool reset, IVanillaNpcRandom random)
        {
            if (++webFrameCalls > MaximumCobwebFrameCalls) return false;
            if (x <= 0 || y <= 0 || x >= tiles.Dimensions.WidthTiles - 1 || y >= tiles.Dimensions.HeightTiles - 1) return true;
            var cell = ReadWebTile(x, y);
            if (!cell.IsActive) return true;
            if (!VanillaCobwebFrames1458.TryGetFrameImportant(cell.Type, out bool important)) return false;
            if (important) return true;
            if (cell.Type != VanillaTileIds.Cobweb.Value || cell.Shape != 0) return false;
            short oldFrameX = cell.FrameX, oldFrameY = cell.FrameY;
            byte bank;
            if (reset) { bank = (byte)random.NextInt32(0, 3); webBanks[(x, y)] = bank; }
            else if (!webBanks.TryGetValue((x, y), out bank))
            {
                bool known = (x, y) == webCenter
                    ? tiles.TryGetCobwebPlacementFrameNumber(x, y, out bank)
                    : tiles.TryGetCobwebFrameNumber(x, y, out bank);
                if (!known) return false;
                webBankClaims[(x, y)] = bank; webBanks[(x, y)] = bank;
            }
            Span<WorldTile> neighbors = stackalloc WorldTile[8];
            int index = 0;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dy != 0) neighbors[index++] = ReadWebTile(x + dx, y + dy);
            if (!VanillaCobwebFrames1458.TryGetAttachmentMask(neighbors, out byte mask,
                    (cell.Flags & WorldTileFlags.InvisibleBlock) != 0) ||
                !VanillaCobwebFrames1458.TryGetFrame(mask, bank, out cell.FrameX, out cell.FrameY)) return false;
            StageWebTile(x, y, in cell, bank);
            // Source depth-first cosmetic recursion: both coordinates must change; negative old
            // frames suppress the tail. The source limit is stack depth, separate from our plan budget.
            if (oldFrameX >= 0 && oldFrameY >= 0 && oldFrameX != cell.FrameX && oldFrameY != cell.FrameY)
            {
                webReframeDepth++;
                bool success = webReframeDepth >= SourceReframeDepthLimit ||
                    PlanWebCosmetic(x - 1, y, false, random) && PlanWebCosmetic(x + 1, y, false, random) &&
                    PlanWebCosmetic(x, y - 1, false, random) && PlanWebCosmetic(x, y + 1, false, random);
                webReframeDepth--;
                if (!success) return false;
            }
            return true;
        }

        private bool HasKnownClearTileProtection()
        {
            if (protectionNpcs is null) return false;
            for (int slot = 0; slot < protectionNpcs.Capacity; slot++)
                if (protectionNpcs.TryGetActive((byte)slot, out var npc) &&
                    (npc.Type is < 1 or > 696 or 43 or 56 or 101 or 175 or 259 or 260)) return false;
            // Exact SetDefaults AI013 identities. Their phase-owned ProtectSpot ledger is not yet represented;
            // neither higher slots nor previous-frame anchors are silently treated as already protected.
            // This known-clear profile requires AI013 to remain unadmitted. Before admitting those actors,
            // replace the census fence with the source Clear/ProtectSpot phase history, including actors
            // that died or transformed after an earlier protection write in this same physical pass.
            return true;
        }

        private bool PlanWebLiquidDeath(int x, int y, in WorldTile before, IVanillaNpcRandom random)
        {
            if (!HasKnownClearTileProtection() || random is not SystemVanillaNpcRandom trusted) return false;
            webProtectionClaim = true;
            bool drops = before.Wall != SpiderWall || random.NextInt32(0, 4) == 0;
            if (drops)
            {
                if (worldItems is null) return false;
                webAllocation ??= worldItems.CreateAllocationPreview();
                var drop = VanillaSimpleTileBreakResolver1458.MaterializeItemState(VanillaItemIds.Cobweb, 1,
                    x, y, new SystemWorldItemSpawnRandom(trusted.SourceRandom));
                if (!webAllocation.TrySpawnSource(in drop, 0, out _)) return false;
                webOperations.Add(new(CobwebOperationKind.Item, x, y));
            }
            var dead = before;
            dead.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            dead.Type = 0; dead.Shape = 0; dead.TileColor = 0; dead.FrameX = dead.FrameY = -1;
            webBanks[(x, y)] = 0;
            StageWebTile(x, y, in dead, 0);
            if (!PlanWebSquare(x, y, random)) return false;
            webOperations.Add(new(CobwebOperationKind.Kill, x, y));
            return true;
        }

        private void CommitCobwebPlan()
        {
            try
            {
                foreach (var operation in webOperations)
                {
                    switch (operation.Kind)
                    {
                        case CobwebOperationKind.Tile:
                            var cell = operation.Tile;
                            tiles.Set(operation.X, operation.Y, in cell);
                            if (operation.FrameNumber is { } bank)
                            {
                                bool retained = cell.IsActive
                                    ? tiles.TryRetainCobwebFrameNumber(operation.X, operation.Y, in cell, bank)
                                    : tiles.TryRetainCobwebPlacementFrameNumber(operation.X, operation.Y, in cell, bank);
                                if (!retained) throw new InvalidOperationException("An accepted cobweb frame claim changed during commit.");
                            }
                            break;
                        case CobwebOperationKind.Liquid:
                            if (!tiles.LiquidUpdates.TryEnqueue(operation.X, operation.Y))
                                throw new InvalidOperationException("An accepted cobweb liquid claim changed during commit.");
                            tiles.LiquidUpdates.ClearSkipNextUpdate(operation.X, operation.Y);
                            break;
                        case CobwebOperationKind.Item:
                            if (webAllocation?.TryCommitNext(out _, out _) != true)
                                throw new InvalidOperationException("An accepted cobweb item allocation changed during commit.");
                            break;
                        case CobwebOperationKind.Kill:
                            var state = new TerrariaTileManipulationState((byte)TerrariaTileManipulationAction.KillTile,
                                checked((short)operation.X), checked((short)operation.Y), 0, 0);
                            tileReplication?.TryPublishCommitted(GameCommandSourceId.System, in state);
                            break;
                    }
                }
            }
            finally { CancelTileProducer(); }
        }
    }
}
