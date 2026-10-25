using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct GemStaffContactResult1458(
    ProjectileSnapshot Current, bool SourceMustKill, int AcceptedHits);

internal sealed partial class RuntimeProjectileNpcCombatPass
{
    // Called inside the source Update/Kill sequence while the real generation remains active.
    // The generic post-simulation pass must never process these actors a second time.
    internal bool TryProcessGemContacts(in ProjectileSnapshot currentProjectile, bool terminalArea,
        WorldTileStore tiles, out GemStaffContactResult1458 result)
    {
        var current = currentProjectile;
        result = new(current, false, 0);
        if (!usesSourceRandom || status is null ||
            !VanillaGemStaffProjectile1458.IsGemStaffProjectile(current.Type) ||
            !projectiles.TryGet(current.Handle, out var retained) || retained != current ||
            !projectiles.TryGetLifecycle(current.Handle, out var lifecycle) ||
            lifecycle.GemStaffFeatures is not { } features || lifecycle.GemStaff is not { } gem ||
            !VanillaGemStaffProjectile1458.TryDecodeFeatures(current.Ai.Ai0, out var reportedFeatures) ||
            reportedFeatures != features ||
            !projectiles.TryGetCombatTrustedOwner(current.Handle, out var ownerHandle) ||
            current.Spawner != ownerHandle.Slot.Value ||
            !players.TryCaptureProjectileUse(ownerHandle, out var owner) || owner is null ||
            owner.Buffs is null || owner.Player.Luck != 0 || !HasSupportedGemHitBuffs(owner.Buffs) ||
            !players.TryCaptureProjectileCombatSnapshot(ownerHandle, out var ownerCombat) ||
            !TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(current.Type, out var definition)) return false;

        if (terminalArea ? !lifecycle.GemStaffKillInProgress ||
                (features & GemStaffFeatures1458.AoeExplosion) == 0 : lifecycle.GemStaffKillInProgress)
            return false;

        int width = terminalArea ? 160 : definition.Width;
        int height = terminalArea ? 160 : definition.Height;
        if (!VanillaGemStaffProjectile1458.TryBuildDamageHitbox(current.PositionX, current.PositionY,
                width, height, out var body)) return false;
        long ordinal = lifecycle.GemStaffUpdateCount;
        int acceptedHits = 0;
        var targets = new NpcSnapshot[npcs.Capacity];
        int count = npcs.CopyActive(targets);
        for (int i = 0; i < count; i++)
        {
            var target = targets[i];
            if (target.Handle.Slot >= VanillaGemStaffProjectile1458.MaximumNpcCount ||
                !IsEligibleTarget(in target, out var size) ||
                target.TypeIdentity.Value is not (1 or 3 or 59) ||
                target.Simulation.LifeRegenCounter is null || target.Simulation.Immortal != false ||
                target.TypeIdentity.Value == 3 && (target.Simulation.ShimmerTransparency != 0f ||
                    target.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer) ||
                target.TypeIdentity.Value == 1 && target.Ai.Ai1 == 1345f ||
                localNpcImmunity.IsImmune(current.Handle, target.Handle, ordinal, 60)) continue;

            var targetBox = new GemStaffHitbox1458((int)target.PositionX, (int)target.PositionY,
                size.Width, size.Height);
            var terrain = new Dictionary<WorldSectionId, long>();
            bool terrainKnown = true;
            bool CanHit(GemStaffLosQuery1458 query)
            {
                if (!CaptureGemLosSections(tiles, in query, terrain))
                {
                    terrainKnown = false;
                    return false;
                }
                // CanHit's integer rectangle centers are identical for 0x0 and 1x1.
                // This is Collision.CanHit, not the different CanHitLine traversal.
                return VanillaWorldCanHit.HasLineOfSight(tiles, query.FromX, query.FromY,
                    Math.Max(1, query.FromWidth), Math.Max(1, query.FromHeight), query.ToX, query.ToY,
                    Math.Max(1, query.ToWidth), Math.Max(1, query.ToHeight));
            }
            if (!VanillaGemStaffProjectile1458.TryCollide(features, in body,
                    current.PositionX + width / 2f, current.PositionY + height / 2f,
                    gem.Trail, in targetBox, CanHit, out bool intersects)) return false;
            if (!terrainKnown) return false;
            if (!intersects) continue;
            if (!players.IsCurrentProjectileUse(owner) ||
                !npcs.TryCaptureDeathMutationSerial(out ulong npcSerial) ||
                !status.CaptureAddition(in target, out var buffPlan) ||
                !projectiles.TryPrepareNpcHit(in current, true, out var projectilePlan) ||
                projectilePlan is null) return false;

            var before = sourceRandom.Clone();
            var after = before.Clone();
            int crit = after.Next(1, 101);
            int variation = after.Next(-15, 16);
            if (!VanillaCombatFacts.TryResolveGemStaffPveHit(current.Type, current.Damage,
                    in ownerCombat, features, crit, variation, out var hit)) return false;
            _ = after.NextDouble();
            _ = after.NextDouble();
            var checkpoint = localNpcImmunity.Capture(current.Handle, target.Handle);
            var expectedLifecycle = lifecycle;
            var projectileHandle = current.Handle;
            bool IsCurrent() => players.IsCurrentProjectileUse(owner) && projectilePlan.IsCurrent &&
                buffPlan.IsCurrent && localNpcImmunity.IsCurrent(in checkpoint) &&
                npcs.TryCaptureDeathMutationSerial(out ulong serial) && serial == npcSerial &&
                projectiles.TryGetLifecycle(projectileHandle, out var live) && live == expectedLifecycle &&
                sourceRandom.HasSameState(before) && IsGemLosCurrent(tiles, terrain);
            ProjectileSnapshot adopted = current;
            bool adoptedThisHit = false;
            void Adopt(NpcSnapshot accepted)
            {
                if (!status.TryAdoptAddition(buffPlan, in accepted) ||
                    !projectilePlan.TryAdoptUnpublished(out adopted, out bool despawned) || despawned)
                    throw new InvalidOperationException("A prepared gem hit lost its retained owners.");
                adoptedThisHit = true;
                localNpcImmunity.MarkHit(adopted.Handle, target.Handle, ordinal);
                CommittedHits++;
                if (accepted.Simulation.Life == 0) Kills++;
                status.PublishAddition(buffPlan);
            }
            int direction = current.VelocityX > .01f ? 1 : current.VelocityX < -.01f ? -1 : 0;
            RuntimeProjectileNpcDamageResult outcome;
            try
            {
                outcome = combat.TryStrikePreparedProjectile(in current, in target, direction,
                    hit.Damage, hit.ArmorPenetration, hit.Critical, before, after, IsCurrent, Adopt);
            }
            catch
            {
                // An observer can throw after the joint commit. Preserve the exact adopted
                // image for the caller; it must compare that image, never adopt an arbitrary reread.
                if (adoptedThisHit) result = new(adopted, false, acceptedHits + 1);
                throw;
            }
            if (outcome == RuntimeProjectileNpcDamageResult.Rejected) return false;
            projectilePlan.TryPublish();
            current = adopted;
            acceptedHits++;
            result = new(current, false, acceptedHits);
            if (!projectiles.TryGet(current.Handle, out retained) || retained != current ||
                !projectiles.TryGetLifecycle(current.Handle, out lifecycle))
            {
                result = new(current, false, acceptedHits);
                return false;
            }
            if (!terminalArea && lifecycle.PenetrateOverride == 0)
            {
                result = new(current, true, acceptedHits);
                return true;
            }
        }
        result = new(current, false, acceptedHits);
        return true;
    }

