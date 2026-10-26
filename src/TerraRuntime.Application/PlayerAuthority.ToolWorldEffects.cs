using System.Drawing;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // WorldGen.SlopeTile/PoundTile (1458) displace every intersecting live player,
    // in wire-slot order, after framing. Preserve velocity and the physics phase:
    // this is a tile correction, not a teleport or another player update.
    internal bool TryPrepareToolShapeEffects(ReadOnlySpan<ToolWorldShapeEffect1458> effects,
        out ToolShapePlayerPreparation? preparation)
    {
        preparation = null;
        // Same explicit safety ceiling as the wider tool-world transaction, not
        // a vanilla limit. Refuse the entire action before adopting any owner.
        if (effects.Length > 4096) return false;
        var before = new PlayerStateSnapshot?[byte.MaxValue];
        var humans = new RuntimePlayerMember?[byte.MaxValue];
        var nextY = new float[byte.MaxValue];
        var changed = new bool[byte.MaxValue];
        ulong serial = membership.Serial;
        long update = currentCombatTick;
        for (int slot = 0; slot < byte.MaxValue; slot++)
        {
            bool human = membership.TryGet((byte)slot, out var member);
            bool server = serverPlayers is not null &&
                serverPlayers.TryGet(new PlayerSlotId((byte)slot), out var candidate) &&
                serverPlayers.TryGet(candidate.Player, out _);
            if (human && server) return false;
            if (human)
            {
                humans[slot] = member;
                before[slot] = member.CaptureSnapshot();
            }
            else if (server)
            {
                serverPlayers!.TryGet(new PlayerSlotId((byte)slot), out var snapshot);
                before[slot] = snapshot;
            }
            if (before[slot] is { } player)
            {
                if (!float.IsFinite(player.PositionX) || !float.IsFinite(player.PositionY) ||
                    !float.IsFinite(player.VelocityX) || !float.IsFinite(player.VelocityY)) return false;
                nextY[slot] = player.PositionY;
            }
        }
        foreach (var effect in effects)
        {
            if (!effect.RequiresPlayerLift) continue;
            if (worldTiles is null || effect.X < 0 || effect.Y < 0 ||
                effect.X >= worldTiles.Dimensions.WidthTiles || effect.Y >= worldTiles.Dimensions.HeightTiles) return false;
            var tile = new Rectangle(checked(effect.X * 16), checked(effect.Y * 16), 16, 16);
            for (int slot = 0; slot < byte.MaxValue; slot++)
            {
                if (before[slot] is not { IsDead: false } player) continue;
                var (width, height) = player.HasMount
                    ? VanillaPlayerMountHitbox1458.Resolve(player.MountType)
                    : (VanillaPlayerHitboxFacts.BaseWidth, VanillaPlayerHitboxFacts.BaseHeight);
                // Rectangle construction in the source truncates position toward
                // zero. Float overlap/floor changes contacts at negative edges.
                if (player.PositionX <= int.MinValue || player.PositionX >= int.MaxValue ||
                    nextY[slot] <= int.MinValue || nextY[slot] >= int.MaxValue) return false;
                var bounds = new Rectangle((int)player.PositionX, (int)nextY[slot], (int)width, (int)height);
                if (!tile.IntersectsWith(bounds)) continue;
                if (player.Revision.Value >= ulong.MaxValue - 1 ||
                    humans[slot] is { } movingHuman &&
                    movingHuman.ProjectileUseInputRevision >= ulong.MaxValue - 1) return false;
                nextY[slot] = tile.Y - height;
                changed[slot] = true;
            }
        }
        preparation = new(this, serial, update, before, humans, nextY, changed);
        return preparation.IsCurrent;
    }

    internal sealed class ToolShapePlayerPreparation(
        PlayerAuthority owner, ulong membershipSerial, long update,
        PlayerStateSnapshot?[] before, RuntimePlayerMember?[] humans,
        float[] nextY, bool[] changed)
    {
        private bool sealedForAdoption;
        private PlayerHandle clockOwner;
        private bool adopted;
        private int publicationIndex;
        private readonly PlayerStateSnapshot?[] accepted = new PlayerStateSnapshot?[byte.MaxValue];

        internal bool IsCurrent => !adopted && IsCurrentCensus(fullSnapshot: true);

        private bool IsCurrentCensus(bool fullSnapshot)
        {
            if (owner.membership.Serial != membershipSerial || owner.currentCombatTick != update) return false;
            // Capture negative slots too: a new server actor inside the edited
            // tile invalidates the plan even if every old actor is unchanged.
            for (int slot = 0; slot < byte.MaxValue; slot++)
            {
                bool human = owner.membership.TryGet((byte)slot, out var member);
                PlayerStateSnapshot? current = null;
                if (human)
                {
                    if (!ReferenceEquals(member, humans[slot])) return false;
                    current = member.CaptureSnapshot();
                }
                if (owner.serverPlayers is { } servers &&
                    servers.TryGet(new PlayerSlotId((byte)slot), out var server) &&
                    servers.TryGet(server.Player, out _))
                {
                    if (human || humans[slot] is not null) return false;
                    current = server;
                }
                if (fullSnapshot)
                {
                    if (current != before[slot]) return false;
                }
                else
                {
                    if (current != before[slot] &&
                        !(current is { } now && before[slot] is { } old &&
                        humans[slot] is not null && old.Player == clockOwner &&
                        old.Revision.Value < ulong.MaxValue &&
                        now == (old with { Revision = new PlayerStateRevision(old.Revision.Value + 1) }))) return false;
                }
                if (changed[slot] && current is { } moved &&
                    (moved.Revision.Value == ulong.MaxValue ||
                    humans[slot] is { ProjectileUseInputRevision: ulong.MaxValue })) return false;
            }
            return true;
        }

        // Call only at the final joint preflight, after every external provider.
        // The source tool clock is then adopted on the same actor before these
        // positions. Its revision/ItemPhase changes are expected, while any
        // geometry, identity or death change remains an invalidation.
        internal bool TrySealForJointAdoption(PlayerHandle toolActor)
        {
            if (sealedForAdoption || !toolActor.IsAssigned || toolActor.Slot.Value == byte.MaxValue ||
                humans[toolActor.Slot.Value]?.Connection.Player != toolActor || !IsCurrent) return false;
            clockOwner = toolActor;
            sealedForAdoption = true;
            return true;
        }

        // A world-owned shape change has no paid player clock to adopt first.
        internal bool TrySealForWorldAdoption()
        {
            if (sealedForAdoption || !IsCurrent) return false;
            clockOwner = default;
            sealedForAdoption = true;
            return true;
        }

        internal bool TryAdoptUnpublished()
        {
            if (!sealedForAdoption || adopted || !IsCurrentCensus(fullSnapshot: !clockOwner.IsAssigned)) return false;
            for (int slot = 0; slot < byte.MaxValue; slot++)
            {
                if (!changed[slot] || before[slot] is not { } old) continue;
                if (humans[slot] is { } member)
                {
                    bool preservePhase = member.RemotePhaseSnapshot == member.CaptureSnapshot() &&
                        member.RemotePhaseItem == member.ItemPhase &&
                        member.RemotePhaseInputRevision == member.ProjectileUseInputRevision;
                    if (!member.TryAdvanceRevision())
                        throw new InvalidOperationException("Preflighted tool position revision changed.");
                    member.PositionY = nextY[slot];
                    if (preservePhase) member.MarkRemotePhaseSnapshot();
                    accepted[slot] = member.CaptureSnapshot();
                }
                else
                {
                    if (!owner.serverPlayers!.TryAdoptToolShapePosition(old, nextY[slot], out var snapshot))
                        throw new InvalidOperationException("Preflighted server tool position changed.");
                    accepted[slot] = snapshot;
                }
            }
            adopted = true;
            return true;
        }

        internal bool TryPublishNext()
        {
            if (!adopted) return false;
            while (publicationIndex < accepted.Length)
            {
                int slot = publicationIndex++;
                if (accepted[slot] is not { } state) continue;
                if (humans[slot] is { } member)
                {
                    if (!owner.membership.TryGet(member.Connection, out var current) ||
                        !ReferenceEquals(member, current) || current.CaptureSnapshot() != state) continue;
                    var movement = new PlayerMovementCommitRequest(state.Player.Slot,
                        state.ControlFlags, state.MovementFlags, state.MiscFlags1, state.MiscFlags2,
                        state.SelectedItem, state.PositionX, state.PositionY, true,
                        state.VelocityX, state.VelocityY, state.HasMount, state.MountType,
                        true, state.PotionOfReturnOriginalPositionX, state.PotionOfReturnOriginalPositionY,
                        state.PotionOfReturnHomePositionX, state.PotionOfReturnHomePositionY,
                        true, state.CameraTargetX, state.CameraTargetY);
                    owner.events?.PlayerMoved(member.Connection, in movement);
                }
                else owner.serverPlayers!.TryPublishToolShapePosition(in state);
                return true;
            }
            return false;
        }
    }
}
