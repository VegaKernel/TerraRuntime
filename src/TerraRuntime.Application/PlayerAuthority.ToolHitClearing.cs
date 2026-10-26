using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // Mirrors the CLIENT packet17 successful-kill cache notification for the
    // represented local cache of every receiving player. The sender's own local
    // mining path already did ClearMiningCacheAt and does not receive its echo.
    internal bool TryClearOtherToolHits(PlayerHandle excluded, int x, int y, WorldTileAuthority world)
    {
        ulong serial = membership.Serial;
        var changes = new List<ToolHitClear1458>();
        Span<bool> presence = stackalloc bool[ToolHitState1458.SlotCount];
        for (int slot = 0; slot < byte.MaxValue; slot++)
        {
            if (!membership.TryGet((byte)slot, out var member) ||
                member.Connection.Player == excluded || member.ItemPhase is not { ToolUse: { } tools } phase)
                continue;
            var before = member.CaptureSnapshot();
            var reads = world.CreateToolReadPreparation();
            if (reads is null || !reads.TryCapturePruneBefore(tools.Hits, presence) ||
                !tools.Hits.TryClearThisTile(x, y, presence, out var after) || !reads.IsCurrent) return false;
            if (ReferenceEquals(after, tools.Hits)) continue;
            var input = tools.Input;
            bool preserve = input is { IsCurrent: true } && input.Actor is not null;
            changes.Add(new(member, before, phase, after,
                member.RemotePhaseSnapshot == before && member.RemotePhaseItem == phase &&
                    member.RemotePhaseInputRevision == member.ProjectileUseInputRevision,
                input, input?.Actor, input?.Revision ?? 0, preserve));
        }
        if (membership.Serial != serial) return false;
        foreach (var change in changes)
            if (!membership.TryGet(change.Before.Player, out var current) ||
                !ReferenceEquals(current, change.Member) || current.CaptureSnapshot() != change.Before ||
                current.ItemPhase != change.Phase ||
                change.Input is { } input && input.Revision != change.InputRevision) return false;

        // No external provider or observer runs in this private cache tail.
        // Preserve only the exact known Hits afterimage; never resnapshot an
        // arbitrary callback's player/control/inventory changes as trusted input.
        foreach (var change in changes)
        {
            var member = change.Member;
            if (change.Input is { } stale && !change.PreserveInput) RetireToolInput(member, stale);
            var phase = member.ItemPhase!.Value;
            member.ItemPhase = phase with { ToolUse = phase.ToolUse! with { Hits = change.After } };
            if (change.RemoteCurrent) member.MarkRemotePhaseSnapshot();
            if (change.PreserveInput && change.Input is { } input && change.Actor is { } actor &&
                (!TryCaptureToolActor(member.Connection, out var accepted) || accepted is null ||
                 !input.TryRebaseAfterOwnedPhase(actor, change.InputRevision, accepted)))
                RetireToolInput(member, input);
        }
        return true;
    }

    private readonly record struct ToolHitClear1458(RuntimePlayerMember Member, PlayerStateSnapshot Before,
        RuntimePlayerItemPhase1458 Phase, ToolHitState1458 After, bool RemoteCurrent,
        RuntimePlayerToolInput1458? Input, RuntimePlayerToolActorCapture1458? Actor,
        ulong InputRevision, bool PreserveInput);
}
