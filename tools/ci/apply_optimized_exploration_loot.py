from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


workspace_path = Path("src/TerraRuntime.World/RuntimeWorldGenerationWorkspace.cs")
workspace = workspace_path.read_text(encoding="utf-8")
marker = '''    /// <summary>Returns a detached dense chest snapshot suitable for persistence.</summary>
    public WorldChest[] CaptureGeneratedChests()
    {
        var snapshot = new WorldChest[generatedChests.Count];
        for (int i = 0; i < generatedChests.Count; i++)
        {
            WorldChest source = generatedChests[i];
            snapshot[i] = source with { Items = source.Items.ToArray() };
        }
        return snapshot;
    }
'''
replacement = marker + '''
    /// <summary>
    /// Replaces only the detached item side-table for an already registered generated chest. Geometry, dense slot
    /// identity, coordinates and name remain unchanged; the same vanilla item/prefix validation used by TryAddChest
    /// is applied before the replacement becomes visible to later generation passes.
    /// </summary>
    internal bool TryReplaceGeneratedChestItems(
        int x,
        int y,
        ReadOnlySpan<WorldGenerationChestItem> items)
    {
        if (items.Length > WorldGenerationChestRules.VanillaItemSlotCount)
            return false;

        int chestIndex = -1;
        for (int i = 0; i < generatedChests.Count; i++)
        {
            WorldChest chest = generatedChests[i];
            if (chest.X == x && chest.Y == y)
            {
                chestIndex = i;
                break;
            }
        }
        if (chestIndex < 0)
            return false;

        var persistedItems = new WorldChestItem[WorldGenerationChestRules.VanillaItemSlotCount];
        for (int index = 0; index < items.Length; index++)
        {
            WorldGenerationChestItem item = items[index];
            if (item.Stack < 0 || item.Stack > short.MaxValue || item.Prefix.Value > byte.MaxValue)
                return false;

            if (item.Stack == 0)
            {
                if (!item.ItemType.IsNone || item.Prefix.Value != 0)
                    return false;
                continue;
            }

            if (!VanillaItemIds.TryCreate(item.ItemType.Value, out ItemTypeId validated) || validated.IsNone)
                return false;

            persistedItems[index] = new WorldChestItem(
                item.Stack,
                item.ItemType.Value,
                checked((byte)item.Prefix.Value));
        }

        WorldChest current = generatedChests[chestIndex];
        generatedChests[chestIndex] = current with { Items = persistedItems };
        return true;
    }
'''
workspace = replace_once(workspace, marker, replacement, "generated chest replacement insertion")
workspace_path.write_text(workspace, encoding="utf-8")

provider_path = Path("src/TerraRuntime.World/Generation/Optimized/OptimizedSurfaceDecorationWorldGenerationProvider.cs")
provider = provider_path.read_text(encoding="utf-8")
provider = replace_once(
    provider,
    '    private static readonly WorldGenerationPassId SurfaceLifeId = new("terraruntime:optimized/surface-life");\n    private static readonly WorldGenerationPassId ProgressionValidationId = new("terraruntime:optimized/progression-validation");',
    '    private static readonly WorldGenerationPassId SurfaceLifeId = new("terraruntime:optimized/surface-life");\n    private static readonly WorldGenerationPassId ExplorationLootId = new("terraruntime:optimized/exploration-loot-v2");\n    private static readonly WorldGenerationPassId ProgressionValidationId = new("terraruntime:optimized/progression-validation");',
    "exploration loot pass id")
provider = replace_once(
    provider,
    '        bool rewiredValidation = false;\n        bool insertedSurfaceLife = false;',
    '        bool rewiredValidation = false;\n        bool insertedSurfaceLife = false;\n        bool insertedExplorationLoot = false;',
    "exploration loot insertion flag")
provider = replace_once(
    provider,
    '''            builder.Add(CloneDescriptor(entry.Descriptor, [SurfaceLifeId]), entry.Pass);
            insertedSurfaceLife = true;''',
    '''            builder.Add(
                new WorldGenerationPassDescriptor(
                    ExplorationLootId,
                    WorldGenerationRngMode.IsolatedDeterministic,
                    requiredAfter: [SurfaceLifeId]),
                ExplorationLootPass.Instance);
            builder.Add(CloneDescriptor(entry.Descriptor, [ExplorationLootId]), entry.Pass);
            insertedSurfaceLife = true;
            insertedExplorationLoot = true;''',
    "exploration loot pass insertion")
provider = replace_once(
    provider,
    '''        if (!insertedMorphology || !insertedUndergroundMorphology || !rewiredCaves ||
            !insertedDungeonV2 || !rewiredValidation || !insertedSurfaceLife)''',
    '''        if (!insertedMorphology || !insertedUndergroundMorphology || !rewiredCaves ||
            !insertedDungeonV2 || !rewiredValidation || !insertedSurfaceLife || !insertedExplorationLoot)''',
    "exploration loot final gate")
dungeon_pass = '''    private sealed class DungeonV2Pass : IWorldGenerationPass
    {
        public static DungeonV2Pass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context) =>
            _ = OptimizedDungeonV2.Apply(context);
    }
'''
provider = replace_once(
    provider,
    dungeon_pass,
    dungeon_pass + '''
    private sealed class ExplorationLootPass : IWorldGenerationPass
    {
        public static ExplorationLootPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context) =>
            _ = OptimizedExplorationLoot1458.Apply(context);
    }
''',
    "exploration loot pass class")
provider_path.write_text(provider, encoding="utf-8")

print("Applied optimized source-backed exploration loot integration.")
