using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Server-owned player identity plus client-supplied life values accepted for authoritative state.
/// The claimed wire player id is intentionally absent.
/// </summary>
public readonly record struct PlayerHealthCommitRequest(
    PlayerSlotId PlayerSlot,
    short Life,
    short MaxLife);

public interface IPlayerHealthIngress
{
    bool TryPost(ConnectionHandle connection, in PlayerHealthCommitRequest request);
}

/// <summary>
/// Vanilla 1.4.5.8 clamps statLifeMax to at least 20 when packet 16 is accepted.
/// It does not clamp current life to that maximum in the packet handler.
/// </summary>
public static class VanillaPlayerHealthNormalizer
{
    public const short MinimumMaxLife = 20;

    public static PlayerHealthCommitRequest Normalize(in PlayerHealthCommitRequest request) =>
        request with { MaxLife = Math.Max(request.MaxLife, MinimumMaxLife) };
}

/// <summary>
/// Server-owned player identity plus client-supplied mana values accepted for authoritative state.
/// The claimed wire player id is intentionally absent.
/// </summary>
public readonly record struct PlayerManaCommitRequest(
    PlayerSlotId PlayerSlot,
    short Mana,
    short MaxMana);

public interface IPlayerManaIngress
{
    bool TryPost(ConnectionHandle connection, in PlayerManaCommitRequest request);
}
