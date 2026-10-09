namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool TryRefreshPendingBulletUseCapture(RuntimePlayerProjectileUseCapture capture,
        out RuntimePlayerProjectileUseCapture current)
    {
        current = capture;
        if (IsCurrentProjectileUse(capture)) return true;
        if (!membership.TryGet(capture.Connection, out var member) || !ReferenceEquals(member, capture.Member) ||
            member.Revision == ulong.MaxValue || member.ProjectileUseInputRevision == ulong.MaxValue ||
            member.ProjectileUseInputRevision != capture.InputRevision ||
            member.RemotePhaseInputRevision != capture.InputRevision ||
            member.RemotePhaseSnapshot is not { } ownedPhase || member.CaptureSnapshot() != ownedPhase ||
            member.ItemPhase != member.RemotePhaseItem ||
            !inventory.TryIsCurrent(capture.Connection, capture.InventorySerial)) return false;

        // Refresh only the currentness proof. The caller keeps the original launch pose, reports,
        // ammunition decision and detached projectile cursor. Immediate combat guards stay strict.
        var refreshed = capture with { Player = ownedPhase, ItemPhase = member.RemotePhaseItem };
        if (!IsCurrentProjectileUse(refreshed)) return false;
        current = refreshed;
        return true;
    }
}
