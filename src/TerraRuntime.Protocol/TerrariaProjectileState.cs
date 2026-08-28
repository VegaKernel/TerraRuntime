namespace TerraRuntime.Protocol;

/// <summary>
/// Protocol-neutral components of Terraria's packed projectile identity.
/// Runtime identity stays wider and generation-safe; only this projection is constrained to protocol 326.
/// </summary>
public readonly record struct TerrariaProjectileKeyState(
    byte Spawner,
    ushort ProjectileIndex,
    ushort Generation)
{
    public const ushort MaximumProjectileIndex = 1000;
    public const ushort MaximumGeneration = 16383;

    public bool IsValid =>
        ProjectileIndex <= MaximumProjectileIndex &&
        Generation is > 0 and <= MaximumGeneration;
}

/// <summary>
/// Protocol-library-neutral authoritative projection for Terraria packet 27 / ProjectileNew.
/// Presence flags and packed ProjectileKey serialization belong to the protocol adapter.
/// </summary>
public readonly record struct TerrariaProjectileUpdateState(
    TerrariaProjectileKeyState Key,
    int ProjectileType,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    float Ai0,
    float Ai1,
    float Ai2,
    ushort BannerIdToRespondTo,
    short Damage,
    float KnockBack,
    short OriginalDamage)
{
    public bool IsValid =>
        Key.IsValid &&
        ProjectileType is >= 0 and <= short.MaxValue &&
        float.IsFinite(PositionX) &&
        float.IsFinite(PositionY) &&
        float.IsFinite(VelocityX) &&
        float.IsFinite(VelocityY) &&
        float.IsFinite(Ai0) &&
        float.IsFinite(Ai1) &&
        float.IsFinite(Ai2) &&
        float.IsFinite(KnockBack);
}

/// <summary>
/// Protocol-library-neutral authoritative projection for Terraria packet 29 / ProjectileDestroy.
/// The final authoritative position is retained because the vanilla packet carries it with the key.
/// </summary>
public readonly record struct TerrariaProjectileDestroyState(
    TerrariaProjectileKeyState Key,
    float PositionX,
    float PositionY)
{
    public bool IsValid =>
        Key.IsValid &&
        float.IsFinite(PositionX) &&
        float.IsFinite(PositionY);
}

public enum TerrariaProjectileDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Malformed = 3,
    InvalidState = 4
}
