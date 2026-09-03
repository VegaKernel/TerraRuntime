using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime;

internal enum CombatIntegrityResolveResult : byte
{
    LegacyFallback = 0,
    Accepted = 1,
    Rejected = 2
}

internal enum CombatIntegrityReason : byte
{
    None = 0,
    MissingPlayer = 1,
    MissingSelectedItem = 2,
    UnsupportedWeapon = 3,
    UnsupportedPrefix = 4,
    UnmodeledEquipment = 5,
    TargetOutOfRange = 6,
    AttackCadence = 7,
    DamageOutsideEnvelope = 8,
    DpsCeiling = 9,
    UnmodeledTargetGeometry = 10
}

internal readonly record struct CombatIntegrityDiagnostic(
    long Tick,
    PlayerHandle Player,
    NpcHandle Target,
    CombatIntegrityReason Reason,
    int ClientDamage,
    int AuthoritativeDamage,
    bool AuthoritativeCritical,
    float SuspicionScore);

internal readonly record struct AuthoritativeCombatRoll(
    AttackContext Context,
    NpcDamageRequest Request,
    int MinimumDamage,
    int MaximumDamage,
    int AnimationTicks,
    int UseTimeTicks,
    float ImpossibleCenterDistancePixels,
    int CritChance);

/// <summary>
/// One source of truth for the verified direct-melee slice. Wire damage/crit never enter the calculation.
/// The catalog is intentionally opt-in until equipment and player-buff modifier state is fully modeled.
/// </summary>
internal sealed class AuthoritativeCombatCalculator
{
    private readonly PlayerAuthority players;
    private readonly Random random;

    public AuthoritativeCombatCalculator(PlayerAuthority players, Random? random = null)
    {
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.random = random ?? Random.Shared;
    }

    public CombatIntegrityReason TryCalculate(
        ConnectionHandle connection,
        in NpcSnapshot target,
        int hitDirection,
        out AuthoritativeCombatRoll roll)
    {
        roll = default;
        if (!players.TryCapture(connection.Player, out PlayerStateSnapshot player))
            return CombatIntegrityReason.MissingPlayer;

        int selectedSlot = player.SelectedItem;
        if (!VanillaPlayerItemSlotCatalog.IsInventorySlot((short)selectedSlot) ||
            !players.TryGetInventoryItem(connection, selectedSlot, out RuntimePlayerInventoryItem item) ||
            item.IsEmpty)
        {
            return CombatIntegrityReason.MissingSelectedItem;
        }

        if (!VanillaItemCombatCatalog.TryGetDirectMelee(item.ItemType, out VanillaDirectMeleeCombatDefinition weapon))
            return CombatIntegrityReason.UnsupportedWeapon;
        if (!VanillaItemCombatCatalog.TryGetPrefixModifiers(item.Prefix, out VanillaCombatPrefixModifiers prefix))
            return CombatIntegrityReason.UnsupportedPrefix;

        if (!players.TryCaptureCombatSnapshot(connection, out VanillaPlayerCombatSnapshot attackerCombat))
            return CombatIntegrityReason.UnmodeledEquipment;

        VanillaResolvedDirectMeleeUse resolved = VanillaDirectMeleeCombatMath.Resolve(
            in weapon,
            in prefix,
            in attackerCombat,
            random.Next(-15, 16),
            random.Next(1, 101),
            pvp: false);

        DamageSource source = DamageSource.FromPlayerItem(connection.Player);
        var context = new AttackContext(connection.Player, source, item.ItemType, item.Prefix, Pvp: false);
        if (!context.IsValid)
            return CombatIntegrityReason.MissingSelectedItem;

        roll = new AuthoritativeCombatRoll(
            context,
            new NpcDamageRequest(
                target.Handle,
                source,
                resolved.Damage,
                ArmorPenetration: resolved.ArmorPenetration,
                Critical: resolved.Critical,
                KnockBack: resolved.KnockBack,
                HitDirection: hitDirection),
            resolved.MinimumDamage,
            resolved.MaximumDamage,
            resolved.AnimationTicks,
            resolved.UseTimeTicks,
            resolved.ImpossibleCenterDistancePixels,
            resolved.CritChance);
        return CombatIntegrityReason.None;
    }
}

