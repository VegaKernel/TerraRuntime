using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;

namespace TerraRuntime;

internal enum PvpCombatResolveResult : byte
{
    LegacyFallback = 0,
    Accepted = 1,
    Rejected = 2
}

internal readonly record struct AuthoritativePvpHit(
    PlayerHandle Attacker,
    PlayerHandle Target,
    int Damage,
    bool Critical,
    float KnockBack,
    int HitDirection,
    int MinimumDamage,
    int MaximumDamage,
    int AnimationTicks,
    int UseTimeTicks);

/// <summary>
/// Strict direct-melee PvP bridge. It shares VanillaDirectMeleeCombatMath with NPC combat and admits only the state
/// whose target modifiers are actually known. Unsupported armor/buff/equipment state falls back rather than inventing
/// a second PvP formula. Packet-117 damage/crit/item fields are claims, never calculator inputs.
/// </summary>
internal sealed class RuntimePvpCombatIntegrity
{
    private const int PlayerSlots = byte.MaxValue + 1;
    private readonly PlayerAuthority players;
    private readonly Random random;
    private readonly long[] lastAttackTick = new long[PlayerSlots];
    private readonly PlayerSessionGeneration[] lastAttackGeneration = new PlayerSessionGeneration[PlayerSlots];
    private readonly long[] lastPairHitTick = new long[PlayerSlots * PlayerSlots];
    private readonly PlayerSessionGeneration[] lastPairAttackerGeneration = new PlayerSessionGeneration[PlayerSlots * PlayerSlots];
    private readonly PlayerSessionGeneration[] lastPairTargetGeneration = new PlayerSessionGeneration[PlayerSlots * PlayerSlots];

    public RuntimePvpCombatIntegrity(PlayerAuthority players, Random? random = null)
    {
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.random = random ?? Random.Shared;
        Array.Fill(lastAttackTick, long.MinValue);
        Array.Fill(lastPairHitTick, long.MinValue);
    }

    public PvpCombatResolveResult ResolveClientItemHit(
        long tick,
        ConnectionHandle attackerConnection,
        in TerrariaPlayerHurtState wire,
        out AuthoritativePvpHit hit)
    {
        hit = default;
        if (!attackerConnection.IsAssigned || !wire.Pvp || wire.TargetPlayer == attackerConnection.Player.Slot.Value)
            return PvpCombatResolveResult.Rejected;
        if (wire.Reason.HasProjectile || wire.Reason.HasNpc || wire.Reason.HasOther ||
            !wire.Reason.HasPlayer || wire.Reason.SourcePlayer != attackerConnection.Player.Slot.Value)
            return PvpCombatResolveResult.LegacyFallback;
        if (!players.TryCapture(attackerConnection.Player, out PlayerStateSnapshot attacker) ||
            !players.TryGet(wire.TargetPlayer, out RuntimePlayerMember targetMember))
            return PvpCombatResolveResult.Rejected;
        PlayerStateSnapshot target = targetMember.CaptureSnapshot();
        if (!attacker.Hostile || !target.Hostile || attacker.IsDead || target.IsDead || !target.HasHealth || target.Life <= 0)
            return PvpCombatResolveResult.Rejected;
        if (attacker.Team != 0 && attacker.Team == target.Team)
            return PvpCombatResolveResult.Rejected;

        int selectedSlot = attacker.SelectedItem;
        if (!VanillaPlayerItemSlotCatalog.IsInventorySlot((short)selectedSlot) ||
            !players.TryGetInventoryItem(attackerConnection, selectedSlot, out RuntimePlayerInventoryItem item) || item.IsEmpty)
            return PvpCombatResolveResult.Rejected;
        if (!VanillaItemCombatCatalog.TryGetDirectMelee(item.ItemType, out VanillaDirectMeleeCombatDefinition weapon) ||
            !VanillaItemCombatCatalog.TryGetPrefixModifiers(item.Prefix, out VanillaCombatPrefixModifiers prefix))
            return PvpCombatResolveResult.LegacyFallback;

        // Until armor/accessory/buff state participates in the same calculator, only the naked baseline is strict.
        if ((players.TryCaptureEquipment(attackerConnection, out PlayerEquipmentCommitRequest[] attackerEquipment) && attackerEquipment.Length != 0) ||
            (players.TryCaptureEquipment(target.Player, out PlayerEquipmentCommitRequest[] targetEquipment) && targetEquipment.Length != 0))
            return PvpCombatResolveResult.LegacyFallback;

        VanillaResolvedDirectMeleeUse resolved = VanillaDirectMeleeCombatMath.Resolve(
            in weapon,
            in prefix,
            random.Next(-15, 16),
            random.Next(1, 101),
            pvp: true);

        float attackerCx = attacker.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float attackerCy = attacker.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float targetCx = target.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float targetCy = target.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float dx = targetCx - attackerCx;
        float dy = targetCy - attackerCy;
        float maxRange = resolved.ImpossibleCenterDistancePixels +
            MathF.Max(PlayerAuthority.VanillaBasePlayerWidth, PlayerAuthority.VanillaBasePlayerHeight) * 0.5f;
        if (dx * dx + dy * dy > maxRange * maxRange)
            return PvpCombatResolveResult.Rejected;

        int attackerSlot = attacker.Player.Slot.Value;
        int targetSlot = target.Player.Slot.Value;
        long previousAttack = lastAttackGeneration[attackerSlot] == attacker.Player.Generation
            ? lastAttackTick[attackerSlot]
            : long.MinValue;
        if (previousAttack != long.MinValue && tick != previousAttack && tick - previousAttack < resolved.UseTimeTicks)
            return PvpCombatResolveResult.Rejected;
        int pair = attackerSlot * PlayerSlots + targetSlot;
        long previousPair = lastPairAttackerGeneration[pair] == attacker.Player.Generation &&
            lastPairTargetGeneration[pair] == target.Player.Generation
                ? lastPairHitTick[pair]
                : long.MinValue;
        if (previousPair != long.MinValue && tick - previousPair < resolved.AnimationTicks)
            return PvpCombatResolveResult.Rejected;

        if (Math.Max(wire.Damage, (short)0) > resolved.MaximumDamage)
            return PvpCombatResolveResult.Rejected;

        lastAttackGeneration[attackerSlot] = attacker.Player.Generation;
        lastAttackTick[attackerSlot] = tick;
        lastPairAttackerGeneration[pair] = attacker.Player.Generation;
        lastPairTargetGeneration[pair] = target.Player.Generation;
        lastPairHitTick[pair] = tick;
        int direction = Math.Clamp(wire.HitDirection, -1, 1);
        hit = new AuthoritativePvpHit(
            attacker.Player,
            target.Player,
            resolved.Damage,
            resolved.Critical,
            resolved.KnockBack,
            direction,
            resolved.MinimumDamage,
            resolved.MaximumDamage,
            resolved.AnimationTicks,
            resolved.UseTimeTicks);
        return PvpCombatResolveResult.Accepted;
    }
}
