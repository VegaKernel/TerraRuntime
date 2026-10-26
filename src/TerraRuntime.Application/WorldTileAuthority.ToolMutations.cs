using System.Runtime.InteropServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal enum ToolWorldShapeOperation1458 : byte { None, Slope, Pound }
internal readonly record struct ToolWorldShapeEffect1458(int X, int Y, byte BeforeShape, byte FinalShape)
{
    public ToolWorldShapeOperation1458 Operation { get; init; }
    // Qualified final raw source predicate: SlopeTile tests slope==0,
    // PoundTile tests !halfBrick. Normalized Shape==0 is insufficient.
    public bool RequiresPlayerLift { get; init; }
}
internal readonly record struct ToolWorldMutationStep1458(
    WorldTileMutationKind Kind, int X, int Y, WorldTileMutationResult Result);

internal readonly record struct ToolWorldPublicationOffset1458(int End, int Items, int Npcs, int Projectiles);
internal enum ToolWorldPublicationKind1458 : byte { Item, Npc, Projectile, TileSquare, TreeEffect, TileManipulation, ChestUpdate, LarvaBoss }

internal sealed partial class WorldTileAuthority
{
    internal bool TryCreateToolMutationPlan(ToolWorldTileOverlay1458 overlay, out ToolWorldMutationPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        plan = null;
        if (tiles is null || mutations is null || !ReferenceEquals(tiles, overlay.Tiles) ||
            worldItemSpawnRandom is not SystemWorldItemSpawnRandom || !overlay.IsCurrentOwned)
            return false;
        var candidate = new ToolWorldMutationPlan(this, overlay);
        if (ToolProjectileOwner is { } projectileOwner && !candidate.TryBindSourceProjectiles(projectileOwner))
        { candidate.Dispose(); return false; }
        if (!candidate.IsCurrentOwned) { candidate.Dispose(); return false; }
        plan = candidate;
        return true;
    }

    /// <summary>
    /// One complete source action: one virtual world image, one cloned world
    /// Main.rand/genRand alias stream and one lazy source allocation preview.
    /// Repeated shovel/tree steps are staged here, never independently committed.
    /// Player/token/health/metadata participants are prepared by the enclosing
    /// transaction and all adopt before any returned item or tile publication.
    /// </summary>
    internal sealed partial class ToolWorldMutationPlan : IDisposable
    {
        private readonly WorldTileAuthority owner;
        private readonly ItemDropRandomPreview random;
        private readonly ulong npcBeforeSerial;
        private RuntimeWorldItemStore.AllocationPreview? allocation;
        private ToolTreeActor1458[]? sourceActors;
        private RuntimeNpcStore.PreparedWorldSpawns? npcSpawns;
        private RuntimeProjectileStore.WorldSpawnPreparation? projectileSpawns;
        private RuntimeProjectileStore? projectileStore;
        private RuntimeFallingBlockProjectiles.SourceBirthPreparation? fallingBirths;
        private readonly List<ToolWorldPublicationKind1458> publicationOrder = [];
        private readonly List<ToolWorldMutationStep1458> mutations = [];
        private readonly List<ToolWorldShapeEffect1458> shapes = [];
        private readonly List<VanillaSimpleTileBreakOutcome> outcomes = [];
        private int dropCount;
        private int allocatedDropCount;
        private bool failed;
        private bool sealedPlan;
        private bool adopted;
        private bool disposed;

        internal ToolWorldMutationPlan(WorldTileAuthority owner, ToolWorldTileOverlay1458 overlay)
        {
            this.owner = owner;
            Overlay = overlay;
            random = owner.CreateItemDropRandomPreview();
            if (!owner.npcs.TryCaptureDeathMutationSerial(out npcBeforeSerial)) failed = true;
            CaptureTreeShakeBefore();
        }

