using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

// One source-valid, no-wire incomplete-replacement alternative inside ToolUse.
// Cloned endpoints prove compatibility with the chosen source afterimage; they
// are never elapsed-time certificates or permission to reset an unknown stream.
internal sealed class RuntimePlayerPlacementAlternative1458
{
    internal const int MaximumTargets = 144;
    private RuntimePlayerPlacementTarget1458[] targets = [];
    internal ReadOnlySpan<RuntimePlayerPlacementTarget1458> Targets => targets;
    internal bool HasSelectionAlternative { get; private set; } = true;
    private readonly VanillaUnifiedRandom1458 canonical;
    private readonly VanillaUnifiedRandom1458 alternative;
    internal ToolClockState1458 Clocks { get; }
    internal ToolHitState1458 ReplacementHits { get; }
    internal ulong HistoryEpoch { get; }
    internal RuntimePlayerItemPhase1458 ExpectedPhaseWithoutTools { get; }
    internal RuntimePlayerToolUse1458? ExpectedCanonicalTools { get; }
    internal RuntimePlayerInventoryStore.ShopJournalCapture Inventory { get; }
    internal WorldTileAuthority.ToolWorldReadPreparation Reads { get; }
    internal PlayerBuffState Buffs { get; }
    internal PlayerEquipmentCommitRequest[] Equipment { get; }
    internal PlayerAppearanceCommitRequest? Appearance { get; }
    internal int CanonicalRemoteCrit { get; }
    internal int AlternativeRemoteCrit { get; }
    internal byte PermanentMask { get; }
    internal RuntimePlayerUpdateWorld1458 World { get; }
    internal bool Ready => Clocks.Selected.ItemTime == 0 && Clocks.Selected.Animation == 0;

    internal RuntimePlayerPlacementAlternative1458(VanillaUnifiedRandom1458 canonical,
        VanillaUnifiedRandom1458 alternative, ToolClockState1458 clocks, ToolHitState1458 replacementHits,
        ulong epoch, RuntimePlayerItemPhase1458 expectedPhase, RuntimePlayerInventoryStore.ShopJournalCapture inventory,
        WorldTileAuthority.ToolWorldReadPreparation reads, PlayerBuffState buffs,
        PlayerEquipmentCommitRequest[] equipment, PlayerAppearanceCommitRequest? appearance,
        int canonicalRemoteCrit, int alternativeRemoteCrit, byte permanentMask, RuntimePlayerUpdateWorld1458 world)
    {
        this.canonical = canonical.Clone(); this.alternative = alternative.Clone();
        Clocks = clocks; ReplacementHits = replacementHits; HistoryEpoch = epoch;
        ExpectedPhaseWithoutTools = expectedPhase with { ToolUse = null };
        ExpectedCanonicalTools = expectedPhase.ToolUse is { } tools
            ? tools with { Input = null, PlacementAlternative = null } : null;
        Inventory = inventory; Reads = reads; Buffs = buffs; Equipment = equipment.ToArray();
        Appearance = appearance; CanonicalRemoteCrit = canonicalRemoteCrit;
        AlternativeRemoteCrit = alternativeRemoteCrit; PermanentMask = permanentMask; World = world;
    }
    internal bool MatchesCanonical(VanillaUnifiedRandom1458 current) => current.HasSameState(canonical);
    internal VanillaUnifiedRandom1458 CreateAlternativePreview() => alternative.Clone();

    internal RuntimePlayerPlacementAlternative1458 WithTargets(
        ReadOnlySpan<RuntimePlayerPlacementTarget1458> sourceTargets, bool hasSelectionAlternative)
    {
        if (sourceTargets.Length > MaximumTargets) throw new ArgumentOutOfRangeException(nameof(sourceTargets));
        var copy = new RuntimePlayerPlacementAlternative1458(canonical, alternative, Clocks, ReplacementHits,
            HistoryEpoch, ExpectedPhaseWithoutTools with { ToolUse = ExpectedCanonicalTools }, Inventory,
            Reads, Buffs, Equipment, Appearance, CanonicalRemoteCrit, AlternativeRemoteCrit, PermanentMask, World);
        copy.targets = sourceTargets.ToArray();
        copy.HasSelectionAlternative = hasSelectionAlternative;
        return copy;
    }
}

// Bounded alternative source afterimages, carried only by the existing ToolUse
// placement history. Each constant target denotes one compatible cursor history;
// no live inventory, RNG stream or world state belongs to this value.
internal sealed class RuntimePlayerPlacementTarget1458
{
    private readonly VanillaUnifiedRandom1458 random;
    internal int X { get; }
    internal int Y { get; }
    internal ToolClockState1458 Clocks { get; }
    internal ToolHitState1458 ReplacementHits { get; }
    internal int RemoteCrit { get; }
    internal RuntimePlayerPlacementTarget1458(int x, int y, ToolClockState1458 clocks,
        ToolHitState1458 hits, VanillaUnifiedRandom1458 random, int remoteCrit)
    {
        X = x; Y = y; Clocks = clocks; ReplacementHits = hits;
        this.random = random.Clone(); RemoteCrit = remoteCrit;
    }
    internal VanillaUnifiedRandom1458 CreatePreview() => random.Clone();
}
