using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    internal bool TryPrepareOrdinaryArrowPublication(
        IReadOnlyList<RuntimeProjectileNpcCombatPass.OrdinaryArrowHit> hits, PlayerHandle player,
        out OrdinaryArrowPublicationPreparation? preparation)
    {
        preparation = null;
        if (!player.IsAssigned || hits.Count > npcs.Capacity || !IsOrdinaryArrowPipelineIdle()) return false;
        var entries = new RuntimeProjectileNpcCombatPass.OrdinaryArrowHit[hits.Count];
        var targets = new NpcHandle[hits.Count];
        var wires = new TerrariaNpcDamageState[hits.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = hits[i];
            if (entry.Projectile.Type.Value is not (4 or 5) || !entry.Request.IsValid ||
                entry.Request.Source.Kind != DamageSourceKind.PlayerProjectile ||
                entry.Request.Source.Player != player || entry.Request.Source.Projectile != entry.Projectile.Handle ||
                entry.Before.TypeIdentity.Value != 3 || entry.Before.NetIdentity.Value != 3 ||
                entry.Accepted.TypeIdentity.Value != 3 || entry.Accepted.NetIdentity.Value != 3 ||
                entry.Before.Handle != entry.Accepted.Handle || entry.Request.Target != entry.Accepted.Handle ||
                entry.Result.Target != entry.Accepted.Handle || entry.Result.Revision != entry.Accepted.Revision ||
                entry.Result.Source != entry.Request.Source || entry.Result.SourceDamage != entry.Request.BaseDamage ||
                entry.Result.Critical != entry.Request.Critical || entry.Result.Lethal ||
                entry.Result.LifeBefore != entry.Before.Simulation.Life ||
                entry.Result.LifeAfter != entry.Accepted.Simulation.Life || entry.Result.LifeAfter <= 0 ||
                !npcs.TryGet(entry.Before.Handle, out var retained) || retained != entry.Before) return false;
            entries[i] = entry;
            targets[i] = entry.Accepted.Handle;
            wires[i] = new(entry.Accepted.Handle.Slot,
                RuntimeNpcPacketProjection.ToProtocolGeneration(entry.Accepted.Handle.Generation),
                (short)Math.Min(entry.Request.BaseDamage, short.MaxValue), entry.Request.KnockBack,
                checked((byte)(entry.Request.HitDirection + 1)), entry.Request.Critical ? (byte)1 : (byte)0);
        }
        if (!interactions.TryPrepareMarks(targets, player, out var marks) || marks is null) return false;
        preparation = new(this, entries, wires, marks);
        return true;
    }

    private bool IsOrdinaryArrowPipelineIdle() => pendingProjectileStrike is null &&
        pendingDeathPlan is null && pendingDebuffPrelude is null;

    internal sealed class OrdinaryArrowPublicationPreparation
    {
        private readonly RuntimeNpcNetworkCombatPipeline owner;
        private readonly RuntimeProjectileNpcCombatPass.OrdinaryArrowHit[] entries;
        private readonly TerrariaNpcDamageState[] wires;
        private readonly RuntimeNpcPlayerInteractionLedger.MarksPreparation marks;
        private bool adopted;
        private bool publishing;
        private int published;

        internal OrdinaryArrowPublicationPreparation(RuntimeNpcNetworkCombatPipeline owner,
            RuntimeProjectileNpcCombatPass.OrdinaryArrowHit[] entries, TerrariaNpcDamageState[] wires,
            RuntimeNpcPlayerInteractionLedger.MarksPreparation marks)
        { this.owner = owner; this.entries = entries; this.wires = wires; this.marks = marks; }

        internal bool CanAdopt
        {
            get
            {
                if (adopted || !owner.IsOrdinaryArrowPipelineIdle() || !marks.IsCurrent) return false;
                foreach (var entry in entries)
                    if (!owner.npcs.TryGet(entry.Before.Handle, out var current) || current != entry.Before) return false;
                return true;
            }
        }

        // Called after the admitted actor/NPC/RNG/immunity adoption, before its first publication.
        internal bool TryAdoptInteractions()
        {
            if (adopted || !owner.IsOrdinaryArrowPipelineIdle() || !marks.IsCurrent) return false;
            foreach (var entry in entries)
                if (!owner.npcs.TryGet(entry.Accepted.Handle, out var current) || current != entry.Accepted) return false;
            if (!marks.TryAdoptUnpublished()) return false;
            adopted = true;
            return true;
        }

        internal bool TryPublishHit(in RuntimeProjectileNpcCombatPass.OrdinaryArrowHit supplied)
        {
            if (!adopted || publishing || published >= entries.Length) return false;
            publishing = true;
            try
            {
                // The actor journal skips replaced targets. Consume those stale entries here as well,
                // while refusing to reorder an earlier entry whose final target is still retained.
                while (published < entries.Length && supplied != entries[published])
                {
                    if (IsTargetCurrent(entries[published])) return false;
                    published++;
                }
                if (published >= entries.Length) return false;
                int index = published++;
                var entry = entries[index];
                if (!IsTargetCurrent(entry)) return false;
                // Source packet28 carries chosen strike input, never resolved HP damage. No random or HitEffect here.
                owner.npcReplication?.TryPublishDamage(default, in wires[index]);
                if (!IsTargetCurrent(entry)) return false;
                // Preserve the runtime's existing NPC-update observer policy, separate from source28/27 ordering.
                return owner.npcs.TryPublishUpdate(entry.Accepted);
            }
            finally { publishing = false; }
        }

        private bool IsTargetCurrent(in RuntimeProjectileNpcCombatPass.OrdinaryArrowHit entry) =>
            owner.npcs.TryGet(entry.Accepted.Handle, out var current) && current == entry.Accepted;
    }
}
