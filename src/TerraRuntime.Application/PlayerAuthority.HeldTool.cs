using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed class PlayerHeldToolClockPreparation1458
{
    private readonly PlayerAuthority owner;
    private readonly RuntimePlayerToolActorCapture1458 capture;
    private readonly ProjectileHandle projectile;
    private bool adopted;
    private int? ownerDirection;
    private float? ownerRotation;

    internal PlayerHeldToolClockPreparation1458(PlayerAuthority owner,
        RuntimePlayerToolActorCapture1458 capture, ProjectileHandle projectile)
    {
        this.owner = owner;
        this.capture = capture;
        this.projectile = projectile;
    }

    internal RuntimePlayerToolActorCapture1458 Capture => capture;
    internal bool IsCurrent => !adopted && capture.IsCurrent &&
        capture.Phase.ToolUse?.HeldProjectile == projectile;

    internal bool TryStageSourceOwner(int direction, float itemRotation)
    {
        if (!IsCurrent || ownerDirection.HasValue || direction is not (-1 or 1) ||
            !float.IsFinite(itemRotation)) return false;
        ownerDirection = direction;
        ownerRotation = itemRotation;
        return true;
    }

    internal bool TryAdoptUnpublished()
    {
        if (!IsCurrent || !owner.TryAdoptHeldToolDummyTimeUnpublished(capture, projectile,
            ownerDirection, ownerRotation))
            return false;
        adopted = true;
        return true;
    }
}

internal sealed partial class PlayerAuthority
{
    internal bool TryCaptureHeldToolOwner(PlayerHandle player, ProjectileHandle projectile,
        ProjectileTypeId type, out RuntimePlayerToolActorCapture1458? capture)
    {
        capture = null;
        if (!membership.TryGet(player, out var member) ||
            !TryCaptureToolActor(member.Connection, out var candidate, allowDead: true) ||
            candidate is null || candidate.Phase.ToolUse is not { } tools ||
            tools.HeldProjectile != projectile || tools.HeldProjectileType != type.Value)
            return false;
        capture = candidate;
        return true;
    }

    internal bool TryPrepareHeldToolDummyTime(RuntimePlayerToolActorCapture1458 capture,
        ProjectileHandle projectile, out PlayerHeldToolClockPreparation1458? preparation)
    {
        preparation = null;
        if (!capture.IsCurrent || capture.Phase.ToolUse?.HeldProjectile != projectile)
            return false;
        preparation = new(this, capture, projectile);
        return true;
    }

    internal bool TryAdoptHeldToolDummyTimeUnpublished(RuntimePlayerToolActorCapture1458 capture,
        ProjectileHandle projectile, int? direction = null, float? rotation = null)
    {
        if (direction.HasValue != rotation.HasValue ||
            direction is not null and not (-1 or 1) ||
            rotation.HasValue && !float.IsFinite(rotation.Value) ||
            !capture.IsCurrent || !membership.TryGet(capture.Connection, out var member) ||
            member.ItemPhase is not { ToolUse: { } tools } phase ||
            tools.HeldProjectile != projectile || !member.TryAdvanceRemotePhaseRevision())
            return false;
        // Source AI20 calls SetDummyItemTime(2) even on its final AI call. It
        // leaves animationMax, toolTime and the HitTile progress untouched.
        var local = tools.Clocks.Selected with { Animation = 2, ItemTime = 2, ItemTimeMax = 3 };
        member.ItemPhase = phase with
        {
            Selected = phase.Selected with { Animation = 2, ItemTime = 2, ItemTimeMax = 3 },
            ToolUse = tools with { Clocks = tools.Clocks with { Selected = local } }
        };
        member.ItemAnimation = 2;
        if (direction is { } sourceDirection)
        {
            member.ControlFlags = (byte)((member.ControlFlags & ~64) | (sourceDirection > 0 ? 64 : 0));
            member.ItemRotation = rotation!.Value;
        }
        member.MarkRemotePhaseSnapshot();
        return true;
    }
}
