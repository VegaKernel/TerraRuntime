using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    private sealed class Endpoint(TerrariaConnectionOutboundQueue outbound)
    {
        private readonly object proximityStreamGate = new();
        // Source playerNetSyncState is bounded by physical NPC slots. Full runtime handles
        // distinguish slot reuse without retaining every retired generation until disconnect.
        private readonly NpcHandle[] proximityStreamOwners = new NpcHandle[MaxNpcSlots];
        private readonly byte[] proximityStreamCounters = new byte[MaxNpcSlots];
        private sealed record PlayingOccupation(PlayerHandle Player);
        private PlayingOccupation? playing;
        private float reportedCameraX;
        private float reportedCameraY;

        public TerrariaConnectionOutboundQueue Outbound { get; } =
            outbound ?? throw new ArgumentNullException(nameof(outbound));

        public bool MatchesPlayer(PlayerHandle player) => Volatile.Read(ref playing)?.Player == player;

        public bool IsPlaying => Volatile.Read(ref playing)?.Player.IsAssigned == true;

        public object? CapturePlayingOccupation() => Volatile.Read(ref playing);
        public bool IsCurrentPlayingOccupation(object captured) =>
            ReferenceEquals(Volatile.Read(ref playing), captured) && IsPlaying;

        public void MarkPlaying(PlayerHandle player)
        {
            lock (proximityStreamGate)
            {
                Array.Clear(proximityStreamOwners);
                Array.Clear(proximityStreamCounters);
                reportedCameraX = 0f;
                reportedCameraY = 0f;
            }
            Volatile.Write(ref playing, new PlayingOccupation(player));
        }

        public void ClearPlaying(PlayerHandle player)
        {
            var captured = Volatile.Read(ref playing);
            if (captured?.Player != player ||
                !ReferenceEquals(Interlocked.CompareExchange(ref playing, null, captured), captured)) return;
            lock (proximityStreamGate)
            {
                Array.Clear(proximityStreamOwners);
                Array.Clear(proximityStreamCounters);
            }
        }

        public void UpdateReportedCameraPosition(PlayerHandle player, in PlayerMovementCommitRequest request)
        {
            if (!MatchesPlayer(player)) return;
            lock (proximityStreamGate)
            {
                reportedCameraX = request.HasCameraTarget ? request.CameraTargetX : request.PositionX;
                reportedCameraY = request.HasCameraTarget ? request.CameraTargetY : request.PositionY;
            }
        }

        public bool TryAccumulateProximityStream(
            NpcHandle npc, float npcCenterX, float npcCenterY, bool boss, byte threshold)
        {
            if (!IsPlaying || !npc.IsAssigned || threshold == 0) return false;
            lock (proximityStreamGate)
            {
                if (!TryOwnProximitySlot(npc)) return false;
                float dx = npcCenterX - reportedCameraX;
                float dy = npcCenterY - reportedCameraY;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                byte gain = boss
                    ? threshold
                    : distance < 250f ? threshold
                    : distance < 500f ? (byte)4
                    : distance < 1_000f ? (byte)2
                    : distance < 1_500f ? (byte)1
                    : (byte)0;
                byte counter = unchecked((byte)(proximityStreamCounters[npc.Slot] + gain));
                proximityStreamCounters[npc.Slot] = counter;
                return counter >= threshold;
            }
        }

        // NetMessage.SendData(23) clears this recipient's streamCounter after an actual send.
        public void ResetProximityStream(NpcHandle npc)
        {
            if (!IsPlaying || !npc.IsAssigned) return;
            lock (proximityStreamGate)
            {
                if (TryOwnProximitySlot(npc)) proximityStreamCounters[npc.Slot] = 0;
            }
        }

        private bool TryOwnProximitySlot(NpcHandle npc)
        {
            var owner = proximityStreamOwners[npc.Slot];
            if (owner == npc) return true;
            if (owner.IsAssigned && owner.Generation.Value > npc.Generation.Value) return false;
            proximityStreamOwners[npc.Slot] = npc;
            proximityStreamCounters[npc.Slot] = 0;
            return true;
        }
    }
}
