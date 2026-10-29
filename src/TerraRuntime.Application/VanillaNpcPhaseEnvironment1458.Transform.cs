using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class VanillaNpcPhaseEnvironment1458
{
    private sealed partial class Capture
    {
        private NpcSnapshot? transformCheckpoint;
        private NpcSnapshot committed;
        private byte[]? transformBuffFrame;
        private readonly List<(byte[] Bytes, bool Home)> beforeTransformFrames = [];
        private readonly List<(byte[] Bytes, bool Home)> afterTransformFrames = [];
        private RuntimeNpcReplicationRegistry.RetainedHistoricalPublication? historicalPublication;

        public bool TryStageTransform(RuntimeNpcStore.AiSpawnPreview preview, in NpcSnapshot working,
            NpcTypeId target, in NpcAiState suppliedTransformAi, out NpcSnapshot transformed)
        {
            transformed = default;
            if (!IsCurrent || transformCheckpoint.HasValue ||
                !preview.TryStageTransform(in working, target, in suppliedTransformAi, context, out var checkpoint) ||
                !RuntimeNpcBuffStatus1458.TryPlanMaterializedTransform(in phase, in checkpoint, out var nextPhase) ||
                !RuntimeNpcBuffStatus1458.TryPrepareGemBunnyBuffFrame(source.Handle.Slot,
                    nextPhase.Slots, out transformBuffFrame)) return false;
            phase = nextPhase;
            transformCheckpoint = checkpoint;
            // Every transform target in this retained phase is an active ordinary
            // non-netAlways NPC. SendData23 consumes netUpdate when its birth is pending.
            networkSyncRequested = !npcs.HasPendingBirth(source.Handle);
            RecordSourceFrame(transformBuffFrame, home: false);
            transformed = checkpoint;
            return IsCurrent;
        }

        private bool TryContinueCollision(RuntimeNpcStore.AiSpawnPreview preview, in NpcStateUpdate collision,
            out NpcStateUpdate continued)
        {
            continued = collision;
            if (!IsCurrent) return false;
            networkSyncRequested |= collision.RequiresImmediateSync;
            continued = collision with { RequiresImmediateSync = networkSyncRequested };
            // Collision_WaterCollision cures against the CURRENT flags before
            // FishTransformationDuringRain. A preceding SetDefaults already
            // cleared those flags without clearing their physical buff slots.
            var removed = RuntimeNpcBuffStatus1458.PlanGemBunnyWetRemoval(phase.Slots,
                phase.Stinky, collision.Simulation.Wet);
            bool changed = false;
            for (int i = 0; i < RuntimeNpcBuffStatus1458.Capacity; i++) changed |= removed[i] != phase.Slots[i];
            if (changed)
            {
                if (!RuntimeNpcBuffStatus1458.TryPrepareGemBunnyBuffFrame(source.Handle.Slot,
                        in removed, out wetFrame)) return false;
                phase = phase with { Slots = removed };
                RecordSourceFrame(wetFrame, home: false);
            }
            int target = collision.Type switch
            {
                230 when collision.Simulation.Wet => VanillaNpcIds.Goldfish.Value,
                593 when collision.Simulation.Wet => VanillaNpcIds.GoldGoldfish.Value,
                55 when !collision.Simulation.Wet && facts.Raining == true => 230,
                592 when !collision.Simulation.Wet && facts.Raining == true => 593,
                _ => 0
            };
            if (target == 0) return IsCurrent;
            var working = source with { Type = collision.Type, NetId = collision.NetId,
                PositionX = collision.PositionX, PositionY = collision.PositionY,
                VelocityX = collision.VelocityX, VelocityY = collision.VelocityY,
                Target = collision.Target, Ai = collision.Ai, Simulation = collision.Simulation };
            if (!TryStageTransform(preview, in working, new(target), default, out var transformed)) return false;
            if (collision.Type is 230 or 593 && transformed.VelocityY < 0f)
                transformed = transformed with { VelocityY = 0f };
            if (collision.Type is 55 or 592)
                transformed = transformed with { Simulation = transformed.Simulation with
                    { HomeTileX = (int)(transformed.PositionX / 16f) + 10 * transformed.Simulation.DirectionX } };
            // The recorded Transform checkpoint intentionally precedes this
            // caller's clamp/home continuation and the later collision/frame.
            continued = new(transformed.Type, transformed.NetId, transformed.PositionX, transformed.PositionY,
                transformed.VelocityX, transformed.VelocityY, transformed.Target, transformed.Ai, transformed.Simulation)
                { VanillaNetworkPhase = true, RequiresImmediateSync = networkSyncRequested };
            if (collision.Type is 55 or 592)
            {
                var checkpoint = transformCheckpoint!.Value;
                if (!RuntimeNpcReplicationRegistry.TryPrepareGemBunnyHome(in checkpoint, in continued, out var homeFrame))
                    return false;
                if (homeFrame is not null) RecordSourceFrame(homeFrame, home: true);
            }
            return IsCurrent;
        }

        public bool TryPrepareAdoption(in NpcStateUpdate final)
        {
            if (!IsCurrent) return false;
            if (replication is not null && source.TypeIdentity == VanillaNpcIds.MysticFrog)
            {
                foreach (var frame in beforeTransformFrames)
                    if (frame.Home && !replication.CanAdoptSourceHomeUpdate(in source, frame.Bytes)) return false;
                foreach (var frame in afterTransformFrames)
                    if (frame.Home && !replication.CanAdoptSourceHomeUpdate(in source, frame.Bytes)) return false;
            }
            if (transformCheckpoint is not { } checkpoint || replication is null) return true;
            var prefixes = new List<byte[]>(2);
            var tails = new List<byte[]>(2);
            if (initialPhase.Expired)
            {
                if (!RuntimeNpcBuffStatus1458.TryPrepareGemBunnyBuffFrame(source.Handle.Slot,
                        initialPhase.Slots, out var expired)) return false;
                prefixes.Add(expired);
            }
            if (transformBuffFrame is null) return false;
            foreach (var frame in beforeTransformFrames) prefixes.Add(frame.Bytes);
            foreach (var frame in afterTransformFrames) tails.Add(frame.Bytes);
            return replication.TryPrepareHistoricalCheckpoint(in checkpoint, in final,
                    npcs.HasPendingBirth(source.Handle), prefixes.ToArray(), tails.ToArray(), out historicalPublication) &&
                historicalPublication!.TrySealRecipients() && IsCurrent;
        }

        private void RecordSourceFrame(byte[] bytes, bool home)
        {
            if (transformCheckpoint.HasValue) afterTransformFrames.Add((bytes, home));
            else beforeTransformFrames.Add((bytes, home));
        }

        private bool CurrentCommittedStatus()
        {
            var statusCheckpoint = finalPhase with { StatusRevision = initialPhase.StatusRevision + 1 };
            return adopted && npcs.TryGet(committed.Handle, out var current) && current == committed &&
                status.IsCurrentNpcPhase1458(in statusCheckpoint);
        }
    }
}
