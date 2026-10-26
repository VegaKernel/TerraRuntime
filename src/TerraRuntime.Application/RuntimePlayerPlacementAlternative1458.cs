using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

// One source-valid, no-wire incomplete-replacement alternative inside ToolUse.
// Cloned endpoints prove compatibility with the chosen source afterimage; they
// are never elapsed-time certificates or permission to reset an unknown stream.
internal sealed class RuntimePlayerPlacementAlternative1458
{
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
}
