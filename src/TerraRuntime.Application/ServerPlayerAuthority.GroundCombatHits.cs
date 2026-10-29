using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    internal bool TryPrepareGroundCombatHits(in ProjectileSnapshot initial, NpcHandle sourceNpc,
        long tick, bool expertMode, bool masterMode, bool goodWorld,
        VanillaProjectileWorldStateStepper.OrdinaryArrowTerrainCapture terrain,
        out GroundCombatHitPreparation? preparation)
        => GroundCombatHitPreparation.TryCreate(this, in initial, sourceNpc, tick, expertMode, masterMode, goodWorld,
            terrain, out preparation);

    private bool HasRepresentedGroundCombatEffects(PlayerHandle player)
    {
        int slot = player.Slot.Value;
        if (npcContactBuffOwners[slot] != player || npcContactBuffStates[slot] is not { } buffs) return true;
        // These flags do not modify the admitted incoming defense/knockback
        // snapshot. Weak/BrokenArmor and other unowned stat effects cannot use it.
        foreach (var type in buffs.CaptureTypes())
            if (!Represented(type)) return false;
        foreach (var type in buffs.CaptureEffectTypes())
            if (!Represented(type)) return false;
        return true;
        static bool Represented(BuffTypeId type) => type.Value is 20 or 22 or 24 or 30 or 31 or 32 or 35 or 39 or 144;
    }

    private sealed record GroundCombatBuffImage(PlayerHandle Owner, PlayerBuffState? Reference,
        PlayerBuffState? Copy, PlayerDebuffSnapshot1458 Effects, LavaState Lava,
        PlayerHandle LeechOwner, int LeechTicks, bool LeechFirst);

    private GroundCombatBuffImage CaptureGroundCombatBuffs(PlayerHandle player)
    {
        int slot = player.Slot.Value;
        var buffs = npcContactBuffStates[slot];
        return new(npcContactBuffOwners[slot], buffs, buffs?.Clone(), npcContactDebuffEffects[slot],
            lavaStates[slot], moonLeechOwners[slot], moonLeechTicks[slot], moonLeechBeforeBurning[slot]);
    }

    private bool GroundCombatBuffsCurrent(PlayerHandle player, GroundCombatBuffImage expected)
    {
        int slot = player.Slot.Value;
        var lava = lavaStates[slot];
        return npcContactBuffOwners[slot] == expected.Owner &&
            ReferenceEquals(npcContactBuffStates[slot], expected.Reference) &&
            (expected.Copy is null || expected.Reference!.HasSameSlots(expected.Copy)) &&
            npcContactDebuffEffects[slot] == expected.Effects && lava.Owner == expected.Lava.Owner &&
            lava.Protection == expected.Lava.Protection && lava.BurningTicks == expected.Lava.BurningTicks &&
            lava.RegenLoss == expected.Lava.RegenLoss && lava.PublishedBurning == expected.Lava.PublishedBurning &&
            moonLeechOwners[slot] == expected.LeechOwner && moonLeechTicks[slot] == expected.LeechTicks &&
            moonLeechBeforeBurning[slot] == expected.LeechFirst;
    }

    private sealed class GroundCombatPlayerImage(PlayerStateSnapshot before, bool reachable,
        VanillaPlayerCombatSnapshot combat, GroundCombatBuffImage buffs,
        RuntimePlayerDamageImmunityStore.Capture1458 immunity, bool immune)
    {
        internal readonly PlayerStateSnapshot Before = before;
        internal readonly bool Reachable = reachable;
        internal readonly VanillaPlayerCombatSnapshot Combat = combat;
        internal readonly GroundCombatBuffImage Buffs = buffs;
        internal readonly RuntimePlayerDamageImmunityStore.Capture1458 Immunity = immunity;
        internal bool Immune = immune;
        internal bool HasHit;
        internal FinalDamageToHp Damage;
        internal int Direction;
        internal PlayerBuffState? PreparedBuffs;
        internal PlayerStateSnapshot Accepted;
        internal GroundCombatBuffImage? AcceptedBuffs;
        internal RuntimePlayerDamageImmunityStore.Capture1458 AcceptedImmunity;
    }

    internal sealed class GroundCombatHitPreparation
    {
        internal static bool TryCreate(ServerPlayerAuthority owner, in ProjectileSnapshot initial, NpcHandle sourceNpc,
            long tick, bool expertMode, bool masterMode, bool goodWorld,
            VanillaProjectileWorldStateStepper.OrdinaryArrowTerrainCapture terrain,
            out GroundCombatHitPreparation? preparation)
        {
            preparation = null;
            if (!VanillaGroundCombatPlayerHit1458.IsSupported(initial.Type) || !sourceNpc.IsAssigned ||
                masterMode && !expertMode || !terrain.IsCurrent) return false;
            var captured = new PlayerStateSnapshot[owner.states.Capacity];
            int count = owner.states.CopySnapshots(captured);
            var entries = new GroundCombatPlayerImage?[byte.MaxValue + 1];
            for (int i = 0; i < count; i++)
            {
                var player = captured[i];
                if (player.HasMount && player.MountType > 65) return false;
                var body = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) : (20, 42);
                bool reachable = terrain.MayReach(player.PositionX, player.PositionY, (int)body.Item1, (int)body.Item2);
                VanillaPlayerCombatSnapshot combat = default;
                if (reachable && !player.IsDead && initial.Damage > 0 &&
                    (player.HasMount || !player.HasHealth || player.Life <= 0 || player.Revision.Value > ulong.MaxValue - 2 ||
                     !owner.CanOwnGroundCombatHurt(in player) ||
                     !player.GodMode && (!owner.TryCaptureCombatSnapshot(player.Player, out combat) || !owner.HasRepresentedGroundCombatEffects(player.Player))))
                    return false;
                entries[player.Player.Slot.Value] = new(player, reachable, combat,
                    owner.CaptureGroundCombatBuffs(player.Player), owner.damageImmunity.Capture(player.Player),
                    owner.IsGeneralPveImmune(player.Player, tick));
            }
            var candidate = new GroundCombatHitPreparation(owner, initial, sourceNpc, tick, expertMode,
                masterMode, goodWorld, terrain, captured[..count], entries,
                owner.combatEquipmentExpertMode, owner.combatEquipmentMasterMode, owner.combatEquipmentLocalVanityArmor);
            if (!candidate.IsCurrent) return false;
            preparation = candidate;
            return true;
        }
        private readonly ServerPlayerAuthority owner;
        private readonly ProjectileSnapshot initial;
        private readonly NpcHandle sourceNpc;
        private readonly long tick;
        private readonly bool expert, master, good;
        private readonly VanillaProjectileWorldStateStepper.OrdinaryArrowTerrainCapture terrain;
        private readonly PlayerStateSnapshot[] census;
        private readonly GroundCombatPlayerImage?[] entries;
        private readonly PlayerStateSnapshot[] scratch;
        private readonly bool equipmentExpert, equipmentMaster;
        private readonly VanillaPlayerLocalVanityArmor1458? vanity;
        private readonly List<(GroundCombatPlayerImage Target, ResolvedDamagePublication Publication)> journal = [];
        private bool adopted, published;

        private GroundCombatHitPreparation(ServerPlayerAuthority owner, ProjectileSnapshot initial, NpcHandle sourceNpc,
            long tick, bool expert, bool master, bool good,
            VanillaProjectileWorldStateStepper.OrdinaryArrowTerrainCapture terrain,
            PlayerStateSnapshot[] census, GroundCombatPlayerImage?[] entries,
            bool equipmentExpert, bool equipmentMaster, VanillaPlayerLocalVanityArmor1458? vanity)
        {
            this.owner = owner; this.initial = initial; this.sourceNpc = sourceNpc; this.tick = tick;
            this.expert = expert; this.master = master; this.good = good; this.terrain = terrain;
            this.census = census; this.entries = entries; this.equipmentExpert = equipmentExpert;
            this.equipmentMaster = equipmentMaster; this.vanity = vanity;
            scratch = new PlayerStateSnapshot[owner.states.Capacity];
        }

        internal int CommittedHits { get; private set; }
        internal int Kills { get; private set; }
        internal int GodModeAvoidances { get; private set; }
        internal bool HasPlayer(int slot) => (uint)slot < entries.Length && entries[slot] is not null;
        internal bool IsCurrent
        {
            get
            {
                if (adopted || !terrain.IsCurrent || owner.combatEquipmentExpertMode != equipmentExpert ||
                    owner.combatEquipmentMasterMode != equipmentMaster || owner.combatEquipmentLocalVanityArmor != vanity ||
                    owner.states.CopySnapshots(scratch) != census.Length || !scratch.AsSpan(0, census.Length).SequenceEqual(census))
                    return false;
                foreach (var entry in entries)
                    if (entry is not null && (!owner.GroundCombatBuffsCurrent(entry.Before.Player, entry.Buffs) ||
                        owner.damageImmunity.Capture(entry.Before.Player) != entry.Immunity)) return false;
                return true;
            }
        }

        internal bool TryStageTarget(int slot, in ProjectileSnapshot shot, in VanillaProjectileDefinition definition,
            VanillaUnifiedRandom1458 previewRandom, out bool evpContact)
        {
            evpContact = false;
            if (!HasPlayer(slot) || adopted || shot.Handle != initial.Handle || shot.Type != initial.Type ||
                shot.Spawner != initial.Spawner || shot.Damage != initial.Damage) return false;
            var target = entries[slot]!;
            var player = target.Before;
            if (player.IsDead || target.Immune || shot.Damage <= 0) return true;
            if (!PlayerAuthority.GroundCombatIntersects(in shot, in definition, in player)) return true;
            if (!target.Reachable) return false;
            int damage = VanillaIncomingPlayerDamageFacts1458.ResolveHostileProjectileDamage(shot.Damage,
                previewRandom.Next(-15, 16), Math.Min(3, (master ? 3 : expert ? 2 : 1) + (good ? 1 : 0)));
            if (damage <= 0) return true;
            evpContact = true;
            if (player.GodMode) { GodModeAvoidances++; return true; }
            int direction = player.PositionX + 10f < shot.PositionX + definition.Width / 2 ? -1 : 1;
            var attack = new AuthoritativeAttackDamage(DamageSource.FromNpcProjectile(sourceNpc, shot.Handle),
                damage, 0, false, 4.5f, direction);
            if (!VanillaCombatDamagePipeline.TryResolvePlayerDamage(in attack, target.Combat, false, out var final,
                    expert || good, master || expert && good) || final.Damage <= 0) return false;
            target.HasHit = true; target.Damage = final; target.Direction = direction; target.Immune = true;
            CommittedHits++;
            if (ResolveLifeAfterDamage(in player, in final) == 0) { Kills++; return true; }
            var status = new ProjectilePlayerHurt1458(shot.Type, player.DerivedLifeMax!.Value, good, previewRandom);
            if (status.TryResolveStatusAfterSurvivingHurt(final.Damage, expert, master, out var type, out int duration) &&
                !target.Combat.IsImmuneToNpcContactBuff(type))
            {
                var next = target.Buffs.Owner == player.Player ? target.Buffs.Copy?.Clone() ?? new() : new PlayerBuffState();
                if (next.TryApplyNpcContactBuff(type, duration)) target.PreparedBuffs = next;
            }
            return true;
        }

        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            // All sources and revision capacity were checked before this callback-free block.
            // The shared HP owner remains the only writer of life, knockback and immunity.
            foreach (var target in entries)
            {
                if (target is null || !target.HasHit) continue;
                var publications = new List<ResolvedDamagePublication>(1);
                var before = target.Before;
                var result = owner.CommitResolvedDamage(tick, in before,
                    DamageSource.FromNpcProjectile(sourceNpc, initial.Handle), initial.Type, in target.Damage,
                    target.Direction, VanillaPlayerImmunityChannel1458.General, false, false,
                    out var accepted, targetCombat: target.Combat, expertMode: expert, masterMode: master,
                    deferredPublications: publications, preparedProjectileBuffs: target.PreparedBuffs);
                if (result != PlayerDamageCommitResult.Committed)
                    throw new InvalidOperationException("A captured server-player HP owner changed without a callback.");
                target.Accepted = accepted; target.AcceptedBuffs = owner.CaptureGroundCombatBuffs(accepted.Player);
                target.AcceptedImmunity = owner.damageImmunity.Capture(accepted.Player);
                journal.Add((target, publications[0]));
            }
            adopted = true;
            return true;
        }

        internal void Publish()
        {
            if (!adopted || published) return;
            published = true;
            foreach (var entry in journal)
            {
                var target = entry.Target;
                owner.PublishResolvedDamage(entry.Publication, () =>
                    owner.GroundCombatBuffsCurrent(target.Accepted.Player, target.AcceptedBuffs!) &&
                    owner.damageImmunity.Capture(target.Accepted.Player) == target.AcceptedImmunity);
            }
        }
    }
}