        public ToolWorldTileOverlay1458 Overlay { get; }
        internal WorldTileAuthority Owner => owner;
        public IWorldItemSpawnRandom Random => random.Random;
        internal RuntimeProjectileStore? SourceProjectiles => projectileStore;
        internal bool TryBindSourceProjectiles(RuntimeProjectileStore store)
        {
            if (!IsCurrentOwned || sealedPlan || (projectileStore is not null && !ReferenceEquals(projectileStore, store))) return Fail();
            projectileStore ??= store;
            projectileSpawns ??= store.CreateWorldSpawnPreparation();
            return IsCurrentOwned;
        }
        internal VanillaUnifiedRandom1458 SourceRandom => ((SystemWorldItemSpawnRandom)random.Random).SourceRandom;
        internal bool TrySetSourceActorView(RuntimeProjectileStore.SimulationPreparation phaseActor) =>
            !sealedPlan && IsCurrentOwned && phaseActor.IsCurrent &&
            (projectileSpawns is null || projectileSpawns.TrySetSourceActorView(phaseActor)) && IsCurrentOwned;
        internal bool TrySetSourceActorView(in ProjectileSnapshot expected, in ProjectileStateUpdate update,
            in ProjectileLifecycleState lifecycle, bool sourceActive) =>
            !sealedPlan && IsCurrentOwned &&
            (projectileSpawns is null || projectileSpawns.TrySetSourceActorView(in expected, in update, in lifecycle, sourceActive)) && IsCurrentOwned;
        internal bool TryBindSourceActor(RuntimeProjectileStore.SimulationPreparation actor) =>
            sealedPlan && IsCurrentOwned && actor.IsCurrent &&
            (projectileSpawns is null || projectileSpawns.TryBindSourceActor(actor)) && IsCurrentOwned;

        internal bool TryBindSourceActors(ReadOnlySpan<ToolTreeActor1458> actors)
        {
            if (!IsCurrentOwned || sealedPlan || actors.Length > byte.MaxValue) return Fail();
            if (sourceActors is not null) return sourceActors.AsSpan().SequenceEqual(actors) || Fail();
            if (allocation is not null) return Fail();
            sourceActors = actors.ToArray();
            return true;
        }

        private bool TryCreateSourceAllocation()
        {
            if (allocation is not null) return true;
            if (sourceActors is null) { allocation = owner.CreateItemAllocationPreview(); return true; }
            if (owner.worldItems.SourceOwnerFactsProvider is not RuntimeWorldItemOwnerFactsProvider1458 provider ||
                !provider.TryCaptureToolProjection(owner.players, Overlay.Tiles, Overlay, sourceActors, out var ownerFacts) ||
                ownerFacts is null) return Fail();
            var views = new List<WorldItemAllocationPlayer1458>();
            foreach (var actor in sourceActors)
                if (actor.Active) views.Add(new(actor.Slot, actor.X, actor.Y, actor.Width, actor.Height));
            // Keep active dead actors for visibility and owner-presence checks.
            allocation = owner.worldItems.CreateProjectedAllocationPreview(CollectionsMarshal.AsSpan(views), ownerFacts);
            return true;
        }

        public ToolWorldPublicationOffset1458 CapturePublicationOffset() =>
            new(publicationOrder.Count, dropCount, npcSpawns is null ? 0 : npcSpawns.Births.Length, projectileSpawns is null ? 0 : projectileSpawns.Births.Length);

        public ReadOnlySpan<ToolWorldMutationStep1458> Mutations => CollectionsMarshal.AsSpan(mutations);
        public ReadOnlySpan<ToolWorldShapeEffect1458> ShapeEffects => CollectionsMarshal.AsSpan(shapes);
        public ReadOnlySpan<VanillaSimpleTileBreakOutcome> BreakOutcomes => CollectionsMarshal.AsSpan(outcomes);

        public bool TryStage(in WorldTileMutationRequest request, out WorldTileMutationResult result) =>
            TryStage(in request, ToolWorldShapeOperation1458.None, false, out result);

