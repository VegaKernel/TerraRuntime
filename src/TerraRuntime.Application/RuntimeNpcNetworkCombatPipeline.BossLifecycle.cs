using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private static bool IsDestroyerMember(NpcTypeId type) =>
        type == VanillaNpcIds.Destroyer || type == VanillaNpcIds.DestroyerBody || type == VanillaNpcIds.DestroyerTail;

    private static bool IsHardmodeBossRoot(NpcTypeId type) =>
        type == VanillaNpcIds.QueenSlime || type == VanillaNpcIds.Destroyer ||
        type == VanillaNpcIds.Retinazer || type == VanillaNpcIds.Spazmatism ||
        type == VanillaNpcIds.SkeletronPrime || type == VanillaNpcIds.Plantera ||
        type == VanillaNpcIds.Golem || type == VanillaNpcIds.DukeFishron ||
        type == VanillaNpcIds.LunaticCultist || type == VanillaNpcIds.EmpressOfLight ||
        type == VanillaNpcIds.MoonLordCore;

    private bool TryResolveDestroyerRoot(in NpcSnapshot member, out NpcSnapshot root)
    {
        if (member.TypeIdentity == VanillaNpcIds.Destroyer)
        {
            root = member;
            return true;
        }
        if (IsDestroyerMember(member.TypeIdentity) && float.IsFinite(member.Ai.Ai3) &&
            member.Ai.Ai3 >= 0f && member.Ai.Ai3 < byte.MaxValue && member.Ai.Ai3 == MathF.Truncate(member.Ai.Ai3) &&
            DeathNpcs.TryGetActive((byte)member.Ai.Ai3, out NpcSnapshot linked) && linked.TypeIdentity == VanillaNpcIds.Destroyer)
        {
            root = linked;
            return true;
        }
        int count = DeathNpcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            if (npcFamilyBuffer[index].TypeIdentity == VanillaNpcIds.Destroyer)
            {
                root = npcFamilyBuffer[index];
                return true;
            }
        }
        root = default;
        return false;
    }

    private bool TrySetDestroyerRootLife(in NpcSnapshot root, int life, out NpcSnapshot committed)
    {
        committed = default;
        if (root.TypeIdentity != VanillaNpcIds.Destroyer || life < 0 || life > root.Simulation.LifeMax ||
            !DeathNpcs.TryGet(root.Handle, out var current) || current != root)
            return false;
        var update = new NpcStateUpdate(
            root.Type, root.NetId, root.PositionX, root.PositionY, root.VelocityX, root.VelocityY, root.Target, root.Ai,
            root.Simulation with { Life = life, JustHit = true });
        return DeathNpcs.TryUpdate(root.Handle, in update, out committed);
    }

    private void MarkDestroyerInteraction(in NpcSnapshot member, PlayerHandle player)
    {
        if (!TryResolveDestroyerRoot(in member, out NpcSnapshot root))
        {
            interactions.TryMark(member.Handle, player);
            return;
        }
        int count = DeathNpcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            if (peer.TypeIdentity == VanillaNpcIds.Destroyer ||
                (IsDestroyerMember(peer.TypeIdentity) && float.IsFinite(peer.Ai.Ai3) &&
                 peer.Ai.Ai3 >= 0f && peer.Ai.Ai3 < byte.MaxValue && (byte)peer.Ai.Ai3 == root.Handle.Slot))
                interactions.TryMark(peer.Handle, player);
        }
    }

    private void CleanupDestroyerSegments(byte rootSlot)
    {
        int count = DeathNpcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            if ((peer.TypeIdentity != VanillaNpcIds.DestroyerBody && peer.TypeIdentity != VanillaNpcIds.DestroyerTail) ||
                !float.IsFinite(peer.Ai.Ai3) || peer.Ai.Ai3 < 0f || peer.Ai.Ai3 >= byte.MaxValue || (byte)peer.Ai.Ai3 != rootSlot)
                continue;
            if (DeathNpcs.TryDespawn(peer.Handle))
            {
                interactions.Forget(peer.Handle);
                npcReplication?.TryPublishDeath(in peer);
            }
        }
    }

    private bool TryResolveWallOfFleshRoot(in NpcSnapshot member, out NpcSnapshot root)
    {
        if (member.TypeIdentity == VanillaNpcIds.WallOfFlesh)
        {
            root = member;
            return true;
        }
        if (member.TypeIdentity == VanillaNpcIds.WallOfFleshEye && float.IsFinite(member.Ai.Ai3) &&
            member.Ai.Ai3 >= 0f && member.Ai.Ai3 < byte.MaxValue &&
            DeathNpcs.TryGetActive((byte)member.Ai.Ai3, out NpcSnapshot linked) && linked.TypeIdentity == VanillaNpcIds.WallOfFlesh)
        {
            root = linked;
            return true;
        }
        int count = DeathNpcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            if (npcFamilyBuffer[index].TypeIdentity == VanillaNpcIds.WallOfFlesh)
            {
                root = npcFamilyBuffer[index];
                return true;
            }
        }
        root = default;
        return false;
    }

    private bool TrySetWallOfFleshRootLife(in NpcSnapshot root, int life, out NpcSnapshot committed)
    {
        committed = default;
        if (root.TypeIdentity != VanillaNpcIds.WallOfFlesh || life < 0 || life > root.Simulation.LifeMax)
            return false;
        var update = new NpcStateUpdate(
            root.Type, root.NetId, root.PositionX, root.PositionY, root.VelocityX, root.VelocityY, root.Target, root.Ai,
            root.Simulation with { Life = life, JustHit = true });
        return DeathNpcs.TryUpdate(root.Handle, in update, out committed);
    }

    private void MarkWallOfFleshInteraction(in NpcSnapshot member, PlayerHandle player)
    {
        if (TryResolveWallOfFleshRoot(in member, out NpcSnapshot root))
            interactions.TryMark(root.Handle, player);
        interactions.TryMark(member.Handle, player);
        int count = DeathNpcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            if (peer.TypeIdentity == VanillaNpcIds.WallOfFleshEye && (byte)peer.Ai.Ai3 == root.Handle.Slot)
                interactions.TryMark(peer.Handle, player);
        }
    }

    private void CleanupWallOfFleshChildren(byte rootSlot)
    {
        int count = DeathNpcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            bool child = (peer.TypeIdentity == VanillaNpcIds.WallOfFleshEye || peer.TypeIdentity == VanillaNpcIds.TheHungry) &&
                         float.IsFinite(peer.Ai.Ai3) && peer.Ai.Ai3 >= 0f && peer.Ai.Ai3 < byte.MaxValue && (byte)peer.Ai.Ai3 == rootSlot;
            if (!child)
                continue;
            if (DeathNpcs.TryDespawn(peer.Handle))
            {
                interactions.Forget(peer.Handle);
                npcReplication?.TryPublishDeath(in peer);
            }
        }
    }

    private void MarkSkeletronInteraction(PlayerHandle player)
    {
        int count = DeathNpcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            if (peer.TypeIdentity == VanillaNpcIds.SkeletronHead || peer.TypeIdentity == VanillaNpcIds.SkeletronHand)
                interactions.TryMark(peer.Handle, player);
        }
    }

    private static bool IsPrimeMember(NpcTypeId type) =>
        type == VanillaNpcIds.SkeletronPrime || type == VanillaNpcIds.PrimeCannon ||
        type == VanillaNpcIds.PrimeSaw || type == VanillaNpcIds.PrimeVice || type == VanillaNpcIds.PrimeLaser;

    private void MarkMechanicalFamilyInteraction(NpcTypeId type, PlayerHandle player)
    {
        // NPC.ApplyInteraction propagates between every active twin, or all active Prime127..131 parts,
        // before the strike. Use the existing exact-generation ledger, not a second encounter credit store.
        bool twins = VanillaMechanicalBossLootEvaluator.IsTwin(type);
        int count = DeathNpcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            if (twins ? VanillaMechanicalBossLootEvaluator.IsTwin(peer.TypeIdentity) : IsPrimeMember(peer.TypeIdentity))
                interactions.TryMark(peer.Handle, player);
        }
    }

    private void ApplyHardmodeBossDeathEffects(in NpcSnapshot dead)
    {
        if (dead.TypeIdentity == VanillaNpcIds.QueenSlime)
        {
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.QueenSlime);
            return;
        }
        if (dead.TypeIdentity == VanillaNpcIds.Destroyer)
        {
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.Destroyer);
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.AnyMechanicalBoss);
            return;
        }
        if (dead.TypeIdentity == VanillaNpcIds.Retinazer || dead.TypeIdentity == VanillaNpcIds.Spazmatism)
        {
            NpcTypeId other = dead.TypeIdentity == VanillaNpcIds.Retinazer ? VanillaNpcIds.Spazmatism : VanillaNpcIds.Retinazer;
            int count = DeathNpcs.CopyActive(npcFamilyBuffer);
            for (int index = 0; index < count; index++)
            {
                NpcSnapshot peer = npcFamilyBuffer[index];
                if (peer.Handle != dead.Handle && peer.TypeIdentity == other && peer.Simulation.Life > 0)
                    return;
            }
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.Twins);
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.AnyMechanicalBoss);
            return;
        }
        if (dead.TypeIdentity == VanillaNpcIds.SkeletronPrime)
        {
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.SkeletronPrime);
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.AnyMechanicalBoss);
            return;
        }
        if (dead.TypeIdentity == VanillaNpcIds.Plantera)
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.Plantera);
        else if (dead.TypeIdentity == VanillaNpcIds.Golem)
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.Golem);
        else if (dead.TypeIdentity == VanillaNpcIds.DukeFishron)
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.DukeFishron);
        else if (dead.TypeIdentity == VanillaNpcIds.LunaticCultist)
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.LunaticCultist);
        else if (dead.TypeIdentity == VanillaNpcIds.EmpressOfLight)
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.EmpressOfLight);
        else if (dead.TypeIdentity == VanillaNpcIds.MoonLordCore)
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.MoonLord);
    }

    private void ApplySkeletronDeathEffects()
    {
        DeathProgression.MarkCompleted(VanillaWorldProgressionId.Skeletron);
    }

    private void ApplyQueenBeeDeathEffects()
    {
        DeathProgression.MarkCompleted(VanillaWorldProgressionId.QueenBee);
    }

    private void ApplyDeerclopsDeathEffects()
    {
        DeathProgression.MarkCompleted(VanillaWorldProgressionId.Deerclops);
    }

    private void ApplyWallOfFleshDeathEffects(in NpcSnapshot wallOfFlesh)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.WallOfFlesh, out VanillaNpcDefinition definition))
            throw new InvalidOperationException("Wall of Flesh definition disappeared during its committed death path.");

        if (!IsPreviewingDeath && worldTiles is not null)
        {
            VanillaWallOfFleshDeathWorldMutation.Apply(
                worldTiles,
                wallOfFlesh.PositionX,
                wallOfFlesh.PositionY,
                definition.Width,
                definition.Height,
                crimsonWorld);
        }

        DeathProgression.MarkCompleted(VanillaWorldProgressionId.Hardmode);
    }

    private void ApplyEvilBossDeathEffects(bool eaterBoss)
    {
        if (eaterBoss)
        {
            // NPC.DoDeathEvents evaluates these branches before SetEventFlagCleared(downedBoss2).
            bool wasAlreadyDowned = evilBossDownedBaseline || DeathProgression.IsCompleted(VanillaWorldProgressionId.EvilBoss);
            if (skyblockLowTiles)
                DeathProgression.MarkCompleted(VanillaWorldProgressionId.ShadowOrbSmashed);
            if (isThereAWorldSurface && (!wasAlreadyDowned || random.NextInt32(0, 2) == 0))
                DeathClock?.ScheduleMeteor();
        }

        DeathProgression.MarkCompleted(VanillaWorldProgressionId.EvilBoss);
    }

    private bool TryFindClosestPlayer(in NpcSnapshot npc, out PlayerStateSnapshot closest)
    {
        closest = default;
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(npc.Simulation, out var body))
            return false;

        // Player.FindClosest uses integer half-widths/heights and strict Manhattan-distance comparison.
        float npcCenterX = npc.PositionX + body.Width / 2;
        float npcCenterY = npc.PositionY + body.Height / 2;
        float bestDistance = -1f;
        bool foundAny = false;

        for (int index = 0; index < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots; index++)
        {
            var slot = new PlayerSlotId(checked((byte)index));
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;

            if (!foundAny)
            {
                closest = player;
                foundAny = true;
            }
            if (player.IsDead)
                continue;

            (float playerWidth, float playerHeight) = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) :
                (VanillaPlayerWidth, VanillaPlayerHeight);
            float playerCenterX = player.PositionX + (int)playerWidth / 2;
            float playerCenterY = player.PositionY + (int)playerHeight / 2;
            float distance = MathF.Abs(playerCenterX - npcCenterX) + MathF.Abs(playerCenterY - npcCenterY);
            if (bestDistance >= 0f && distance >= bestDistance)
                continue;

            bestDistance = distance;
            closest = player;
        }

        return foundAny;
    }

    private void AdvanceSlimeRainDeath(in NpcSnapshot dead)
    {
        // Main.slimeRainNPC is consulted with NPC.type. The implemented Slime Rain variants all retain
        // Blue Slime's canonical type and differ only by net ID, so this deliberately does not filter net IDs.
        if (DeathClock is null || dead.TypeIdentity != VanillaNpcIds.BlueSlime)
            return;

        bool kingSlimeActive = false;
        int active = DeathNpcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < active; index++)
        {
            if (npcFamilyBuffer[index].TypeIdentity == VanillaNpcIds.KingSlime)
            {
                kingSlimeActive = true;
                break;
            }
        }

        if (!DeathClock.TryAdvanceSlimeRainKillCount(
                slimeRainNpc: true,
                kingSlimeActive,
                DeathProgression.IsCompleted(VanillaWorldProgressionId.KingSlime)))
        {
            return;
        }

        // NPC.NPCLoot resolves closestPlayer before DoDeathEvents. It resets the counter after calling
        // SpawnOnPlayer regardless of whether placement succeeds, which RuntimeWorldClock already preserved.
        if (slimeRainKingSpawn is not null && TryFindClosestPlayer(in dead, out PlayerStateSnapshot closest))
        {
            if (IsPreviewingDeath) deathPreviewFailed = true;
            else _ = slimeRainKingSpawn(closest.Player.Slot);
        }
    }

    private void AdvanceMoonEventDeath(in NpcSnapshot dead)
    {
        // NPC.DoDeathEvents invokes both Moon-event progress checks after NPCLoot. RuntimeWorldClock owns their
        // shared transient wave state, while this boundary makes every authoritative kill path contribute once.
        DeathClock?.TryAdvanceMoonEventDeath(dead.TypeIdentity, expertMode, masterMode);
    }

    private void ApplyKingSlimeDeathEffects(in NpcSnapshot kingSlime)
    {
        DeathProgression.SetSlimeBlueSpawnBaseline(DeathClock?.SlimeBlueSpawnUnlocked == true);

        DeathClock?.TryStopSlimeRain(random);
        if (DeathClock is not null && DeathProgression.MarkSlimeBlueSpawnUnlocked())
        {
            DeathClock.MarkSlimeBlueSpawnUnlocked();
            if (!EnsurePreviewSpawnStream()) return;
            if (TryCreateNerdySlimeSpawnIntent(in kingSlime, out NpcAiSpawnIntent intent) &&
                DeathNpcs.TrySpawnIntent(in intent, out NpcSnapshot nerdy))
            {
                float velocityX = random.NextFloatDirection() * 3f;
                var update = new NpcStateUpdate(
                    nerdy.Type,
                    nerdy.NetId,
                    nerdy.PositionX,
                    nerdy.PositionY,
                    velocityX,
                    -10f,
                    nerdy.Target,
                    nerdy.Ai,
                    nerdy.Simulation);
                if (!DeathNpcs.TryUpdate(nerdy.Handle, in update, out _))
                    throw new InvalidOperationException("Nerdy Slime death spawn could not receive launch velocity.");
            }
        }
        DeathProgression.MarkCompleted(VanillaWorldProgressionId.KingSlime);
    }

    private static bool TryCreateNerdySlimeSpawnIntent(in NpcSnapshot source, out NpcAiSpawnIntent intent)
    {
        intent = default;
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox))
        {
            return false;
        }

        float centerX = source.PositionX + hitbox.Width * 0.5f;
        float centerY = source.PositionY + hitbox.Height * 0.5f;
        intent = new NpcAiSpawnIntent(
            VanillaNpcIds.TownSlimeBlue,
            BottomX: (int)centerX - 10,
            BottomY: (int)centerY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: checked((ushort)VanillaNpcDefinitionCatalog.DefaultTarget));
        return true;
    }

}
