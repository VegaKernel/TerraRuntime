using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Server-owned identity plus client-supplied player presentation state accepted for synchronization.
/// The claimed wire id is intentionally absent.
/// </summary>
public readonly record struct PlayerAppearanceCommitRequest(
    PlayerSlotId PlayerSlot,
    byte SkinVariant,
    byte VoiceVariant,
    float VoicePitchOffset,
    byte Hair,
    string Name,
    byte HairDye,
    ushort HideVisibleAccessory,
    byte HideMisc,
    PlayerRgbColor HairColor,
    PlayerRgbColor SkinColor,
    PlayerRgbColor EyeColor,
    PlayerRgbColor ShirtColor,
    PlayerRgbColor UnderShirtColor,
    PlayerRgbColor PantsColor,
    PlayerRgbColor ShoeColor,
    byte DifficultyFlags,
    byte TorchAndCartFlags,
    byte ConsumableUnlockFlags);

/// <summary>
/// Server-owned player identity plus one client-supplied equipment/inventory slot update.
/// The claimed wire id is intentionally absent. Raw item fields are retained for packet compatibility;
/// authoritative consumers should use the typed accessors after packet-5 canonicalization.
/// </summary>
public readonly record struct PlayerEquipmentCommitRequest(
    PlayerSlotId PlayerSlot,
    short SlotId,
    short Stack,
    byte Prefix,
    short ItemNetId,
    byte ItemFlags)
{
    public PrefixId PrefixId => new(Prefix);

    /// <summary>
    /// Crosses a normalized request into canonical Terraria item identity. Empty slots map to ItemTypeId.None;
    /// signed legacy packet net ids are intentionally rejected here because ingress normalization owns that compatibility.
    /// </summary>
    public bool TryGetCanonicalItemType(out ItemTypeId itemType)
    {
        if (Stack <= 0)
        {
            itemType = VanillaItemIds.None;
            return ItemNetId == 0;
        }

        return VanillaItemIds.TryCreate(ItemNetId, out itemType) && !itemType.IsNone;
    }
}

/// <summary>
/// Protocol-neutral player movement accepted from one authenticated connection.
/// Player identity is authoritative: no client-claimed player id is carried into this command.
/// </summary>
public readonly record struct PlayerMovementCommitRequest(
    PlayerSlotId PlayerSlot,
    byte ControlFlags,
    byte MovementFlags,
    byte MiscFlags1,
    byte MiscFlags2,
    byte SelectedItem,
    float PositionX,
    float PositionY,
    bool HasVelocity,
    float VelocityX,
    float VelocityY,
    bool HasMount,
    ushort MountType,
    bool HasPotionOfReturnPositions,
    float PotionOfReturnOriginalPositionX,
    float PotionOfReturnOriginalPositionY,
    float PotionOfReturnHomePositionX,
    float PotionOfReturnHomePositionY,
    bool HasCameraTarget,
    float CameraTargetX,
    float CameraTargetY);

/// <summary>
/// Owned, protocol-neutral player spawn data submitted to the authoritative game loop after packet decoding.
/// </summary>
public readonly record struct PlayerSpawnCommitRequest(
    PlayerSlotId ClaimedSlot,
    short SpawnX,
    short SpawnY,
    int RespawnTimer,
    short DeathsPve,
    short DeathsPvp,
    byte Team,
    byte SpawnContext);

/// <summary>
/// Server-owned player identity plus client-supplied life values accepted for authoritative state.
/// The claimed wire player id is intentionally absent.
/// </summary>
public readonly record struct PlayerHealthCommitRequest(
    PlayerSlotId PlayerSlot,
    short Life,
    short MaxLife);

/// <summary>
/// Server-owned player identity plus client-supplied mana values accepted for authoritative state.
/// The claimed wire player id is intentionally absent.
/// </summary>
public readonly record struct PlayerManaCommitRequest(
    PlayerSlotId PlayerSlot,
    short Mana,
    short MaxMana);