        public bool TryStage(in WorldTileMutationRequest request,
            ToolWorldShapeOperation1458 shapeOperation, bool sourceRequiresPlayerLift,
            out WorldTileMutationResult result)
        {
            result = default;
            if (failed || sealedPlan || adopted || disposed || mutations.Count == ToolWorldTileOverlay1458.MaximumChangedCells ||
                shapeOperation is not (ToolWorldShapeOperation1458.None or ToolWorldShapeOperation1458.Slope or ToolWorldShapeOperation1458.Pound) ||
                (request.Kind is WorldTileMutationKind.SetShape or WorldTileMutationKind.SetToolShape) != (shapeOperation != ToolWorldShapeOperation1458.None) ||
                (sourceRequiresPlayerLift && shapeOperation == ToolWorldShapeOperation1458.None))
                return Fail();
            Span<(int X, int Y, WorldTile Tile)> before = stackalloc (int, int, WorldTile)[9];
            if (!Overlay.TryCaptureMutationBefore(request.X, request.Y, before, out int count) ||
                !owner.mutations!.TryPrepareToolPrimitive(in request, before[..count], out var world, out result) || world is null)
                return Fail();

            if (request.Kind == WorldTileMutationKind.KillTile)
            {
                if (!VanillaTileDefinitionCatalog.TryGet(result.Before.TileType, out var definition)) return Fail();
                bool cordage = definition.ContextualDropKind == VanillaTileContextualDropKind.CordageVine &&
                    owner.players.ClosestPlayerHasFunctionalItem(request.X, request.Y, VanillaItemIds.GuideToPlantFiberCordage);
                var outcome = VanillaSimpleTileBreakResolver1458.Resolve(definition, request.X, request.Y, cordage, Random);
                if (outcome.DropStatus == VanillaTileDropResolutionStatus.WrongPath) return Fail();
                outcomes.Add(outcome);
                if (outcome.HasDrop && !TryStageDrop(outcome.Drop)) return false;
            }
            else if (request.Kind == WorldTileMutationKind.KillWall)
            {
                var wall = result.Before.WallType;
                bool skeletron = owner.skeletronDownedBaseline || owner.progression.IsCompleted(VanillaWorldProgressionId.Skeletron);
                bool golem = owner.golemDownedBaseline || owner.progression.IsCompleted(VanillaWorldProgressionId.Golem);
                if (!VanillaWallBreakRules1458.CanPlayerSmashWall(owner.tiles!, request.X, request.Y) ||
                    !VanillaWallBreakRules1458.IsProgressionUnlocked(wall, skeletron, golem) ||
                    !VanillaWallDropCatalog1458.TryResolve(wall, out var item)) return Fail();
                if (!item.IsNone)
                {
                    var drop = VanillaSimpleTileBreakResolver1458.MaterializeItemState(item, 1, request.X, request.Y, Random);
                    if (!TryStageDrop(in drop)) return false;
                }
            }
            if (!Overlay.TryStageChanges(world.Changes)) return Fail();
            mutations.Add(new(request.Kind, request.X, request.Y, result));
            if (request.Kind is WorldTileMutationKind.SetShape or WorldTileMutationKind.SetToolShape)
                shapes.Add(new(request.X, request.Y, result.Before.Shape, result.After.Shape)
                { Operation = shapeOperation, RequiresPlayerLift = sourceRequiresPlayerLift });
            return IsCurrentOwned;
        }

        internal bool TryStageSourceCell(WorldTileMutationKind kind, int x, int y,
            in WorldTile before, in WorldTile after)
        {
            if (failed || sealedPlan || adopted || disposed || mutations.Count == ToolWorldTileOverlay1458.MaximumChangedCells ||
                !Overlay.TryRead(x, y, out var current) || !current.Equals(before) || !Overlay.TryStage(x, y, in after))
                return Fail();
            var dimensions = Overlay.Tiles.Dimensions;
            var result = new WorldTileMutationResult(WorldTileMutationStatus.Applied, before, after,
                Math.Max(0, x - 1), Math.Max(0, y - 1), Math.Min(dimensions.WidthTiles - 1, x + 1),
                Math.Min(dimensions.HeightTiles - 1, y + 1), before.Equals(after) ? 0 : 1);
            mutations.Add(new(kind, x, y, result));
            return IsCurrentOwned;
        }

        internal bool TryStageSourceSimpleDrop(int x, int y, in WorldTile before)
        {
            if (!VanillaTileDefinitionCatalog.TryGet(before.TileType, out var definition)) return Fail();
            bool cordage = definition.ContextualDropKind == VanillaTileContextualDropKind.CordageVine &&
                owner.players.ClosestPlayerHasFunctionalItem(x, y, VanillaItemIds.GuideToPlantFiberCordage);
            var outcome = VanillaSimpleTileBreakResolver1458.Resolve(definition, x, y, cordage, Random);
            if (outcome.DropStatus == VanillaTileDropResolutionStatus.WrongPath) return Fail();
            outcomes.Add(outcome);
            return !outcome.HasDrop || TryStageDrop(outcome.Drop);
        }

        internal bool TryStageSourceItem(ItemTypeId item, int stack, int x, int y)
        {
            if (item.IsNone || stack <= 0 || stack > ushort.MaxValue) return Fail();
            var drop = VanillaSimpleTileBreakResolver1458.MaterializeItemState(item, (ushort)stack, x, y, Random);
            return TryStageDrop(in drop);
        }

