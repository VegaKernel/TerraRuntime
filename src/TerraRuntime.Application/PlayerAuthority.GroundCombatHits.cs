using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Buffs;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool TryPrepareGroundCombatHits(in ProjectileSnapshot initial, NpcHandle sourceNpc,
        long tick, bool expertMode, bool masterMode, bool goodWorld,
        VanillaProjectileWorldStateStepper.OrdinaryArrowTerrainCapture terrain,
        out GroundCombatHitPreparation? preparation)
    {
        preparation = null;
        if (!sourceNpc.IsAssigned || !VanillaGroundCombatPlayerHit1458.IsSupported(initial.Type) ||
            expertMode != this.expertMode || masterMode != this.masterMode || currentCombatTick != tick)
            return false;
        var token = new GroundCombatHitPreparation(this, initial, sourceNpc, tick, expertMode, masterMode, goodWorld);
        foreach (var member in membership.Members)
        {
            int slot = member.Slot.Value;
            if (slot >= byte.MaxValue) return false;
            var snapshot = member.CaptureSnapshot();
            token.members[slot] = member;
            token.before[slot] = snapshot;
            if (snapshot.HasMount && snapshot.MountType > 65) return false;
            var body = snapshot.HasMount ? VanillaPlayerMountHitbox1458.Resolve(snapshot.MountType)
                : (VanillaPlayerHitboxFacts.BaseWidth, VanillaPlayerHitboxFacts.BaseHeight);
            if (snapshot.IsDead || initial.Damage <= 0 || !terrain.MayReach(snapshot.PositionX, snapshot.PositionY,
                    (int)body.Item1, (int)body.Item2)) continue;
            // The source local player's mount body requires a separately owned profile.
            if (snapshot.HasMount || !TryCaptureProjectileUse(member.Connection, out var profile) || profile is null ||
                !TryCaptureIncomingCombat(snapshot.Player, out var incoming) || !incoming.IsAlive ||
                incoming.Member != member || incoming.Player != snapshot || !CanOwnGroundCombatHurt(in incoming) ||
                transferProfiles.CaptureBuffState(member.Connection) is not { } buffs)
                return false;
            token.profiles[slot] = profile;
            token.incoming[slot] = incoming;
            token.buffs[slot] = buffs;
            token.immunity[slot] = damageImmunity.Capture(snapshot.Player);
            token.immune[slot] = damageImmunity.IsPveImmune(snapshot.Player,
                VanillaPlayerImmunityChannel1458.General, tick);
        }
        if (!token.IsCurrent) return false;
        preparation = token;
        return true;
    }

    internal sealed class GroundCombatHitPreparation
    {
        private readonly PlayerAuthority owner;
        private readonly ProjectileSnapshot initial;
        private readonly NpcHandle sourceNpc;
        private readonly long tick;
        private readonly bool expert, master, goodWorld;
        private readonly ulong membershipSerial, inventorySerial;
        private readonly RuntimePlayerUpdateWorld1458? updateWorld;
        private readonly bool equipmentExpert, equipmentMaster;
        private readonly VanillaPlayerLocalVanityArmor1458? localVanity;
        internal readonly RuntimePlayerMember?[] members = new RuntimePlayerMember?[byte.MaxValue];
        internal readonly PlayerStateSnapshot[] before = new PlayerStateSnapshot[byte.MaxValue];
        internal readonly RuntimePlayerProjectileUseCapture?[] profiles = new RuntimePlayerProjectileUseCapture?[byte.MaxValue];
        internal readonly IncomingPlayerCombatCapture1458?[] incoming = new IncomingPlayerCombatCapture1458?[byte.MaxValue];
        internal readonly PlayerBuffState?[] buffs = new PlayerBuffState?[byte.MaxValue];
        internal readonly RuntimePlayerDamageImmunityStore.Capture1458?[] immunity = new RuntimePlayerDamageImmunityStore.Capture1458?[byte.MaxValue];
        internal readonly bool[] immune = new bool[byte.MaxValue];
        private readonly bool[] shadowImmune = new bool[byte.MaxValue], avoided = new bool[byte.MaxValue];
        private readonly List<Hit> hits = [];
        private readonly List<int> avoidances = [];
        private bool adopted, published;

        private sealed record Hit(int Slot, IncomingPlayerCombatCapture1458 Incoming, FinalDamageToHp Final,
            short Life, int Direction, PlayerBuffState BeforeBuffs, PlayerBuffState AfterBuffs,
            NpcContactBuff1458[] Effects)
        {
            internal PlayerStateSnapshot Accepted;
            internal RuntimePlayerItemPhase1458? AcceptedPhase;
            internal bool StealthChanged;
        }

        internal GroundCombatHitPreparation(PlayerAuthority owner, ProjectileSnapshot initial, NpcHandle sourceNpc, long tick,
            bool expert, bool master, bool goodWorld)
        {
            this.owner = owner; this.initial = initial; this.sourceNpc = sourceNpc; this.tick = tick;
            this.expert = expert; this.master = master; this.goodWorld = goodWorld;
            membershipSerial = owner.membership.Serial; inventorySerial = owner.inventory.Serial;
            updateWorld = owner.playerUpdateWorld;
            equipmentExpert = owner.combatEquipmentExpertMode; equipmentMaster = owner.combatEquipmentMasterMode;
            localVanity = owner.combatEquipmentLocalVanityArmor;
        }

        internal int CommittedHits => hits.Count;
        internal int Kills => hits.Count(h => h.Life == 0);
        internal int GodModeAvoidances { get; private set; }
        internal bool HasPlayer(int slot) => (uint)slot < members.Length && members[slot] is not null;

        internal bool IsCurrent
        {
            get
            {
                if (adopted || owner.membership.Serial != membershipSerial || owner.inventory.Serial != inventorySerial ||
                    owner.currentCombatTick != tick || owner.expertMode != expert || owner.masterMode != master ||
                    owner.playerUpdateWorld != updateWorld || owner.combatEquipmentExpertMode != equipmentExpert ||
                    owner.combatEquipmentMasterMode != equipmentMaster || owner.combatEquipmentLocalVanityArmor != localVanity)
                    return false;
                for (int slot = 0; slot < byte.MaxValue; slot++)
                {
                    if (members[slot] is not { } member) continue;
                    if (!owner.membership.TryGet(member.Connection, out var current) || !ReferenceEquals(member, current) ||
                        member.CaptureSnapshot() != before[slot]) return false;
                    if (profiles[slot] is { } profile && !owner.IsCurrentProjectileUse(profile) ||
                        incoming[slot] is { } capture && !owner.IsCurrentIncomingCombat(in capture) ||
                        buffs[slot] is { } beforeBuffs && !owner.transferProfiles.IsCurrentBuffState(member.Connection, beforeBuffs) ||
                        immunity[slot] is { } beforeImmunity && owner.damageImmunity.Capture(before[slot].Player) != beforeImmunity)
                        return false;
                }
                return true;
            }
        }

        internal bool TryStageTarget(int slot, in ProjectileSnapshot shot, in VanillaProjectileDefinition definition,
            VanillaUnifiedRandom1458 previewRandom, out bool evpContact)
        {
            evpContact = false;
            if (adopted || (uint)slot >= byte.MaxValue || shot.Handle != initial.Handle ||
                shot.Type != initial.Type || shot.Spawner != initial.Spawner) return false;
            if (members[slot] is null || before[slot].IsDead || shadowImmune[slot] || shot.Damage <= 0) return true;
            var target = before[slot];
            if (!GroundCombatIntersects(in shot, in definition, in target)) return true;
            if (incoming[slot] is not { } capture || buffs[slot] is not { } beforeBuffs) return false;
            if (immune[slot]) return true;
            int damage = VanillaIncomingPlayerDamageFacts1458.ResolveHostileProjectileDamage(shot.Damage,
                previewRandom.Next(-15, 16), Math.Min(3, (master ? 3 : expert ? 2 : 1) + (goodWorld ? 1 : 0)));
            if (damage <= 0) return true;
            evpContact = true;
            if (capture.GodMode)
            {
                GodModeAvoidances++;
                if (!avoided[slot]) { avoidances.Add(slot); avoided[slot] = true; }
                return true;
            }
            int direction = target.PositionX + VanillaBasePlayerWidth / 2 < shot.PositionX + definition.Width / 2 ? -1 : 1;
            var attack = new AuthoritativeAttackDamage(DamageSource.FromNpcProjectile(sourceNpc, shot.Handle),
                damage, 0, false, 4.5f, direction);
            if (!VanillaCombatDamagePipeline.TryResolvePlayerDamage(in attack, capture.Combat, false,
                    out var final, expert || goodWorld, master || expert && goodWorld) || final.Damage <= 0 ||
                !TryResolveIncomingLifeAfterDamage(in capture, final.Damage, out short life) ||
                tick > long.MaxValue - VanillaIncomingPlayerDamageFacts1458.ResolvePveImmunityTicks(final.Damage)) return false;
            var next = beforeBuffs.Clone();
            NpcContactBuff1458[] effects = [];
            if (life > 0)
            {
                var status = new ProjectilePlayerHurt1458(shot.Type, capture.Player.DerivedLifeMax!.Value, goodWorld, previewRandom);
                if (status.HasStatusAfterSurvivingHurt(final.Damage))
                {
                    var type = new BuffTypeId(31);
                    if (next.TryApplyNpcContactBuff(type, status.ConfusedDuration(expert, master), capture.Combat.IsImmuneToNpcContactBuff(type)))
                        effects = [new(type, next.GetDuration(type))];
                }
            }
            // The shared legacy death owner has no qualified KillMe/tombstone RNG tail.
            // Keep functional death/debit and no new status; never fabricate a cursor adjustment.
            hits.Add(new(slot, capture, final, life, direction, beforeBuffs, next, effects));
            shadowImmune[slot] = true;
            return true;
        }

        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            foreach (var hit in hits)
            {
                var member = members[hit.Slot]!;
                bool preservePhase = member.RemotePhaseSnapshot == hit.Incoming.Player &&
                    member.RemotePhaseItem == hit.Incoming.ItemPhase &&
                    member.RemotePhaseInputRevision == member.ProjectileUseInputRevision;
                if (!member.TryAdvanceRevision()) throw new InvalidOperationException("Preflighted projectile Hurt revision changed.");
                hit.StealthChanged = member.Stealth != 1f;
                member.Stealth = 1f;
                owner.AdoptPveLifeAndImmunity(member, hit.Incoming, hit.Final, hit.Life, hit.Direction,
                    tick, VanillaPlayerImmunityChannel1458.General);
                if (!owner.transferProfiles.TryAdoptBuffState(member.Connection, hit.BeforeBuffs, hit.AfterBuffs))
                    throw new InvalidOperationException("Preflighted projectile Hurt buff owner changed.");
                if (preservePhase) member.MarkRemotePhaseSnapshot();
                hit.Accepted = member.CaptureSnapshot(); hit.AcceptedPhase = member.ItemPhase;
            }
            adopted = true;
            return true;
        }

        internal void Publish()
        {
            if (!adopted || published) return;
            published = true;
            ExceptionDispatchInfo? failure = null;
            foreach (var hit in hits)
            {
                var member = members[hit.Slot]!;
                bool Current() => owner.membership.TryGet(member.Connection, out var current) &&
                    ReferenceEquals(member, current) && member.CaptureSnapshot() == hit.Accepted &&
                    member.ItemPhase == hit.AcceptedPhase && owner.transferProfiles.IsCurrentBuffState(member.Connection, hit.AfterBuffs);
                try
                {
                    if (!Current()) continue;
                    if (hit.StealthChanged) owner.events?.PlayerStealthUpdated(member.Connection, 1f);
                    if (!Current()) continue;
                    var health = new PlayerHealthCommitRequest(member.Slot, member.Life, member.MaxLife);
                    owner.events?.PlayerAuthoritativeHealthUpdated(member.Connection, in health);
                    if (Current()) owner.PublishNpcContactBuffs(member, hit.Effects);
                }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            foreach (int slot in avoidances)
            {
                var member = members[slot]!;
                if (!owner.membership.TryGet(member.Connection, out var current) || !ReferenceEquals(current, member) ||
                    member.CaptureSnapshot() != before[slot]) continue;
                try { owner.AvoidGodModeDamage(tick, before[slot].Player, member); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            failure?.Throw();
        }
    }

    internal static bool GroundCombatIntersects(in ProjectileSnapshot shot, in VanillaProjectileDefinition definition,
        in PlayerStateSnapshot target) =>
        (int)shot.PositionX < (int)target.PositionX + VanillaBasePlayerWidth &&
        (int)shot.PositionX + definition.Width > (int)target.PositionX &&
        (int)shot.PositionY < (int)target.PositionY + VanillaBasePlayerHeight &&
        (int)shot.PositionY + definition.Height > (int)target.PositionY;
}
