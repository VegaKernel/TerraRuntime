namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Player-side facts consumed by vanilla NPC target scoring. NoAggro is already resolved for the
/// NPC type being evaluated; tank-pet redirection is a later targeting layer and does not change
/// which player slot TargetClosest selects. Velocity is carried separately from scoring because some
/// boss AI families, notably Eye of Cthulhu rapid dashes, consume the selected player's live motion.
/// </summary>
public readonly record struct VanillaNpcTargetCandidate(
    byte Slot,
    float CenterX,
    float CenterY,
    int Aggro,
    bool Active,
    bool Dead,
    bool Ghost,
    bool NoAggro)
{
    public float VelocityX { get; init; }

    public float VelocityY { get; init; }

    /// <summary>Terraria player.buffImmune[BuffID.Slow] projected for Deerclops attack selection.</summary>
    public bool SlowBuffImmune { get; init; }

    /// <summary>True when BuffID.Slow is already present; used only to suppress redundant Deerclops casts.</summary>
    public bool HasSlowBuff { get; init; }

    /// <summary>Creative god mode suppresses the source-side Deerclops Slow application.</summary>
    public bool CreativeGodMode { get; init; }
}

public readonly record struct VanillaNpcTargetSelection(
    byte PlayerSlot,
    float ManhattanDistance,
    float AdjustedDistance);

/// <summary>
/// Clean-room player-target scoring from TerrariaServer 1.4.5.8 NPC.TargetClosest/TryTrackingTarget.
/// Candidates must be supplied in vanilla player-slot order so equal scores preserve the first slot.
/// Projectile tank-pet redirection is deliberately outside this player-selection primitive.
/// </summary>
public static class VanillaNpcTargeting
{
    public static bool TrySelectClosestPlayerTarget(
        float npcCenterX,
        float npcCenterY,
        int npcDirection,
        ReadOnlySpan<VanillaNpcTargetCandidate> candidates,
        out VanillaNpcTargetSelection selection)
    {
        if (!float.IsFinite(npcCenterX) ||
            !float.IsFinite(npcCenterY) ||
            npcDirection is < -1 or > 1)
        {
            selection = default;
            return false;
        }

        bool found = false;
        float bestAdjustedDistance = 0f;
        selection = default;

        foreach (VanillaNpcTargetCandidate candidate in candidates)
        {
            if (!candidate.Active || candidate.Dead || candidate.Ghost)
                continue;
            if (!float.IsFinite(candidate.CenterX) || !float.IsFinite(candidate.CenterY))
            {
                selection = default;
                return false;
            }

            float distance =
                MathF.Abs(candidate.CenterX - npcCenterX) +
                MathF.Abs(candidate.CenterY - npcCenterY);
            float adjustedDistance = distance - candidate.Aggro;
            if (candidate.NoAggro && npcDirection != 0)
                adjustedDistance += 1000f;

            if (found && adjustedDistance >= bestAdjustedDistance)
                continue;

            found = true;
            bestAdjustedDistance = adjustedDistance;
            selection = new VanillaNpcTargetSelection(
                candidate.Slot,
                distance,
                adjustedDistance);
        }

        return found;
    }
}