        internal void RecordSourceShape(int x, int y, in WorldTile before, in WorldTile after,
            ToolWorldShapeOperation1458 operation, bool requiresPlayerLift) =>
            shapes.Add(new(x, y, before.Shape, after.Shape)
            { Operation = operation, RequiresPlayerLift = requiresPlayerLift });

        internal bool TryCountSourceNpcs(int type, out int count)
        {
            count = 0;
            if (type <= 0 || !IsCurrentOwned) return false;
            if (npcSpawns is not null) return npcSpawns.TryCountSourceType(type, out count) && IsCurrentOwned;
            for (int slot = 0; slot < Math.Min(owner.npcs.Capacity, VanillaNpcSpawnRules.PhysicalSlotCount); slot++)
                if (owner.npcs.TryGetActive((byte)slot, out var npc) && npc.TypeIdentity.Value == type) count++;
            return IsCurrentOwned;
        }

        internal bool TryStageSourceNpc(in NpcAiSpawnIntent intent, out bool allocated, out NpcSnapshot birth)
        {
            allocated = false; birth = default;
            if (!IsCurrentOwned || sealedPlan || publicationOrder.Count == ToolWorldTileOverlay1458.MaximumChangedCells) return Fail();
            if (npcSpawns is null)
            {
                var live = ((SystemWorldItemSpawnRandom)owner.worldItemSpawnRandom).SourceRandom;
                if (!owner.npcs.TryPrepareWorldSpawns(live, SourceRandom, CaptureSourceNpcWet, out npcSpawns) || npcSpawns is null) return Fail();
            }
            if (!npcSpawns.TryStage(in intent, out allocated, out birth)) return Fail();
            if (allocated) publicationOrder.Add(ToolWorldPublicationKind1458.Npc);
            return IsCurrentOwned;
        }

        internal bool TryFinishSourceNpc(in NpcSnapshot expected, float velocityX, float velocityY,
            NpcAiState ai, bool replaceable, bool wet, out NpcSnapshot final)
        {
            final = default;
            return npcSpawns is not null && npcSpawns.TryFinish(in expected, velocityX, velocityY,
                ai, replaceable, wet, out final) && IsCurrentOwned;
        }

        internal bool TryHasSourceFallingDuplicate(ProjectileTypeId type, float centerX, float centerY, out bool duplicate)
        {
            duplicate = false;
            return projectileSpawns is not null && projectileSpawns.TryHasProjectedFallingDuplicate(type, centerX, centerY, out duplicate);
        }
        internal bool TryFinishSourceProjectile(in ProjectileSnapshot expected, in ProjectileStateUpdate after,
            out ProjectileSnapshot final)
        {
            final = default;
            return projectileSpawns is not null && projectileSpawns.TryFinishSourceCall(in expected, in after, out final) && IsCurrentOwned;
        }

        internal bool TryStageSourceProjectile(RuntimeProjectileStore store, in ProjectileStateUpdate state,
            bool npcProjectile, out ProjectileSnapshot birth)
        {
            birth = default;
            if (!IsCurrentOwned || sealedPlan || publicationOrder.Count == ToolWorldTileOverlay1458.MaximumChangedCells ||
                (projectileStore is not null && !ReferenceEquals(projectileStore, store))) return Fail();
            projectileStore ??= store;
            projectileSpawns ??= store.CreateWorldSpawnPreparation();
            if (!projectileSpawns.TryStage(in state, npcProjectile, out birth)) return Fail();
            publicationOrder.Add(ToolWorldPublicationKind1458.Projectile);
            return IsCurrentOwned;
        }

        /// <summary>Stages one source NewItem in order; all source calls share the same preview.</summary>
        public bool TryStageDrop(in WorldItemDropStateUpdate drop)
        {
            if (failed || sealedPlan || adopted || disposed || dropCount == ToolWorldTileOverlay1458.MaximumChangedCells ||
                publicationOrder.Count == ToolWorldTileOverlay1458.MaximumChangedCells)
                return Fail();
            if (!TryCreateSourceAllocation() || allocation is null) return Fail();
            var sourceDrop = sourceDefaultItemOwner.HasValue && drop.Ownership == WorldItemOwnershipMode.None
                ? drop with { Ownership = WorldItemOwnershipMode.ReserveForLocalPlayer, EnemyGrabDelayTime = 100 }
                : drop;
            if (!allocation.TrySpawnSource(in sourceDrop, 0, out short allocatedSlot, sourceDefaultItemOwner)) return Fail();
            // Sentinel400 is a source publication, not an addressable allocation.
            if ((uint)allocatedSlot < RuntimeWorldItemStore.VanillaCapacity) allocatedDropCount++;
            dropCount++;
            publicationOrder.Add(ToolWorldPublicationKind1458.Item);
            return IsCurrentOwned;
        }

