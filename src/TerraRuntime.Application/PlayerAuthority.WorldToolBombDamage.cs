using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool TryPrepareWorldToolBombDamage(in ProjectileSnapshot explosion,
        in ProjectileLifecycleState lifecycle, out WorldToolBombPlayerDamagePreparation? token)
    {
        token = null;
        if (!explosion.IsActive || explosion.Type.Value != 75 || explosion.Spawner != byte.MaxValue || explosion.Damage != 60 ||
            explosion.KnockBack != 8f || !lifecycle.NpcProjectile || !lifecycle.WorldToolBombKillInProgress ||
            lifecycle.TimeLeft != 0 || !float.IsFinite(explosion.PositionX) || !float.IsFinite(explosion.PositionY)) return false;
        var humans = new RuntimePlayerProjectileUseCapture?[byte.MaxValue];
        var servers = new PlayerStateSnapshot?[byte.MaxValue];
        var immunity = new bool?[byte.MaxValue];
        var incomingBefore = new IncomingPlayerCombatCapture1458?[byte.MaxValue];
        var hits = new List<WorldBombPlayerHit1458>();
        ulong members = membership.Serial, items = inventory.Serial;
        long tick = currentCombatTick;
        var damageWorld = playerUpdateWorld;
        bool equipmentExpert = combatEquipmentExpertMode, equipmentMaster = combatEquipmentMasterMode;
        var localVanity = combatEquipmentLocalVanityArmor;
        var randomOwner = playerUpdateRandom;
        VanillaUnifiedRandom1458? beforeRandom = null, afterRandom = null;
        for (int slot = 0; slot < byte.MaxValue; slot++)
        {
            bool human = membership.TryGet((byte)slot, out var member);
            bool server = serverPlayers is not null && serverPlayers.TryGet(new PlayerSlotId((byte)slot), out _);
            if (human && server) return false;
            if (server)
            {
                serverPlayers!.TryGet(new PlayerSlotId((byte)slot), out var state);
                servers[slot] = state;
                // No callback-free server-player Hurt/profile participant exists
                // here. Never invent a human connection or NPC origin for it.
                if (!state.IsDead && WorldBombIntersects(explosion.PositionX, explosion.PositionY, in state)) return false;
                continue;
            }
            if (!human) continue;
            if (!TryCaptureProjectileUse(member.Connection, out var profile) || profile is null) return false;
            humans[slot] = profile;
            if (profile.Player.IsDead || !WorldBombIntersects(explosion.PositionX, explosion.PositionY, profile.Player)) continue;
            // Ghost is already retained in MovementFlags. Damage_EVP does not
            // test that flag; the separate proximity-AI participant does. Never
            // infer a ghost or reject a live player from appearance difficulty.
            if (profile.Player.HasMount ||
                !TryCaptureIncomingCombat(profile.Player.Player, out var incoming) || !incoming.IsAlive ||
                incoming.Member is null || incoming.Player != profile.Player) return false;
            incomingBefore[slot] = incoming;
            bool immune = damageImmunity.IsPveImmune(profile.Player.Player,
                VanillaPlayerImmunityChannel1458.General, tick);
            immunity[slot] = immune;
            if (immune) continue;
            // Same raw world-mode +Good World projection as the existing NPC
            // difficulty owner. The default hostile curve clamps at Master3.
            // Journey's live slider is not inferred from its presentation bit.
            if (masterMode && !expertMode ||
                damageWorld is not { GetGoodWorld: { } goodWorld, JourneyMode: false } ||
                randomOwner is null || !ReferenceEquals(playerUpdateRandom, randomOwner) || !playerUpdateRandomOwned) return false;
            int sourceDifficulty = (masterMode ? 3 : expertMode ? 2 : 1) + (goodWorld ? 1 : 0);
            int hostileMultiplier = Math.Min(3, sourceDifficulty);
            if (equipmentExpert != (sourceDifficulty >= 2) ||
                equipmentMaster != (sourceDifficulty >= 3)) return false;
            beforeRandom ??= randomOwner.Clone();
            afterRandom ??= beforeRandom.Clone();
            // Explicit authority-role mapping: only DamageVar uses this existing
            // owned UpdatePlayers stream. Client Hurt's visual/reactive33/34-draw
            // trace is NOT reproduced or charged to dedicated world Main.
            int damage = VanillaIncomingPlayerDamageFacts1458.ResolveHostileProjectileDamage(
                explosion.Damage, afterRandom.Next(-15, 16), hostileMultiplier);
            if (incoming.GodMode) continue; // Source Hurt checks Godmode after DamageVar.
            int direction = incoming.Player.PositionX + 10f < explosion.PositionX + 64f ? -1 : 1;
            var attack = new AuthoritativeAttackDamage(DamageSource.FromEnvironmentProjectile(explosion.Handle),
                damage, 0, false, 4.5f, direction);
            if (!VanillaCombatDamagePipeline.TryResolvePlayerDamage(in attack, incoming.Combat, false,
                out var final, sourceDifficulty >= 2, sourceDifficulty >= 3) || final.Damage <= 0 ||
                !TryResolveIncomingLifeAfterDamage(in incoming, final.Damage, out short nextLife)) return false;
            int duration = VanillaIncomingPlayerDamageFacts1458.ResolvePveImmunityTicks(final.Damage);
            if (tick > long.MaxValue - duration || member.Revision == ulong.MaxValue ||
                member.ProjectileUseInputRevision == ulong.MaxValue) return false;
            hits.Add(new(profile, incoming, final, nextLife, direction, tick + duration));
        }
        token = new(this, members, items, tick, humans, servers, immunity, incomingBefore, hits.ToArray(), beforeRandom, afterRandom,
            damageWorld, equipmentExpert, equipmentMaster, localVanity, randomOwner);
        return token.IsCurrentOwned;
    }

    private static bool WorldBombIntersects(float x, float y, in PlayerStateSnapshot target)
    {
        var size = target.HasMount ? VanillaPlayerMountHitbox1458.Resolve(target.MountType) : (20, 42);
        return (int)x < (int)target.PositionX + size.Item1 && (int)x + 128 > (int)target.PositionX &&
            (int)y < (int)target.PositionY + size.Item2 && (int)y + 128 > (int)target.PositionY;
    }

    internal sealed record WorldBombPlayerHit1458(RuntimePlayerProjectileUseCapture Profile,
        IncomingPlayerCombatCapture1458 Incoming, FinalDamageToHp Final, short Life, int Direction, long Until);

    internal sealed class WorldToolBombPlayerDamagePreparation
    {
        private readonly PlayerAuthority owner;
        private readonly ulong members, inventorySerial;
        private readonly long tick;
        private readonly RuntimePlayerUpdateWorld1458? damageWorld;
        private readonly bool equipmentExpert, equipmentMaster;
        private readonly VanillaPlayerLocalVanityArmor1458? localVanity;
        private readonly VanillaUnifiedRandom1458? randomOwner;
        private readonly RuntimePlayerProjectileUseCapture?[] humans;
        private readonly PlayerStateSnapshot?[] servers;
        private readonly WorldBombPlayerHit1458[] hits;
        private readonly bool?[] immunity;
        private readonly IncomingPlayerCombatCapture1458?[] incomingBefore;
        private readonly VanillaUnifiedRandom1458? beforeRandom, afterRandom;
        private readonly PlayerStateSnapshot[] accepted;
        private readonly RuntimePlayerItemPhase1458?[] acceptedPhases;
        private readonly bool[] stealth, motion;
        private bool sealedPlan, adopted;
        private int nextHit, nextEvent;

        internal WorldToolBombPlayerDamagePreparation(PlayerAuthority owner, ulong members, ulong inventorySerial,
            long tick, RuntimePlayerProjectileUseCapture?[] humans, PlayerStateSnapshot?[] servers,
            bool?[] immunity, IncomingPlayerCombatCapture1458?[] incomingBefore, WorldBombPlayerHit1458[] hits, VanillaUnifiedRandom1458? beforeRandom, VanillaUnifiedRandom1458? afterRandom,
            RuntimePlayerUpdateWorld1458? damageWorld, bool equipmentExpert, bool equipmentMaster,
            VanillaPlayerLocalVanityArmor1458? localVanity, VanillaUnifiedRandom1458? randomOwner)
        {
            this.owner = owner; this.members = members; this.inventorySerial = inventorySerial;
            this.damageWorld = damageWorld;
            this.equipmentExpert = equipmentExpert; this.equipmentMaster = equipmentMaster;
            this.localVanity = localVanity; this.randomOwner = randomOwner;
            this.tick = tick; this.humans = humans; this.servers = servers; this.hits = hits; this.immunity = immunity; this.incomingBefore = incomingBefore;
            this.beforeRandom = beforeRandom; this.afterRandom = afterRandom;
            accepted = new PlayerStateSnapshot[hits.Length]; acceptedPhases = new RuntimePlayerItemPhase1458?[hits.Length];
            stealth = new bool[hits.Length]; motion = new bool[hits.Length];
        }

        internal bool IsRandomDistinctFrom(VanillaUnifiedRandom1458 worldOwner) =>
            beforeRandom is null || ReferenceEquals(owner.playerUpdateRandom, randomOwner) && !ReferenceEquals(randomOwner, worldOwner);

        internal bool ValidateExternalOwners() => IsCurrentOwned;
        internal bool IsCurrentOwned
        {
            get
            {
                if (adopted || owner.membership.Serial != members || owner.inventory.Serial != inventorySerial ||
                    owner.currentCombatTick != tick || !IsDamageContextCurrent || beforeRandom is not null &&
                    (!ReferenceEquals(owner.playerUpdateRandom, randomOwner) || randomOwner is null || !owner.playerUpdateRandomOwned ||
                     !randomOwner.HasSameState(beforeRandom))) return false;
                for (int slot = 0; slot < byte.MaxValue; slot++)
                {
                    bool human = owner.membership.TryGet((byte)slot, out _);
                    bool server = owner.serverPlayers is not null &&
                        owner.serverPlayers.TryGet(new PlayerSlotId((byte)slot), out _);
                    if (human != (humans[slot] is not null) || server != servers[slot].HasValue) return false;
                    if (humans[slot] is { } profile && (!owner.IsCurrentProjectileUse(profile) ||
                        immunity[slot] is { } immune && owner.damageImmunity.IsPveImmune(profile.Player.Player,
                            VanillaPlayerImmunityChannel1458.General, tick) != immune)) return false;
                    if (incomingBefore[slot] is { } incoming && !owner.IsCurrentIncomingCombat(in incoming)) return false;
                    if (servers[slot] is { } before &&
                        (!owner.serverPlayers!.TryGet(before.Player, out var current) || current != before)) return false;
                }
                foreach (var hit in hits)
                    if (!owner.IsCurrentIncomingCombat(hit.Incoming) ||
                        owner.damageImmunity.IsPveImmune(hit.Profile.Player.Player,
                            VanillaPlayerImmunityChannel1458.General, tick)) return false;
                // Finish with the same concrete owners after all captured-profile
                // queries; no RNG endpoint or equipment context is stamped anew.
                return IsDamageContextCurrent && (beforeRandom is null ||
                    ReferenceEquals(owner.playerUpdateRandom, randomOwner) && owner.playerUpdateRandomOwned &&
                    randomOwner is not null && randomOwner.HasSameState(beforeRandom));
            }
        }

        private bool IsDamageContextCurrent => owner.playerUpdateWorld == damageWorld &&
            owner.combatEquipmentExpertMode == equipmentExpert && owner.combatEquipmentMasterMode == equipmentMaster &&
            owner.combatEquipmentLocalVanityArmor == localVanity;

        internal bool TrySealForJointAdoption()
        {
            if (sealedPlan || !IsCurrentOwned) return false;
            sealedPlan = true; return true;
        }

        internal bool TryAdoptUnpublished()
        {
            if (!sealedPlan || !IsCurrentOwned) return false;
            for (int index = 0; index < hits.Length; index++)
            {
                var hit = hits[index]; var member = hit.Incoming.Member!;
                bool preservePhase = member.RemotePhaseSnapshot == hit.Incoming.Player &&
                    member.RemotePhaseItem == hit.Incoming.ItemPhase &&
                    member.RemotePhaseInputRevision == member.ProjectileUseInputRevision;
                if (!member.TryAdvanceRevision())
                    throw new InvalidOperationException("Preflighted world-bomb player revision changed.");
                stealth[index] = member.Stealth != 1f; member.Stealth = 1f;
                AdoptIncomingLife(member, hit.Incoming, hit.Life);
                member.IsDead = hit.Life <= 0;
                if (member.IsDead) member.ItemAnimation = 0;
                if (!hit.Final.Mitigation.NoKnockback)
                {
                    member.VelocityX = 4.5f * hit.Direction; member.VelocityY = -3.5f;
                    motion[index] = true;
                }
                owner.damageImmunity.RecordPve(hit.Profile.Player.Player, VanillaPlayerImmunityChannel1458.General, hit.Until);
                if (preservePhase) member.MarkRemotePhaseSnapshot();
                accepted[index] = member.CaptureSnapshot(); acceptedPhases[index] = member.ItemPhase;
            }
            if (afterRandom is not null) randomOwner!.CopyStateFrom(afterRandom);
            adopted = true; return true;
        }

        internal bool TryPublishNext()
        {
            if (!adopted) return false;
            while (nextHit < hits.Length)
            {
                int index = nextHit, step = nextEvent++;
                if (nextEvent == 3) { nextEvent = 0; nextHit++; }
                var hit = hits[index]; var state = accepted[index];
                if (!owner.membership.TryGet(state.Player, out var member) ||
                    !ReferenceEquals(member, hit.Incoming.Member) || member.CaptureSnapshot() != state ||
                    member.ItemPhase != acceptedPhases[index]) continue;
                if (step == 0)
                {
                    if (!stealth[index]) continue;
                    owner.events?.PlayerStealthUpdated(hit.Profile.Connection, 1f);
                }
                else if (step == 1)
                {
                    if (!motion[index]) continue;
                    var movement = new PlayerMovementCommitRequest(state.Player.Slot, state.ControlFlags,
                        state.MovementFlags, state.MiscFlags1, state.MiscFlags2, state.SelectedItem,
                        state.PositionX, state.PositionY, true, state.VelocityX, state.VelocityY,
                        state.HasMount, state.MountType, true, state.PotionOfReturnOriginalPositionX,
                        state.PotionOfReturnOriginalPositionY, state.PotionOfReturnHomePositionX,
                        state.PotionOfReturnHomePositionY, true, state.CameraTargetX, state.CameraTargetY);
                    owner.events?.PlayerMoved(hit.Profile.Connection, in movement);
                }
                else
                {
                    var health = new PlayerHealthCommitRequest(state.Player.Slot, state.Life, state.MaxLife);
                    owner.events?.PlayerAuthoritativeHealthUpdated(hit.Profile.Connection, in health);
                }
                return true;
            }
            return false;
        }
    }
}