/// <summary>
/// Validation stage between authoritative source calculation and world mutation. Rejections happen before
/// PlayerInteraction, HP writes, loot or replication. State is bounded by vanilla player/NPC slot counts.
/// </summary>
internal sealed class CombatValidator
{
    private const int PlayerSlots = byte.MaxValue + 1;
    private const int DpsWindowTicks = 60;
    private const int DpsBucketTicks = 15;
    private const int DpsBucketCount = DpsWindowTicks / DpsBucketTicks;
    private const int DiagnosticCapacity = 256;
    private readonly int npcCapacity;
    private readonly long[] lastHitTick;
    private readonly NpcGeneration[] lastHitTargetGeneration;
    private readonly long[] lastAttackTick = new long[PlayerSlots];
    private readonly PlayerSessionGeneration[] playerGenerations = new PlayerSessionGeneration[PlayerSlots];
    private readonly int[] dpsDamage;
    private readonly long[] dpsEpoch;
    private readonly float[] suspicion = new float[PlayerSlots];
    private readonly long[] suspicionTick = new long[PlayerSlots];
    private readonly int[] critSamples = new int[PlayerSlots];
    private readonly int[] critClaims = new int[PlayerSlots];
    private readonly int[] damageSamples = new int[PlayerSlots];
    private readonly int[] damageEdgeClaims = new int[PlayerSlots];
    private readonly CombatIntegrityDiagnostic[] diagnostics = new CombatIntegrityDiagnostic[DiagnosticCapacity];
    private int diagnosticWrite;