        public bool TrySeal()
        {
            if (failed || adopted || disposed) return false;
            if (sealedPlan) return IsCurrentOwned;
            if (projectileSpawns is not null && owner.ToolFallingBlocks is { } falling &&
                !falling.TryPrepareSourceBirths(projectileSpawns, out fallingBirths)) return Fail();
            if (furniture is not null && !Overlay.TryPrepareMetadata(furniture)) return Fail();
            if (allocation is not null && !allocation.TryClaim()) return Fail();
            sealedPlan = true;
            return ValidateExternalOwners();
        }

        // May query item owner providers. Complete these before final pure
        // player/localRNG/tool/world guards; providers never run during adoption.
        public bool ValidateExternalOwners() => sealedPlan && !failed && !disposed && !adopted &&
            (npcSpawns is null || npcSpawns.ValidateContext()) &&
            (allocation is null || allocation.IsCurrent) && AreLarvaBossOwnersCurrent && IsCurrentOwned;

        public bool IsCurrentOwned => !failed && !disposed && !adopted && Overlay.IsCurrentOwned &&
            random.IsCurrent && IsFurnitureCurrent && AreLarvaBossOwnersCurrent && IsTreeShakeBeforeCurrent && IsContainerMetadataCurrent && owner.npcs.TryCaptureDeathMutationSerial(out ulong currentNpcSerial) &&
            currentNpcSerial == npcBeforeSerial && (allocation is null || allocation.IsCurrentOwned) &&
            (npcSpawns is null || npcSpawns.IsCurrentOwned) &&
            (projectileSpawns is null || projectileSpawns.IsCurrentOwned) &&
            (fallingBirths is null || fallingBirths.IsCurrentOwned);

        public bool TryAdoptUnpublished(out RuntimeWorldItemStore.AllocationPreview.AllocationPublication? publication)
        {
            publication = null;
            if (npcSpawns is not null && !npcSpawns.Births.IsEmpty ||
                projectileSpawns is not null && !projectileSpawns.Births.IsEmpty) return false;
            if (!TryAdoptWholeUnpublished(out var whole) || whole is null) return false;
            publication = whole.Items;
            return true;
        }

        public bool TryAdoptWholeUnpublished(out ToolWorldPublication1458? publication)
        {
            publication = null;
            if (!sealedPlan || !IsCurrentOwned) return false;
            RuntimeWorldItemStore.AllocationPreview.AllocationPublication? items = null;
            RuntimeNpcStore.WorldSpawnPublication? npcs = null;
            RuntimeProjectileStore.WorldSpawnPreparation.WorldSpawnPublication? projectiles = null;
            if (npcSpawns is not null && !npcSpawns.TryAdoptUnpublished(out npcs)) return false;
            if (projectileSpawns is not null && !projectileSpawns.TryAdoptUnpublished(out projectiles))
                throw new InvalidOperationException("Prepared world projectile table changed during callback-free adoption.");
            if (fallingBirths is not null && !fallingBirths.TryAdoptUnpublished())
                throw new InvalidOperationException("Prepared source falling provenance changed during callback-free adoption.");
            if (allocation is not null && !allocation.TryAdoptUnpublished(out items))
                throw new InvalidOperationException("Prepared world item table changed during callback-free adoption.");
            AdoptContainerMetadata();
            if (!Overlay.TryAdoptUnpublished(furniture))
                throw new InvalidOperationException("Prepared tool overlay changed during callback-free adoption.");
            AdoptTreeShakeMarks();
            random.Commit();
            AdoptLarvaBosses();
            // Geometry wakeups/dirty frames follow the source mutation sequence,
            // after final physical adoption and before any observer can reenter.
            foreach (var mutation in mutations)
            {
                if (mutation.Kind is not (WorldTileMutationKind.KillWall or WorldTileMutationKind.PlaceWall))
                    VanillaWorldLiquidWakeup1458.WakeCellAndNeighbours(owner.tiles!, mutation.X, mutation.Y);
                var bounds = mutation.Result;
                var first = TerrariaSectionGeometry.FromTile(owner.tiles!.Dimensions, bounds.FrameMinX, bounds.FrameMinY);
                var last = TerrariaSectionGeometry.FromTile(owner.tiles.Dimensions, bounds.FrameMaxX, bounds.FrameMaxY);
                for (int sectionX = first.X; sectionX <= last.X; sectionX++)
                    for (int sectionY = first.Y; sectionY <= last.Y; sectionY++)
                    {
                        var section = new WorldSectionId(sectionX, sectionY);
                        owner.tiles.DirtySections.MarkDirty(section);
                        owner.tiles.PersistenceDirtySections.MarkDirty(section);
                    }
            }
            adopted = true;
            owner.AppliedWorldItemAllocations += allocatedDropCount;
            publication = new(this, items, npcs, projectiles, publicationOrder.ToArray());
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            allocation?.Dispose();
            projectileSpawns?.Dispose();
        }

