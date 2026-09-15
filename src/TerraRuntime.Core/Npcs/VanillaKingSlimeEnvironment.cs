using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>Resolved vanilla King Slime teleport destination using NPC.Bottom coordinates.</summary>
public readonly record struct VanillaKingSlimeTeleportDestination(float BottomX, float BottomY)
{
    public bool IsFinite => float.IsFinite(BottomX) && float.IsFinite(BottomY);
}

/// <summary>
/// World-facing facts required by the allocation-free King Slime AI primitive. The core AI never traverses
/// mutable world storage directly; the runtime host owns LOS and teleport-spot discovery and supplies only the
/// resolved facts needed for the current authoritative tick.
/// </summary>
public interface IVanillaKingSlimeEnvironment
{
    float WorldPixelWidth { get; }

    float WorldPixelHeight { get; }

    bool CanHitLine(float fromX, float fromY, float toX, float toY);

    bool TryResolveTeleport(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in VanillaNpcTargetCandidate target,
        bool antiCheese,
        out VanillaKingSlimeTeleportDestination destination);
}

/// <summary>
/// World-owned NPC random stream using the pinned Terraria 1.4.5.8 UnifiedRandom algorithm.
/// The default seed follows UnifiedRandom's Environment.TickCount constructor; hosts may supply a seed explicitly.
/// </summary>
public sealed class SystemVanillaNpcRandom : IVanillaNpcRandom
{
    private readonly VanillaUnifiedRandom1458 _random;

    public SystemVanillaNpcRandom()
        : this(new VanillaUnifiedRandom1458(Environment.TickCount))
    {
    }

    public SystemVanillaNpcRandom(int seed)
        : this(new VanillaUnifiedRandom1458(seed))
    {
    }

    private SystemVanillaNpcRandom(VanillaUnifiedRandom1458 random) => _random = random;

    public double NextDouble() => _random.NextDouble();

    public int NextInt32(int inclusiveMin, int exclusiveMax)
    {
        if (exclusiveMax <= inclusiveMin)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));

        return _random.Next(inclusiveMin, exclusiveMax);
    }
}