    public CombatValidator(int npcCapacity)
    {
        if (npcCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcCapacity));
        this.npcCapacity = npcCapacity;
        int pairs = checked(PlayerSlots * npcCapacity);
        lastHitTick = new long[pairs];
        lastHitTargetGeneration = new NpcGeneration[pairs];
        Array.Fill(lastHitTick, long.MinValue);
        Array.Fill(lastAttackTick, long.MinValue);
        dpsDamage = new int[checked(pairs * DpsBucketCount)];
        dpsEpoch = new long[dpsDamage.Length];
        Array.Fill(dpsEpoch, long.MinValue);
    }

    public bool TryValidate(
        long tick,
        in PlayerStateSnapshot player,
        in NpcSnapshot target,
        in TerrariaNpcDamageState wire,
        in AuthoritativeCombatRoll roll,
        out CombatIntegrityDiagnostic diagnostic)
    {
        int playerSlot = player.Player.Slot.Value;
        DecaySuspicion(playerSlot, tick);
        PreparePlayerGeneration(player.Player);

        if (!VanillaNpcDefinitionCatalog.TryGet(target.TypeIdentity, target.NetIdentity, out VanillaNpcDefinition npcDef) ||
            !npcDef.TryResolveHitbox(target.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return Reject(tick, player.Player, target.Handle, CombatIntegrityReason.UnmodeledTargetGeometry,
                Math.Max(wire.Damage, (short)0), in roll, 1f, out diagnostic);
        }

        float playerCx = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float playerCy = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float npcCx = target.PositionX + hitbox.Width * 0.5f;
        float npcCy = target.PositionY + hitbox.Height * 0.5f;
        float dx = npcCx - playerCx;
        float dy = npcCy - playerCy;
        float maxRange = roll.ImpossibleCenterDistancePixels + MathF.Max(hitbox.Width, hitbox.Height) * 0.5f;
        if (dx * dx + dy * dy > maxRange * maxRange)
            return Reject(tick, player.Player, target.Handle, CombatIntegrityReason.TargetOutOfRange, wire.Damage, in roll, 3f, out diagnostic);

        long previousAttack = lastAttackTick[playerSlot];
        // One use can legitimately touch more than one target on the same tick. A later tick, however, is a new
        // use and cannot occur before the source-backed useTime gate has elapsed. This closes the old per-target
        // cadence hole without turning a multi-target melee swing into false-positive anti-cheat noise.
        if (previousAttack != long.MinValue && tick != previousAttack && tick - previousAttack < roll.UseTimeTicks)
            return Reject(tick, player.Player, target.Handle, CombatIntegrityReason.AttackCadence, wire.Damage, in roll, 2f, out diagnostic);

        int pair = checked(playerSlot * npcCapacity + target.Handle.Slot);
        if (lastHitTargetGeneration[pair] != target.Handle.Generation)
        {
            lastHitTargetGeneration[pair] = target.Handle.Generation;
            lastHitTick[pair] = long.MinValue;
            ClearDpsPair(pair);
        }
        long previous = lastHitTick[pair];
        if (previous != long.MinValue && tick - previous < roll.AnimationTicks)
            return Reject(tick, player.Player, target.Handle, CombatIntegrityReason.AttackCadence, wire.Damage, in roll, 2f, out diagnostic);

        int clientDamage = Math.Max(wire.Damage, (short)0);
        if (clientDamage > roll.MaximumDamage)
            return Reject(tick, player.Player, target.Handle, CombatIntegrityReason.DamageOutsideEnvelope, clientDamage, in roll, 4f, out diagnostic);

        int dps = SumDps(pair, tick);
        int maximumHits = (DpsWindowTicks + roll.AnimationTicks - 1) / roll.AnimationTicks + 1;
        int dpsCeiling = checked(roll.MaximumDamage * maximumHits);
        if (dps + roll.Request.BaseDamage > dpsCeiling)
            return Reject(tick, player.Player, target.Handle, CombatIntegrityReason.DpsCeiling, clientDamage, in roll, 3f, out diagnostic);

        lastAttackTick[playerSlot] = tick;
        lastHitTick[pair] = tick;
        AddDps(pair, tick, roll.Request.BaseDamage);
        TrackClaimStatistics(playerSlot, in wire, in roll);
        diagnostic = new CombatIntegrityDiagnostic(
            tick, player.Player, target.Handle, CombatIntegrityReason.None, clientDamage,
            roll.Request.BaseDamage, roll.Request.Critical, suspicion[playerSlot]);
        return true;
    }

    public CombatIntegrityDiagnostic RecordRejection(
        long tick,
        PlayerHandle player,
        NpcHandle target,
        CombatIntegrityReason reason,
        int clientDamage,
        float suspicionDelta = 1f)
    {
        int slot = player.Slot.Value;
        DecaySuspicion(slot, tick);
        suspicion[slot] = Math.Min(100f, suspicion[slot] + suspicionDelta);
        CombatIntegrityDiagnostic diagnostic = new(
            tick, player, target, reason, Math.Max(clientDamage, 0),
            AuthoritativeDamage: 0, AuthoritativeCritical: false, suspicion[slot]);
        diagnostics[diagnosticWrite++ & (DiagnosticCapacity - 1)] = diagnostic;
        return diagnostic;
    }

    public int CopyRecentDiagnostics(Span<CombatIntegrityDiagnostic> destination)
    {
        int count = Math.Min(destination.Length, Math.Min(diagnosticWrite, DiagnosticCapacity));
        for (int i = 0; i < count; i++)
        {
            int index = (diagnosticWrite - count + i) & (DiagnosticCapacity - 1);
            destination[i] = diagnostics[index];
        }
        return count;
    }

    private bool Reject(long tick, PlayerHandle player, NpcHandle target, CombatIntegrityReason reason,
        int clientDamage, in AuthoritativeCombatRoll roll, float suspicionDelta, out CombatIntegrityDiagnostic diagnostic)
    {
        int slot = player.Slot.Value;
        suspicion[slot] = Math.Min(100f, suspicion[slot] + suspicionDelta);
        diagnostic = new CombatIntegrityDiagnostic(
            tick, player, target, reason, clientDamage, roll.Request.BaseDamage,
            roll.Request.Critical, suspicion[slot]);
        diagnostics[diagnosticWrite++ & (DiagnosticCapacity - 1)] = diagnostic;
        return false;
    }

    private void DecaySuspicion(int playerSlot, long tick)
    {
        long previous = suspicionTick[playerSlot];
        suspicionTick[playerSlot] = tick;
        if (previous <= 0 || tick <= previous || suspicion[playerSlot] <= 0f)
            return;
        suspicion[playerSlot] = Math.Max(0f, suspicion[playerSlot] - (tick - previous) * 0.02f);
    }


    private void PreparePlayerGeneration(PlayerHandle player)
    {
        int slot = player.Slot.Value;
        if (playerGenerations[slot] == player.Generation)
            return;

        playerGenerations[slot] = player.Generation;
        lastAttackTick[slot] = long.MinValue;
        int rowStart = slot * npcCapacity;
        Array.Fill(lastHitTick, long.MinValue, rowStart, npcCapacity);
        Array.Clear(lastHitTargetGeneration, rowStart, npcCapacity);
        for (int pair = rowStart; pair < rowStart + npcCapacity; pair++)
            ClearDpsPair(pair);
        critSamples[slot] = 0;
        critClaims[slot] = 0;
        damageSamples[slot] = 0;
        damageEdgeClaims[slot] = 0;
    }

    private void ClearDpsPair(int pair)
    {
        int offset = pair * DpsBucketCount;
        Array.Clear(dpsDamage, offset, DpsBucketCount);
        Array.Fill(dpsEpoch, long.MinValue, offset, DpsBucketCount);
    }

    private int SumDps(int pair, long tick)
    {
        int sum = 0;
        long minimumEpoch = (tick - (DpsWindowTicks - 1)) / DpsBucketTicks;
        int offset = pair * DpsBucketCount;
        for (int i = 0; i < DpsBucketCount; i++)
        {
            if (dpsEpoch[offset + i] >= minimumEpoch)
                sum += dpsDamage[offset + i];
        }
        return sum;
    }

    private void AddDps(int pair, long tick, int damage)
    {
        long epoch = tick / DpsBucketTicks;
        int bucket = (int)(epoch % DpsBucketCount);
        int index = pair * DpsBucketCount + bucket;
        if (dpsEpoch[index] != epoch)
        {
            dpsEpoch[index] = epoch;
            dpsDamage[index] = 0;
        }
        dpsDamage[index] = checked(dpsDamage[index] + damage);
    }

    private void TrackClaimStatistics(int playerSlot, in TerrariaNpcDamageState wire, in AuthoritativeCombatRoll roll)
    {
        int critSampleCount = ++critSamples[playerSlot];
        if (wire.Critical)
            critClaims[playerSlot]++;
        if (critSampleCount >= 32)
        {
            float observed = critClaims[playerSlot] / (float)critSampleCount;
            float expected = roll.CritChance / 100f;
            if (observed > expected + 0.35f)
                suspicion[playerSlot] = Math.Min(100f, suspicion[playerSlot] + 1f);
            critSamples[playerSlot] = 0;
            critClaims[playerSlot] = 0;
        }

        int damageSampleCount = ++damageSamples[playerSlot];
        int clientDamage = Math.Max(wire.Damage, (short)0);
        if (clientDamage == roll.MinimumDamage || clientDamage == roll.MaximumDamage)
            damageEdgeClaims[playerSlot]++;
        if (damageSampleCount >= 32)
        {
            // A player repeatedly claiming exact envelope edges is statistically suspicious, but not proof.
            // The score is diagnostic-only because authoritative damage is already server-generated.
            if (damageEdgeClaims[playerSlot] >= 20)
                suspicion[playerSlot] = Math.Min(100f, suspicion[playerSlot] + 1f);
            damageSamples[playerSlot] = 0;
            damageEdgeClaims[playerSlot] = 0;
        }
    }

}

