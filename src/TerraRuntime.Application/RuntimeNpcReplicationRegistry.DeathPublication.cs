using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    private RetainedDeathPublication? activeDeathPublication;

    internal bool TryPrepareRetainedDeath(RuntimeNpcStore.TerminalRemoval removal,
        out RetainedDeathPublication? publication)
    {
        publication = null;
        var terminal = removal.Expected;
        if (!RuntimeNpcPacketProjection.TryCreate(in terminal, RuntimeNpcSyncKind.Despawn, out var state) ||
            !TerrariaNpcUpdateEncoder.TryEncode(in state, out var terminalFrame)) return false;
        byte[]? birthFrame = null;
        NpcSnapshot? birth = null;
        if (removal.TryCapturePendingBirth(out var pending))
        {
            if (!RuntimeNpcPacketProjection.TryCreate(in pending, RuntimeNpcSyncKind.Spawn, out state) ||
                !TerrariaNpcUpdateEncoder.TryEncode(in state, out birthFrame)) return false;
            birth = pending;
        }
        publication = new(this, removal, terminal, terminalFrame, birth, birthFrame);
        return true;
    }

    // Historical pre-strike birth and final inactive wire belong to the accepted death,
    // not to join baselines. Scope the normal store fanout and keep a network fallback
    // when an earlier operations observer throws before replication is reached.
    internal sealed class RetainedDeathPublication(RuntimeNpcReplicationRegistry owner,
        RuntimeNpcStore.TerminalRemoval removal, NpcSnapshot terminal, byte[] terminalFrame,
        NpcSnapshot? birth, byte[]? birthFrame)
    {
        private bool birthAttempted;
        private bool terminalAttempted;
        private bool birthPublished;
        private bool terminalPublished;

        internal bool Matches(NpcStateCommitKind kind, in NpcSnapshot snapshot) => kind switch
        {
            NpcStateCommitKind.Spawn => birth is { } expected && snapshot == expected,
            NpcStateCommitKind.Despawn => snapshot == terminal,
            _ => false
        };

        internal bool TryPublishFrame(NpcStateCommitKind kind)
        {
            byte[]? frame;
            if (kind == NpcStateCommitKind.Spawn)
            {
                if (birthPublished) return false;
                birthPublished = true;
                frame = birthFrame;
            }
            else
            {
                if (terminalPublished) return false;
                terminalPublished = true;
                frame = terminalFrame;
            }
            if (frame is not null && removal.IsAcceptedCurrent) owner.Broadcast(frame);
            return true;
        }

        internal bool TryPublishPendingBirth() => Publish(pendingBirth: true);
        internal bool TryPublishTerminal() => Publish(pendingBirth: false);

        private bool Publish(bool pendingBirth)
        {
            if (pendingBirth ? birthAttempted : terminalAttempted) return false;
            if (pendingBirth) birthAttempted = true;
            else terminalAttempted = true;
            var previous = owner.activeDeathPublication;
            owner.activeDeathPublication = this;
            ExceptionDispatchInfo? failure = null;
            try
            {
                if (pendingBirth) removal.TryPublishPendingBirthBeforeStrike();
                else removal.TryPublish();
            }
            catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
            finally { owner.activeDeathPublication = previous; }
            TryPublishFrame(pendingBirth ? NpcStateCommitKind.Spawn : NpcStateCommitKind.Despawn);
            failure?.Throw();
            return true;
        }
    }
}
