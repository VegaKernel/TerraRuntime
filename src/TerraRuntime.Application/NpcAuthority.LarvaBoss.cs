using TerraRuntime.Core;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    internal bool TryPrepareLarvaBoss(ToolWorldTileOverlay1458 overlay, int frameX, int frameY,
        ReadOnlySpan<ToolTreeActor1458> projectedActors, VanillaUnifiedRandom1458 random,
        out LarvaBossPreparation? preparation)
    {
        preparation = null;
        if (worldTiles is null || !ReferenceEquals(worldTiles, overlay.Tiles) || !overlay.IsCurrentOwned ||
            worldClock is null || naturalSpawnWorldFacts is not { } facts || facts.DualDungeonsSeed ||
            facts.GameMode is not (0 or 1 or 2)) return false;
        var before = new NpcRawPlayerSlotSnapshot1458[255];
        var selected = new ItemTypeId[255];
        var scopeCandidates = new bool[255];
        var geometryContexts = new VanillaPlayerCombatEquipmentContext[255];
        var source = new NpcRawPlayerSlotSnapshot1458[255];
        int count = 0; byte target = 0; float distance = -1f;
        for (int slot = 0; slot < before.Length; slot++)
        {
            if (!rawPlayerSlots.TryCapture((byte)slot, out before[slot]) ||
                !players.TryCapturePlainLarvaGeometry((byte)slot, out selected[slot], out scopeCandidates[slot], out geometryContexts[slot])) return false;
            var current = before[slot].Facts;
            foreach (var actor in projectedActors)
                if (actor.Slot == slot)
                {
                    if (actor.Active != current.Active || actor.X != current.PositionX || actor.Y != current.PositionY ||
                        actor.Width != current.Width || actor.Height != current.Height || current.Dead && !actor.Dead) return false;
                    current = current with { Dead = actor.Dead }; break;
                }
            source[slot] = before[slot] with { Facts = current };
            if (current.Active && !current.Ghost) count++;
            if (!current.Active || current.Dead) continue;
            float candidate = Math.Abs(current.PositionX - frameX * 16f) + Math.Abs(current.PositionY - frameY * 16f);
            if (distance < 0 || candidate < distance) { target = (byte)slot; distance = candidate; }
        }
        // Check3x3 retains plr0 / distance -1 when nobody is alive. SpawnOnPlayer(222)
        // has no active/dead guard. The concrete raw-slot owner supplies that constructor lifetime.
        var caster = source[target].Facts;
        bool invoke = distance < 4800f;
        if (invoke && (selected[target].Value is LarvaBossItems1458.SniperRifle or LarvaBossItems1458.Binoculars ||
            scopeCandidates[target] && !selected[target].IsNone && !VanillaToolCatalog1458.TryGet(selected[target], out _))) return false;
        var center = caster.Candidate;
        var sections = CaptureInvasionSpawnSections(in center, false);
        var token = new LarvaBossPreparation(this, before, source, selected, scopeCandidates, geometryContexts, sections,
            CaptureSpawnContext(), naturalSpawnProgression.CaptureSnapshot());
        if (!token.IsCurrentOwned) return false;
        if (invoke)
        {
            if (!RuntimeBossSummonSpawn1458.TryFindLarva(worldTiles, overlay, in caster, source,
                random, naturalSpawnSkyblockLowTiles, out bool found, out int x, out int y)) return false;
            token.Found = found; token.BottomX = x; token.BottomY = y;
        }
        token.Target = target; token.PlayerCount = count;
        if (!token.IsCurrentOwned || !overlay.IsCurrentOwned) return false;
        preparation = token;
        return true;
    }

    internal sealed class LarvaBossPreparation
    {
        private readonly NpcAuthority owner;
        private readonly NpcRawPlayerSlotSnapshot1458[] before, source;
        private readonly ItemTypeId[] selected;
        private readonly bool[] scopeCandidates;
        private readonly VanillaPlayerCombatEquipmentContext[] geometryContexts;
        private readonly (WorldSectionId Section, long Version)[] sections;
        private readonly VanillaNpcSpawnContext context;
        private readonly RuntimeWorldProgressionMutationSnapshot progression;
        private bool adopted, published;
        private NpcSnapshot? birth;
        internal LarvaBossPreparation(NpcAuthority owner, NpcRawPlayerSlotSnapshot1458[] before,
            NpcRawPlayerSlotSnapshot1458[] source, ItemTypeId[] selected, bool[] scopeCandidates, VanillaPlayerCombatEquipmentContext[] geometryContexts,
            (WorldSectionId Section, long Version)[] sections, VanillaNpcSpawnContext context,
            RuntimeWorldProgressionMutationSnapshot progression)
        { this.owner=owner;this.before=before;this.source=source;this.selected=selected;this.sections=sections;
            this.context=context;this.progression=progression;this.scopeCandidates=scopeCandidates;this.geometryContexts=geometryContexts; }
        internal bool Found { get; set; }
        internal int BottomX { get; set; }
        internal int BottomY { get; set; }
        internal byte Target { get; set; }
        internal int PlayerCount { get; set; }
        internal bool IsCurrentOwned
        {
            get
            {
                if (adopted || owner.CaptureSpawnContext() != context ||
                    owner.naturalSpawnProgression.CaptureSnapshot() != progression ||
                    !owner.InvasionSpawnSectionsCurrent(sections)) return false;
                for (int slot=0;slot<before.Length;slot++)
                {
                    if (!owner.rawPlayerSlots.TryCapture((byte)slot,out var now) ||
                        now.OwnerSerial != before[slot].OwnerSerial || now.LifetimeRevision != before[slot].LifetimeRevision ||
                        now.MembershipSerial != before[slot].MembershipSerial ||
                        now.LivePlayer?.Player != before[slot].LivePlayer?.Player ||
                        !SameConsumedPlayer(before[slot].Facts, source[slot].Facts, now.Facts) ||
                        !owner.players.TryCapturePlainLarvaGeometry((byte)slot,out var item, out bool scope, out var geometry) || item != selected[slot] || scope != scopeCandidates[slot] || geometry != geometryContexts[slot]) return false;
                }
                return owner.InvasionSpawnSectionsCurrent(sections);
            }
        }
        private static bool SameConsumedPlayer(in VanillaNpcRawPlayer1458 before,
            in VanillaNpcRawPlayer1458 projected, in VanillaNpcRawPlayer1458 now) =>
            before.Slot==now.Slot && before.Active==now.Active && before.Ghost==now.Ghost &&
            (before.Dead==now.Dead || projected.Dead==now.Dead) &&
            before.PositionX==now.PositionX && before.PositionY==now.PositionY &&
            before.Width==now.Width && before.Height==now.Height;
        internal bool HasSameSourceEffects(LarvaBossPreparation other) =>
            ReferenceEquals(owner,other.owner) && Found==other.Found && BottomX==other.BottomX &&
            BottomY==other.BottomY && Target==other.Target && PlayerCount==other.PlayerCount && birth==other.birth &&
            before.AsSpan().SequenceEqual(other.before) && source.AsSpan().SequenceEqual(other.source) &&
            selected.AsSpan().SequenceEqual(other.selected) && scopeCandidates.AsSpan().SequenceEqual(other.scopeCandidates) && geometryContexts.AsSpan().SequenceEqual(other.geometryContexts) && context==other.context && progression==other.progression;
        internal void BindBirth(in NpcSnapshot accepted) => birth=accepted;
        internal void AdoptUnpublished()
        {
            adopted=true;
            if (birth is not null) owner.AppliedSpawns++;
        }
        internal bool Publish()
        {
            if (!adopted || published) return false;
            published=true;
            if (birth is not { } accepted || !owner.npcs.TryGet(accepted.Handle,out var current) || current!=accepted) return true;
            owner.npcs.TryPublishPendingBirth(accepted.Handle);
            if (owner.npcs.TryGet(accepted.Handle,out current) && current==accepted)
                owner.AnnounceBossSpawn(VanillaNpcIds.QueenBee);
            return true;
        }
    }
}
