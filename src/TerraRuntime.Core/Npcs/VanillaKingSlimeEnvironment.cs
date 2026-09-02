using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

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
/// Process-local vanilla NPC random stream. Terraria's Main.rand sequence is not a wire or persistence identity;
/// the compatibility contract pins call ordering and requested ranges while tests can inject a deterministic source.
/// </summary>
public sealed class SystemVanillaNpcRandom : IVanillaNpcRandom
{
    private readonly Random _random;

    public SystemVanillaNpcRandom()
        : this(new Random())
    {
    }

    public SystemVanillaNpcRandom(int seed)
        : this(new Random(seed))
    {
    }

    private SystemVanillaNpcRandom(Random random) => _random = random;

    public int NextInt32(int inclusiveMin, int exclusiveMax)
    {
        if (exclusiveMax <= inclusiveMin)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));

        return _random.Next(inclusiveMin, exclusiveMax);
    }
}
