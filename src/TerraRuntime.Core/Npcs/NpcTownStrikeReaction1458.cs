using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

// Materialized by the application on an owned RNG preview, never evaluated again during damage.
internal readonly record struct NpcTownStrikeReaction1458(NpcHandle Owner, NpcRevision Revision,
    NpcAiState Ai, int Direction, VanillaUnifiedRandom1458 SourceRandom,
    VanillaUnifiedRandom1458 BeforeRandom);
