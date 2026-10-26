using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    // One transient input candidate per owned birth slot. This is neither a
    // physical projectile update nor an authorization to create/retire a use.
    private readonly ToolControlledInput1458?[] toolControlledInputs =
        new ToolControlledInput1458?[RuntimeProjectileStore.MaximumProtocolAddressableCapacity];

    private sealed record ToolControlledInput1458(ToolActorOwner1458 Binding,
        TerrariaProjectileUpdateState Report, PlayerStateSnapshot OwnerPose, ulong InputRevision);

    private readonly ToolActorOwner1458?[] toolStopInputs =
        new ToolActorOwner1458?[RuntimeProjectileStore.MaximumProtocolAddressableCapacity];

    // Packet29 is only a STOP/channel intent for this exact held birth. The next
    // source AI executes cancellation and Dummy2; ingress does not remove it or
    // authorize hits, tile edits, a new use or a replacement generation.
    private bool TryRetainTrustedToolStop(ConnectionHandle connection, ProjectileHandle handle,
        in ProjectileSnapshot current)
    {
        if (handle.Slot >= toolActorOwners.Length || current.Handle != handle ||
            toolActorOwners[handle.Slot] is not { } binding || binding.Projectile != handle ||
            binding.Player != connection.Player || !VanillaHeldToolProjectile1458.IsSupported(current.Type) ||
            !players.TryGet(binding.Player, out var member) || !ReferenceEquals(member, binding.Member) ||
            !projectiles.TryGetCombatTrustedOwner(handle, out var trusted) || trusted != binding.Player ||
            !projectiles.TryGetLifecycle(handle, out var lifecycle) || lifecycle.HeldTool is null ||
            lifecycle.TimeLeft <= 0 || lifecycle.Reflected) return false;
        toolStopInputs[handle.Slot] = binding;
        return true;
    }

    private bool TryRetainTrustedToolInput(ConnectionHandle connection, ProjectileHandle handle,
        in ProjectileSnapshot current, in TerrariaProjectileUpdateState packet)
    {
        if (handle.Slot >= toolActorOwners.Length ||
            toolActorOwners[handle.Slot] is not { } binding || binding.Projectile != handle ||
            binding.Player != connection.Player || current.Handle != handle ||
            !VanillaHeldToolProjectile1458.IsSupported(current.Type) ||
            !players.TryGet(binding.Player, out var member) || !ReferenceEquals(member, binding.Member) ||
            !projectiles.TryGetCombatTrustedOwner(handle, out var trusted) || trusted != binding.Player ||
            packet.Key.Spawner != binding.Player.Slot.Value || packet.ProjectileType != current.Type.Value ||
            packet.OriginalDamage != current.OriginalDamage || packet.Damage != current.Damage ||
            packet.KnockBack != current.KnockBack || packet.BannerIdToRespondTo != current.BannerIdToRespondTo ||
            BitConverter.SingleToInt32Bits(packet.Ai0) != 0 ||
            BitConverter.SingleToInt32Bits(packet.Ai1) != 0 ||
            BitConverter.SingleToInt32Bits(packet.Ai2) != 0 ||
            !float.IsFinite(packet.PositionX) || !float.IsFinite(packet.PositionY) ||
            !float.IsFinite(packet.VelocityX) || !float.IsFinite(packet.VelocityY)) return false;
        var pose = member.CaptureSnapshot();
        if (pose.HasMount || pose.IsDead) return false;
        toolControlledInputs[handle.Slot] = new(binding, packet, pose, member.ProjectileUseInputRevision);
        return true;
    }

    // Source-preimage proof, not a claim to recover MouseWorld. The candidate
    // supplies only a direction represented by the local AI. Its reported
    // physical tuple and its visual RNG outcome are never copied into the store.
    private bool TryPrepareToolControlledAim(ToolActorOwner1458 binding,
        HeldToolOwnerPreparation1458 owner, in ProjectileSnapshot before,
        in HeldToolProjectileState1458 held, bool windows, int tileRangeX, int tileRangeY, int tileBoost,
        out HeldToolOwnerFacts1458 aim, out ToolControlledInput1458? candidate)
    {
        aim = default; candidate = null;
        if (before.Handle.Slot >= toolControlledInputs.Length ||
            toolControlledInputs[before.Handle.Slot] is not { } input ||
            !ReferenceEquals(input.Binding, binding) || input.Binding.Projectile != before.Handle ||
            input.InputRevision != binding.Member.ProjectileUseInputRevision || !owner.IsCurrent ||
            owner.Inactive || !owner.HasCurrentPhase || owner.Before.HasMount ||
            !VanillaToolCatalog1458.TryResolve(binding.Item, binding.Prefix, owner.World.RemixWorld,
                windows, out var tool) || tool.Shoot != before.Type.Value ||
            !VanillaToolProjectileDefinition1458.TryGet(before.Type, out var definition) ||
            tileRangeX is < 0 or > 100 || tileRangeY is < 0 or > 100 || tileBoost is < -20 or > 100) return false;
        var report = input.Report;
        float sourceX = input.OwnerPose.PositionX + 10f;
        float sourceY = input.OwnerPose.PositionY + 21f;
        float halfWidth = before.Type.Value == 445 ? definition.Width / 2f : definition.Width / 2;
        float halfHeight = before.Type.Value == 445 ? definition.Height / 2f : definition.Height / 2;
        if (!ToolInputNear(report.PositionX, sourceX - halfWidth + report.VelocityX) ||
            !ToolInputNear(report.PositionY, sourceY - halfHeight + report.VelocityY)) return false;
        float mx = owner.Before.PositionX + 10f, my = owner.Before.PositionY + 21f;
        int first = before.Type.Value == 445 ? 0 : -3;
        int last = before.Type.Value == 445 ? 0 : 3;
        for (int jitter = first; jitter <= last; jitter++)
        {
            float dx, dy;
            if (before.Type.Value == 445)
            {
                // Lerp(aim,oldVelocity,.7). A post-clamp vector is itself a
                // possible unclamped vector; the pure kernel validates it again.
                dx = (float)((report.VelocityX - held.VelocityX * (double).7f) / (1d - .7f));
                dy = (float)((report.VelocityY - held.VelocityY * (double).7f) / (1d - .7f));
            }
            else
            {
                double factor = windows ? 1d + jitter * (double).01f : 1f + jitter * .01f;
                dx = (float)(report.VelocityX / factor); dy = report.VelocityY;
            }
            if (!float.IsFinite(dx) || !float.IsFinite(dy) || dx == 0f && dy == 0f) continue;
            var facts = new HeldToolOwnerFacts1458(mx, my, true, true, owner.NoItems,
                owner.CrowdControlled, owner.SelectedPick, tool.ShootSpeed, mx + dx, my + dy,
                false, 0, 0, tileRangeX, tileRangeY, tileBoost, windows);
            int draw = 0;
            int sourceJitter = jitter;
            int Sample(int min, int max) => draw++ == 0 ? sourceJitter : 1;
            if (!VanillaHeldToolProjectile1458.TryStepAi(before.Type, definition.Width, definition.Height,
                    in held, in facts, Sample, out var proof) || proof.SourceKill ||
                !ToolInputNear(proof.State.VelocityX, report.VelocityX) ||
                !ToolInputNear(proof.State.VelocityY, report.VelocityY)) continue;
            aim = facts; candidate = input; return true;
        }
        return false;
    }

    private bool TryCaptureToolControlledReach(ToolActorOwner1458 binding,
        HeldToolOwnerPreparation1458 owner, ProjectileTypeId type, out RuntimePlayerToolReach1458 reach)
    {
        reach = default;
        // AI20 does not read reach or target coordinates. AI75 must use the
        // exact represented player projection, never crafting/Artisan ranges.
        if (type.Value != 445) return true;
        return players.TryCaptureToolActor(binding.Member.Connection, out var capture) && capture is not null &&
            ReferenceEquals(capture.Actor.Member, binding.Member) && capture.Actor.Player == owner.Before &&
            players.TryCaptureToolReach(capture, out reach) && capture.IsCurrent && owner.IsCurrent;
    }

    private static bool ToolInputNear(float left, float right) =>
        float.IsFinite(left) && float.IsFinite(right) && MathF.Abs(left - right) <= .002f;
}
