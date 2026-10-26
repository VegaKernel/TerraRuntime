using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // Source-qualified altar/cactus call, not an ingress for client damage.
    // Uses the existing represented Hurt mitigation context. Additional Hurt
    // equipment reactions/dodges remain a required combat-context integration.
    internal bool TryPrepareToolSelfHurt(PlayerToolAttemptPreparation tool,
        in ToolSelfHurtRequest1458 request, out ToolSelfHurtPreparation? preparation)
    {
        preparation = null;
        var actor = tool.Capture;
        if (!tool.IsCurrent || actor.CapturedUpdate != currentCombatTick ||
            request.Damage < 0 || request.HitDirection is < -1 or > 1 ||
            !(request.SourceOtherReason == 4 && request.ImmunityChannel == VanillaPlayerImmunityChannel1458.General ||
              request.SourceOtherReason == 3 && request.ImmunityChannel == VanillaPlayerImmunityChannel1458.TileContact &&
              request.HitDirection == 0) ||
            !TryCaptureIncomingCombat(actor.Connection.Player, out var incoming) ||
            incoming.Member is null || !incoming.IsAlive || incoming.Player != actor.Actor.Player ||
            incoming.ItemPhase != actor.Actor.ItemPhase) return false;
        bool immune = damageImmunity.IsPveImmune(incoming.Player.Player, request.ImmunityChannel, currentCombatTick);
        bool applies = !incoming.GodMode && !immune;
        FinalDamageToHp final = default;
        short life = incoming.Player.Life;
        long until = 0;
        if (applies)
        {
            if (incoming.Member.Revision >= ulong.MaxValue - 1 ||
                incoming.Member.ProjectileUseInputRevision >= ulong.MaxValue - 1) return false;
            var attack = new AuthoritativeAttackDamage(DamageSource.Environment,
                request.Damage, 0, false, 4.5f, request.HitDirection);
            if (!VanillaCombatDamagePipeline.TryResolvePlayerDamage(in attack, incoming.Combat, false,
                out final, expertMode, masterMode) || final.Damage <= 0 ||
                !TryResolveIncomingLifeAfterDamage(in incoming, final.Damage, out life)) return false;
            int duration = VanillaIncomingPlayerDamageFacts1458.ResolvePveImmunityTicks(final.Damage);
            if (currentCombatTick > long.MaxValue - duration) return false;
            until = currentCombatTick + duration;
        }
        preparation = new(this, tool, incoming, request, immune, applies, final, life, until);
        return preparation.IsCurrent;
    }

    internal sealed class ToolSelfHurtPreparation(PlayerAuthority owner,
        PlayerToolAttemptPreparation tool, IncomingPlayerCombatCapture1458 incoming,
        ToolSelfHurtRequest1458 request, bool wasImmune, bool applies,
        FinalDamageToHp final, short nextLife, long until)
    {
        private bool sealedForAdoption;
        private bool adopted;
        private int published;
        private bool stealthChanged;
        private bool motionChanged;
        private PlayerStateSnapshot accepted;
        private RuntimePlayerItemPhase1458? acceptedPhase;

        internal PlayerHandle SourcePlayer => incoming.Player.Player;
        internal bool ProjectedDead => applies ? nextLife <= 0 : incoming.Player.IsDead;

        internal bool IsCurrent => !adopted && tool.IsCurrent &&
            owner.IsCurrentIncomingCombat(in incoming) &&
            owner.damageImmunity.IsPveImmune(incoming.Player.Player, request.ImmunityChannel,
                tool.Capture.CapturedUpdate) == wasImmune;

        internal bool TrySealForJointAdoption()
        {
            if (sealedForAdoption || !IsCurrent) return false;
            sealedForAdoption = true;
            return true;
        }

        internal bool TryAdoptUnpublished()
        {
            if (!sealedForAdoption || adopted || !tool.IsAcceptedCurrent ||
                tool.AcceptedPlayer is not { } clockPlayer || tool.AcceptedPhase is not { } clockPhase ||
                incoming.ItemPhase is not { } oldPhase ||
                clockPlayer != (incoming.Player with
                    { Revision = new PlayerStateRevision(incoming.Player.Revision.Value + 1) }) ||
                clockPhase != (oldPhase with { ToolUse = clockPhase.ToolUse }) ||
                !owner.membership.TryGet(incoming.Player.Player, out var member) ||
                !ReferenceEquals(member, incoming.Member) ||
                owner.currentCombatTick != tool.Capture.CapturedUpdate ||
                owner.damageImmunity.IsPveImmune(incoming.Player.Player, request.ImmunityChannel,
                    owner.currentCombatTick) != wasImmune) return false;
            if (applies)
            {
                bool preservePhase = member.RemotePhaseSnapshot == clockPlayer &&
                    member.RemotePhaseItem == clockPhase &&
                    member.RemotePhaseInputRevision == member.ProjectileUseInputRevision;
                // All other participants have passed their final pure guards.
                // Do not call the immediate PvE commit: it invokes observers
                // before the terrain/items/clock transaction is complete.
                if (!member.TryAdvanceRevision()) return false;
                stealthChanged = member.Stealth != 1f;
                member.Stealth = 1f;
                AdoptIncomingLife(member, in incoming, nextLife);
                member.IsDead = nextLife <= 0;
                if (member.IsDead) member.ItemAnimation = 0;
                if (!final.Mitigation.NoKnockback && request.HitDirection != 0)
                {
                    member.VelocityX = 4.5f * request.HitDirection;
                    member.VelocityY = -3.5f;
                    motionChanged = true;
                }
                owner.damageImmunity.RecordPve(incoming.Player.Player, request.ImmunityChannel, until);
                // This is the guarded Hurt afterimage of the accepted tool phase.
                // Keep its certification through these known writes so the held
                // actor does not mistake canonical damage for a channel release.
                if (preservePhase) member.MarkRemotePhaseSnapshot();
            }
            accepted = member.CaptureSnapshot();
            acceptedPhase = member.ItemPhase;
            adopted = true;
            return true;
        }

        private bool IsPublicationCurrent => adopted &&
            owner.membership.TryGet(incoming.Player.Player, out var member) &&
            ReferenceEquals(member, incoming.Member) && member.CaptureSnapshot() == accepted &&
            member.ItemPhase == acceptedPhase;

        internal bool TryPublishNext()
        {
            if (!adopted || !applies) return false;
            while (published < 3)
            {
                int step = published++;
                if (!IsPublicationCurrent) return false;
                if (step == 0)
                {
                    if (!stealthChanged) continue;
                    owner.events?.PlayerStealthUpdated(tool.Capture.Connection, 1f);
                }
                else if (step == 1)
                {
                    if (!motionChanged) continue;
                    var movement = new PlayerMovementCommitRequest(accepted.Player.Slot,
                        accepted.ControlFlags, accepted.MovementFlags, accepted.MiscFlags1, accepted.MiscFlags2,
                        accepted.SelectedItem, accepted.PositionX, accepted.PositionY, true,
                        accepted.VelocityX, accepted.VelocityY, accepted.HasMount, accepted.MountType,
                        true, accepted.PotionOfReturnOriginalPositionX, accepted.PotionOfReturnOriginalPositionY,
                        accepted.PotionOfReturnHomePositionX, accepted.PotionOfReturnHomePositionY,
                        true, accepted.CameraTargetX, accepted.CameraTargetY);
                    owner.events?.PlayerMoved(tool.Capture.Connection, in movement);
                }
                else
                {
                    var health = new PlayerHealthCommitRequest(accepted.Player.Slot, accepted.Life, accepted.MaxLife);
                    owner.events?.PlayerAuthoritativeHealthUpdated(tool.Capture.Connection, in health);
                }
                return true;
            }
            return false;
        }
    }
}