    // Each successful CanHit query is bounded by its center-to-center tile rectangle,
    // including the one-tile neighbor probes. Retain only intersected network sections;
    // a later loot/world provider cannot change the terrain that authorized this hit.
    private static bool CaptureGemLosSections(WorldTileStore tiles, in GemStaffLosQuery1458 query,
        Dictionary<WorldSectionId, long> sections)
    {
        const int MaximumQueriedSections = 64;
        int maxX = tiles.Dimensions.WidthTiles - 1;
        int maxY = Math.Max(1, tiles.Dimensions.HeightTiles - 40);
        if (maxX < 1 || !TryGemLosAxis(query.FromX, query.FromWidth, maxX, out int fromX) ||
            !TryGemLosAxis(query.ToX, query.ToWidth, maxX, out int toX) ||
            !TryGemLosAxis(query.FromY, query.FromHeight, maxY, out int fromY) ||
            !TryGemLosAxis(query.ToY, query.ToHeight, maxY, out int toY)) return false;
        int left = Math.Max(0, Math.Min(fromX, toX) - 1);
        int right = Math.Min(maxX, Math.Max(fromX, toX) + 1);
        int top = Math.Max(0, Math.Min(fromY, toY) - 1);
        int bottom = Math.Min(tiles.Dimensions.HeightTiles - 1, Math.Max(fromY, toY) + 1);
        var first = TerrariaSectionGeometry.FromTile(tiles.Dimensions, left, top);
        var last = TerrariaSectionGeometry.FromTile(tiles.Dimensions, right, bottom);
        if ((long)(last.X - first.X + 1) * (last.Y - first.Y + 1) > MaximumQueriedSections) return false;
        for (int y = first.Y; y <= last.Y; y++)
            for (int x = first.X; x <= last.X; x++)
            {
                var section = new WorldSectionId(x, y);
                long version = tiles.GetSectionVersion(section);
                if (version < 0 || (version & 1) != 0) return false;
                if (sections.TryGetValue(section, out long previous))
                {
                    if (previous != version) return false;
                }
                else
                {
                    if (sections.Count == MaximumQueriedSections) return false;
                    sections.Add(section, version);
                }
            }
        return true;
    }

    private static bool TryGemLosAxis(float position, int size, int maximum, out int tile)
    {
        tile = 0;
        if (!float.IsFinite(position) || position < int.MinValue ||
            position > (double)int.MaxValue - size / 2 || size < 0) return false;
        tile = Math.Clamp(((int)position + size / 2) / 16, 1, maximum);
        return true;
    }

    private static bool IsGemLosCurrent(WorldTileStore tiles, Dictionary<WorldSectionId, long> sections)
    {
        foreach (var (section, version) in sections)
            if (tiles.GetSectionVersion(section) != version) return false;
        return true;
    }

    // UpdateBuffs23 sets noItems/cursed, but Damage_CanDealDamage has no such gate.
    // It blocks a new use, not damage from this already-owned projectile.
    private static bool HasSupportedGemHitBuffs(ReadOnlySpan<TerraRuntime.Contracts.Gameplay.BuffTypeId> buffs)
    {
        foreach (var buff in buffs)
            if (buff.Value is not (0 or 5 or 16 or 23 or 26 or 93 or 94 or 112 or 114 or 115 or 117 or 206 or 207 or 321))
                return false;
        return true;
    }
}
