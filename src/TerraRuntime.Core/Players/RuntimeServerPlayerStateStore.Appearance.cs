using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public sealed partial class RuntimeServerPlayerStateStore
{
    public bool TrySetAppearance(
        PlayerHandle player,
        in ServerPlayerAppearanceState appearance,
        out ServerPlayerAppearanceState normalized)
    {
        normalized = default;
        if (!TryGetState(player, out ServerPlayerRuntimeState? state) ||
            state.Revision == ulong.MaxValue)
        {
            return false;
        }

        PlayerAppearanceCommitRequest request = ToCommitRequest(player.Slot, in appearance);
        if (!VanillaPlayerAppearanceNormalizer.TryNormalize(in request, out PlayerAppearanceCommitRequest commit))
            return false;

        normalized = ToServerState(in commit);
        state.Revision++;
        state.Appearance = normalized;
        return true;
    }

    public bool TryGetAppearance(
        PlayerHandle player,
        out ServerPlayerAppearanceState appearance)
    {
        if (!TryGetState(player, out ServerPlayerRuntimeState? state) ||
            state.Appearance is not ServerPlayerAppearanceState current)
        {
            appearance = default;
            return false;
        }

        appearance = current;
        return true;
    }

    private static PlayerAppearanceCommitRequest ToCommitRequest(
        PlayerSlotId playerSlot,
        in ServerPlayerAppearanceState state) =>
        new(
            playerSlot,
            state.SkinVariant,
            state.VoiceVariant,
            state.VoicePitchOffset,
            state.Hair,
            state.Name,
            state.HairDye,
            state.HideVisibleAccessory,
            state.HideMisc,
            state.HairColor,
            state.SkinColor,
            state.EyeColor,
            state.ShirtColor,
            state.UnderShirtColor,
            state.PantsColor,
            state.ShoeColor,
            state.DifficultyFlags,
            state.TorchAndCartFlags,
            state.ConsumableUnlockFlags);

    private static ServerPlayerAppearanceState ToServerState(in PlayerAppearanceCommitRequest state) =>
        new(
            state.SkinVariant,
            state.VoiceVariant,
            state.VoicePitchOffset,
            state.Hair,
            state.Name,
            state.HairDye,
            state.HideVisibleAccessory,
            state.HideMisc,
            state.HairColor,
            state.SkinColor,
            state.EyeColor,
            state.ShirtColor,
            state.UnderShirtColor,
            state.PantsColor,
            state.ShoeColor,
            state.DifficultyFlags,
            state.TorchAndCartFlags,
            state.ConsumableUnlockFlags);
}
