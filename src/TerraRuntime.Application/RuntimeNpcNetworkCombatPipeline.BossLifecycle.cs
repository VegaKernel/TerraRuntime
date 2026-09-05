using TerraRuntime.Gameplay.Npcs;
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
            npcs.TryGetActive((byte)member.Ai.Ai3, out NpcSnapshot linked) && linked.TypeIdentity == VanillaNpcIds.Destroyer)
        {
            root = linked;
            return true;
        }
        int count = npcs.CopyActive(npcFamilyBuffer);
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

    private bool TrySetNpcLife(in NpcSnapshot npc, int life, out NpcSnapshot committed)
    {
        committed = default;
        if (life < 0 || life > npc.Simulation.LifeMax)
            return false;
        var update = new NpcStateUpdate(
            npc.Type, npc.NetId, npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai,
            npc.Simulation with { Life = life });
        return npcs.TryUpdate(npc.Handle, in update, out committed);
    }

    private bool TrySetDestroyerRootLife(in NpcSnapshot root, int life, out NpcSnapshot committed)
    {
        committed = default;
        if (root.TypeIdentity != VanillaNpcIds.Destroyer || life < 0 || life > root.Simulation.LifeMax)
            return false;
        var update = new NpcStateUpdate(
            root.Type, root.NetId, root.PositionX, root.PositionY, root.VelocityX, root.VelocityY, root.Target, root.Ai,
            root.Simulation with { Life = life, JustHit = true });
        return npcs.TryUpdate(root.Handle, in update, out committed);
    }

    private void MarkDestroyerInteraction(in NpcSnapshot member, PlayerHandle player)
    {
        if (!TryResolveDestroyerRoot(in member, out NpcSnapshot root))
        {
            interactions.TryMark(member.Handle, player);
            return;
        }
        int count = npcs.CopyActive(npcFamilyBuffer);
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
        int count = npcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            if ((peer.TypeIdentity != VanillaNpcIds.DestroyerBody && peer.TypeIdentity != VanillaNpcIds.DestroyerTail) ||
                !float.IsFinite(peer.Ai.Ai3) || peer.Ai.Ai3 < 0f || peer.Ai.Ai3 >= byte.MaxValue || (byte)peer.Ai.Ai3 != rootSlot)
                continue;
            if (npcs.TryDespawn(peer.Handle))
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
            npcs.TryGetActive((byte)member.Ai.Ai3, out NpcSnapshot linked) && linked.TypeIdentity == VanillaNpcIds.WallOfFlesh)
        {
            root = linked;
            return true;
        }
        int count = npcs.CopyActive(npcFamilyBuffer);
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
        return npcs.TryUpdate(root.Handle, in update, out committed);
    }

    private void MarkWallOfFleshInteraction(in NpcSnapshot member, PlayerHandle player)
    {
        if (TryResolveWallOfFleshRoot(in member, out NpcSnapshot root))
            interactions.TryMark(root.Handle, player);
        interactions.TryMark(member.Handle, player);
        int count = npcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            if (peer.TypeIdentity == VanillaNpcIds.WallOfFleshEye && (byte)peer.Ai.Ai3 == root.Handle.Slot)
                interactions.TryMark(peer.Handle, player);
        }
    }

    private void CleanupWallOfFleshChildren(byte rootSlot)
    {
        int count = npcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            bool child = (peer.TypeIdentity == VanillaNpcIds.WallOfFleshEye || peer.TypeIdentity == VanillaNpcIds.TheHungry) &&
                         float.IsFinite(peer.Ai.Ai3) && peer.Ai.Ai3 >= 0f && peer.Ai.Ai3 < byte.MaxValue && (byte)peer.Ai.Ai3 == rootSlot;
            if (!child)
                continue;
            if (npcs.TryDespawn(peer.Handle))
            {
                interactions.Forget(peer.Handle);
                npcReplication?.TryPublishDeath(in peer);
            }
        }
    }

    private void MarkSkeletronInteraction(PlayerHandle player)
    {
        int count = npcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            if (peer.TypeIdentity == VanillaNpcIds.SkeletronHead || peer.TypeIdentity == VanillaNpcIds.SkeletronHand)
                interactions.TryMark(peer.Handle, player);
        }
    }

    private void ApplyHardmodeBossDeathEffects(in NpcSnapshot dead)
    {
        if (dead.TypeIdentity == VanillaNpcIds.QueenSlime)
        {
            progression.MarkCompleted(VanillaWorldProgressionId.QueenSlime);
            return;
        }
        if (dead.TypeIdentity == VanillaNpcIds.Destroyer)
        {
            progression.MarkCompleted(VanillaWorldProgressionId.Destroyer);
            progression.MarkCompleted(VanillaWorldProgressionId.AnyMechanicalBoss);
            return;
        }
        if (dead.TypeIdentity == VanillaNpcIds.Retinazer || dead.TypeIdentity == VanillaNpcIds.Spazmatism)
        {
            NpcTypeId other = dead.TypeIdentity == VanillaNpcIds.Retinazer ? VanillaNpcIds.Spazmatism : VanillaNpcIds.Retinazer;
            int count = npcs.CopyActive(npcFamilyBuffer);
            for (int index = 0; index < count; index++)
            {
                NpcSnapshot peer = npcFamilyBuffer[index];
                if (peer.Handle != dead.Handle && peer.TypeIdentity == other && peer.Simulation.Life > 0)
                    return;
            }
            progression.MarkCompleted(VanillaWorldProgressionId.Twins);
            progression.MarkCompleted(VanillaWorldProgressionId.AnyMechanicalBoss);
            return;
        }
        if (dead.TypeIdentity == VanillaNpcIds.SkeletronPrime)
        {
            progression.MarkCompleted(VanillaWorldProgressionId.SkeletronPrime);
            progression.MarkCompleted(VanillaWorldProgressionId.AnyMechanicalBoss);
            return;
        }
        if (dead.TypeIdentity == VanillaNpcIds.Plantera)
            progression.MarkCompleted(VanillaWorldProgressionId.Plantera);
        else if (dead.TypeIdentity == VanillaNpcIds.Golem)
            progression.MarkCompleted(VanillaWorldProgressionId.Golem);
        else if (dead.TypeIdentity == VanillaNpcIds.DukeFishron)
            progression.MarkCompleted(VanillaWorldProgressionId.DukeFishron);
        else if (dead.TypeIdentity == VanillaNpcIds.LunaticCultist)
            progression.MarkCompleted(VanillaWorldProgressionId.LunaticCultist);
        else if (dead.TypeIdentity == VanillaNpcIds.EmpressOfLight)
            progression.MarkCompleted(VanillaWorldProgressionId.EmpressOfLight);
        else if (dead.TypeIdentity == VanillaNpcIds.MoonLordCore)
            progression.MarkCompleted(VanillaWorldProgressionId.MoonLord);
    }

    private void ApplySkeletronDeathEffects()
    {
        progression.MarkCompleted(VanillaWorldProgressionId.Skeletron);
    }

    private void ApplyQueenBeeDeathEffects()
    {
        progression.MarkCompleted(VanillaWorldProgressionId.QueenBee);
    }

    private void ApplyDeerclopsDeathEffects()
    {
        progression.MarkCompleted(VanillaWorldProgressionId.Deerclops);
    }

    private void ApplyWallOfFleshDeathEffects(in NpcSnapshot wallOfFlesh)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.WallOfFlesh, out VanillaNpcDefinition definition))
            throw new InvalidOperationException("Wall of Flesh definition disappeared during its committed death path.");

        if (worldTiles is not null)
        {
            VanillaWallOfFleshDeathWorldMutation.Apply(
                worldTiles,
                wallOfFlesh.PositionX,
                wallOfFlesh.PositionY,
                definition.Width,
                definition.Height,
                crimsonWorld);
        }

        DropWallOfFleshRecoveryItems(in wallOfFlesh, in definition);
        progression.MarkCompleted(VanillaWorldProgressionId.Hardmode);
    }

    private void DropWallOfFleshRecoveryItems(in NpcSnapshot wallOfFlesh, in VanillaNpcDefinition definition)
    {
        var origin = new NpcLootWorldItemOrigin(
            wallOfFlesh.PositionX + definition.Width * 0.5f,
            wallOfFlesh.PositionY + definition.Height * 0.5f);

        var potions = new NpcLootDrop(
            VanillaWallOfFleshItemIds.HealingPotion,
            checked((short)random.NextInt32(5, 16)));
        if (!wallOfFleshLoot.TryDeliverWorldItem(in origin, in potions, random))
            throw new InvalidOperationException("Wall of Flesh recovery Healing Potion drop could not be materialized.");

        int heartCount = random.NextInt32(0, 5) + 5;
        for (int index = 0; index < heartCount; index++)
        {
            var heart = new NpcLootDrop(VanillaWallOfFleshItemIds.Heart, 1);
            if (!wallOfFleshLoot.TryDeliverWorldItem(in origin, in heart, random))
                throw new InvalidOperationException("Wall of Flesh recovery Heart drop could not be materialized.");
        }
    }

    private void ApplyEvilBossDeathEffects(bool eaterBoss)
    {
        if (eaterBoss)
        {
            // NPC.DoDeathEvents evaluates these branches before SetEventFlagCleared(downedBoss2).
            bool wasAlreadyDowned = evilBossDownedBaseline || progression.IsCompleted(VanillaWorldProgressionId.EvilBoss);
            if (skyblockLowTiles)
                progression.MarkCompleted(VanillaWorldProgressionId.ShadowOrbSmashed);
            if (isThereAWorldSurface && (!wasAlreadyDowned || random.NextInt32(0, 2) == 0))
                worldClock?.ScheduleMeteor();
        }

        progression.MarkCompleted(VanillaWorldProgressionId.EvilBoss);
    }

    private void DropEaterOfWorldsHealingHeartIfEligible(in NpcSnapshot eaterSegment)
    {
        if (!TryFindClosestPlayer(in eaterSegment, out PlayerStateSnapshot closest) ||
            !closest.HasHealth || closest.Life >= closest.MaxLife ||
            random.NextInt32(0, 4) != 0 ||
            !VanillaNpcDefinitionCatalog.TryGet(
                eaterSegment.TypeIdentity,
                eaterSegment.NetIdentity,
                out VanillaNpcDefinition definition))
        {
            return;
        }

        var origin = new NpcLootWorldItemOrigin(
            eaterSegment.PositionX + definition.Width * 0.5f,
            eaterSegment.PositionY + definition.Height * 0.5f);
        var heart = new NpcLootDrop(VanillaWallOfFleshItemIds.Heart, 1);
        if (!eaterLoot.TryDeliverWorldItem(in origin, in heart, random))
            throw new InvalidOperationException("Eater of Worlds healing Heart drop could not be materialized.");
    }

    private bool TryFindClosestPlayer(in NpcSnapshot npc, out PlayerStateSnapshot closest)
    {
        closest = default;
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition))
            return false;

        float npcCenterX = npc.PositionX + definition.Width * 0.5f;
        float npcCenterY = npc.PositionY + definition.Height * 0.5f;
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

            float playerCenterX = player.PositionX + VanillaPlayerWidth * 0.5f;
            float playerCenterY = player.PositionY + VanillaPlayerHeight * 0.5f;
            float distance = MathF.Abs(playerCenterX - npcCenterX) + MathF.Abs(playerCenterY - npcCenterY);
            if (bestDistance >= 0f && distance >= bestDistance)
                continue;

            bestDistance = distance;
            closest = player;
        }

        return foundAny;
    }

    private void ApplyKingSlimeDeathEffects(in NpcSnapshot kingSlime)
    {
        progression.SetSlimeBlueSpawnBaseline(worldClock?.SlimeBlueSpawnUnlocked == true);

        worldClock?.TryStopSlimeRain(random);
        if (worldClock is not null && progression.MarkSlimeBlueSpawnUnlocked())
        {
            worldClock.MarkSlimeBlueSpawnUnlocked();
            if (TryCreateNerdySlimeSpawnIntent(in kingSlime, out NpcAiSpawnIntent intent) &&
                npcs.TrySpawnIntent(in intent, out NpcSnapshot nerdy))
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
                if (!npcs.TryUpdate(nerdy.Handle, in update, out _))
                    throw new InvalidOperationException("Nerdy Slime death spawn could not receive launch velocity.");
            }
        }
        progression.MarkCompleted(VanillaWorldProgressionId.KingSlime);
    }

    private static bool TryCreateNerdySlimeSpawnIntent(in NpcSnapshot source, out NpcAiSpawnIntent intent)
    {
        intent = default;
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
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