/// <summary>Coordinates calculate -> validate while preserving a compatibility fallback for unverified formulas.</summary>
internal sealed class RuntimeCombatIntegrity
{
    private readonly PlayerAuthority players;
    private readonly AuthoritativeCombatCalculator calculator;
    private readonly CombatValidator validator;

    public RuntimeCombatIntegrity(PlayerAuthority players, int npcCapacity)
    {
        this.players = players;
        calculator = new AuthoritativeCombatCalculator(players);
        validator = new CombatValidator(npcCapacity);
    }

    public CombatIntegrityResolveResult ResolveClientNpcHit(
        long tick,
        ConnectionHandle connection,
        in NpcSnapshot target,
        in TerrariaNpcDamageState wire,
        out NpcDamageRequest request,
        out CombatIntegrityDiagnostic diagnostic)
    {
        request = default;
        diagnostic = default;
        int direction = Math.Clamp(wire.HitDirection, -1, 1);
        CombatIntegrityReason calculation = calculator.TryCalculate(connection, in target, direction, out AuthoritativeCombatRoll roll);
        if (calculation != CombatIntegrityReason.None)
        {
            // Unsupported combat data is compatibility fallback, not guilt. This keeps Phase 7 incremental.
            if (calculation is CombatIntegrityReason.UnsupportedWeapon or
                CombatIntegrityReason.UnsupportedPrefix or CombatIntegrityReason.UnmodeledEquipment)
            {
                return CombatIntegrityResolveResult.LegacyFallback;
            }

            diagnostic = validator.RecordRejection(
                tick, connection.Player, target.Handle, calculation, Math.Max(wire.Damage, (short)0));
            return CombatIntegrityResolveResult.Rejected;
        }

        if (!players.TryCapture(connection.Player, out PlayerStateSnapshot player))
        {
            diagnostic = validator.RecordRejection(
                tick, connection.Player, target.Handle, CombatIntegrityReason.MissingPlayer, Math.Max(wire.Damage, (short)0));
            return CombatIntegrityResolveResult.Rejected;
        }

        if (!validator.TryValidate(tick, in player, in target, in wire, in roll, out diagnostic))
            return CombatIntegrityResolveResult.Rejected;

        request = roll.Request;
        return CombatIntegrityResolveResult.Accepted;
    }

    public int CopyRecentDiagnostics(Span<CombatIntegrityDiagnostic> destination) =>
        validator.CopyRecentDiagnostics(destination);
}