        private bool Fail() { failed = true; return false; }
    }

    internal sealed partial class ToolWorldPublication1458
    {
        internal RuntimeWorldItemStore.AllocationPreview.AllocationPublication? Items { get; }
        private readonly RuntimeNpcStore.WorldSpawnPublication? npcs;
        private readonly RuntimeProjectileStore.WorldSpawnPreparation.WorldSpawnPublication? projectiles;
        private readonly ToolWorldPublicationKind1458[] order;
        private int next;
        private int itemCount, npcCount, projectileCount;
        private bool publishing;
        internal ToolWorldPublication1458(ToolWorldMutationPlan source,
            RuntimeWorldItemStore.AllocationPreview.AllocationPublication? items,
            RuntimeNpcStore.WorldSpawnPublication? npcs,
            RuntimeProjectileStore.WorldSpawnPreparation.WorldSpawnPublication? projectiles,
            ToolWorldPublicationKind1458[] order)
        {
            Items = items; this.npcs = npcs; this.projectiles = projectiles; this.order = order;
            CaptureWirePublication(source);
            larvaBosses = source.CaptureLarvaPublications();
        }

        internal bool TryPublishThrough(in ToolWorldPublicationOffset1458 offset)
        {
            if (publishing || offset.End < next || offset.End > order.Length) return false;
            int expectedItems = itemCount, expectedNpcs = npcCount, expectedProjectiles = projectileCount;
            for (int index = next; index < offset.End; index++)
                switch (order[index])
                {
                    case ToolWorldPublicationKind1458.Item: expectedItems++; break;
                    case ToolWorldPublicationKind1458.Npc: expectedNpcs++; break;
                    case ToolWorldPublicationKind1458.Projectile: expectedProjectiles++; break;
                }
            if (expectedItems != offset.Items || expectedNpcs != offset.Npcs || expectedProjectiles != offset.Projectiles) return false;
            Exception? failure = null;
            publishing = true;
            try
            {
                while (next < offset.End)
                {
                    // Consume before callbacks; a throwing observer cannot replay a birth.
                    var kind = order[next++];
                    try
                    {
                        bool accepted;
                        switch (kind)
                        {
                            case ToolWorldPublicationKind1458.Item:
                                itemCount++; accepted = Items is not null && Items.TryPublishNext(out _); break;
                            case ToolWorldPublicationKind1458.Npc:
                                npcCount++; accepted = npcs is not null && npcs.TryPublishNext(); break;
                            case ToolWorldPublicationKind1458.Projectile:
                                projectileCount++; accepted = projectiles is not null &&
                                    (wireOwner.toolProjectileAuthority is { } sourceOwner
                                        ? sourceOwner.TryPublishToolWorldBirth(projectiles) : projectiles.TryPublishNext()); break;
                            case ToolWorldPublicationKind1458.LarvaBoss:
                                accepted = TryPublishLarvaBoss(); break;
                            case ToolWorldPublicationKind1458.TileSquare:
                            case ToolWorldPublicationKind1458.TreeEffect:
                            case ToolWorldPublicationKind1458.TileManipulation:
                            case ToolWorldPublicationKind1458.ChestUpdate:
                                accepted = TryPublishWire(kind); break;
                            default: accepted = false; break;
                        }
                        if (!accepted) failure ??= new InvalidOperationException("Prepared tool publication journal is inconsistent.");
                    }
                    catch (Exception exception) { failure ??= exception; }
                }
            }
            finally { publishing = false; }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return true;
        }
    }

}
