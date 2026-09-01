from pathlib import Path


def read(path: str) -> str:
    return Path(path).read_text(encoding="utf-8-sig")


def write(path: str, text: str) -> None:
    Path(path).write_text(text, encoding="utf-8")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one anchor, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


ids = "src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs"
replace_once(
    ids,
    "    public static readonly NpcTypeId BloodEelTail = new(623);\n}",
    "    public static readonly NpcTypeId BloodEelTail = new(623);\n"
    "    public static readonly NpcTypeId Vulture = new(61);\n"
    "    public static readonly NpcTypeId SpikeBall = new(70);\n"
    "    public static readonly NpcTypeId BlazingWheel = new(72);\n"
    "    public static readonly NpcTypeId Raven = new(301);\n}"
)
replace_once(
    ids,
    "    public static readonly NpcAiStyleId KingSlime = new(15);\n"
    "    public static readonly NpcAiStyleId BrainOfCthulhu = new(54);",
    "    public static readonly NpcAiStyleId KingSlime = new(15);\n"
    "    public static readonly NpcAiStyleId Vulture = new(17);\n"
    "    public static readonly NpcAiStyleId SpikeBall = new(20);\n"
    "    public static readonly NpcAiStyleId BlazingWheel = new(21);\n"
    "    public static readonly NpcAiStyleId BrainOfCthulhu = new(54);"
)

catalog = "src/TerraRuntime.Core/Npcs/VanillaNpcDefinitionCatalog.cs"
replace_once(
    catalog,
    "    Worm = 7,\n    BrainOfCthulhu = 8,\n    BrainCreeper = 9\n}",
    "    Worm = 7,\n    BrainOfCthulhu = 8,\n    BrainCreeper = 9,\n"
    "    Vulture = 10,\n    SpikeBall = 11,\n    BlazingWheel = 12\n}"
)
replace_once(
    catalog,
    "    GroundFighter = 3,\n    NoClipFlight = 4\n}",
    "    GroundFighter = 3,\n    NoClipFlight = 4,\n"
    "    Vulture = 5,\n    SpikeBall = 6,\n    BlazingWheel = 7\n}"
)
replace_once(
    catalog,
    "    public bool IsBoss => Role == NpcArchetypeRole.Boss;\n",
    "    /// <summary>SetDefaults-owned invulnerability that must exist before the first AI tick.</summary>\n"
    "    public bool DontTakeDamageAtSpawn { get; init; }\n\n"
    "    public bool IsBoss => Role == NpcArchetypeRole.Boss;\n"
)
replace_once(
    catalog,
    "        if (VanillaWormNpcCatalog.TryGetDefinition(type, out definition))\n"
    "            return true;\n\n"
    "        if (type == VanillaNpcIds.BrainOfCthulhu)",
    "        if (VanillaWormNpcCatalog.TryGetDefinition(type, out definition))\n"
    "            return true;\n\n"
    "        if (VanillaNpcAi17_20_21Catalog1458.TryGetDefinition(type, out definition))\n"
    "            return true;\n\n"
    "        if (type == VanillaNpcIds.BrainOfCthulhu)"
)
text = read(catalog)
start = text.index("        if (type == VanillaNpcIds.BrainOfCthulhu)")
end = text.index("        if (type == VanillaNpcIds.BrainCreeper)", start)
block = text[start:end]
old_tail = "                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);\n            return true;"
if block.count(old_tail) != 1:
    raise SystemExit("BrainOfCthulhu definition tail drifted")
block = block.replace(
    old_tail,
    "                SyncAnchor: VanillaNpcSyncAnchor.TopLeft)\n"
    "            {\n"
    "                DontTakeDamageAtSpawn = true\n"
    "            };\n"
    "            return true;",
    1,
)
write(catalog, text[:start] + block + text[end:])

ownership = "src/TerraRuntime.Core/Npcs/RuntimeNpcStateOwnershipPolicy.cs"
replace_once(
    ownership,
    "            if (definition.Type == VanillaNpcIds.BrainOfCthulhu)\n"
    "                simulation = simulation with { DontTakeDamage = true };",
    "            if (definition.DontTakeDamageAtSpawn)\n"
    "                simulation = simulation with { DontTakeDamage = true };"
)

coverage = "src/TerraRuntime.Core/Npcs/VanillaNpcAiCoverageCatalog.cs"
replace_once(
    coverage,
    "public enum VanillaNpcAiCapability : uint",
    "public enum VanillaNpcAiCapability : ulong",
)
replace_once(
    coverage,
    "    BrainBossStateSlice = 1u << 29,\n"
    "    BrainCreeperStateSlice = 1u << 30\n}",
    "    BrainBossStateSlice = 1u << 29,\n"
    "    BrainCreeperStateSlice = 1u << 30,\n"
    "    VultureMotionSlice = 1ul << 31,\n"
    "    SpikeBallMotionSlice = 1ul << 32,\n"
    "    BlazingWheelMotionSlice = 1ul << 33\n}"
)
replace_once(
    coverage,
    "            VanillaFlyerNpcCatalog.DefinitionCount +\n"
    "            VanillaWormNpcCatalog.Count];",
    "            VanillaFlyerNpcCatalog.DefinitionCount +\n"
    "            VanillaWormNpcCatalog.Count +\n"
    "            VanillaNpcAi17_20_21Catalog1458.DefinitionCount];"
)
replace_once(
    coverage,
    "        return entries;\n    }\n\n    private static bool HasNegativeNetVariant",
    "        foreach (VanillaNpcDefinition definition in VanillaNpcAi17_20_21Catalog1458.AllDefinitions)\n"
    "        {\n"
    "            VanillaNpcAiCapability slice = definition.BehaviorFamily switch\n"
    "            {\n"
    "                VanillaNpcBehaviorFamily.Vulture => VanillaNpcAiCapability.VultureMotionSlice,\n"
    "                VanillaNpcBehaviorFamily.SpikeBall => VanillaNpcAiCapability.SpikeBallMotionSlice,\n"
    "                VanillaNpcBehaviorFamily.BlazingWheel => VanillaNpcAiCapability.BlazingWheelMotionSlice,\n"
    "                _ => throw new InvalidOperationException(\"Unexpected AI_017/020/021 behavior family.\")\n"
    "            };\n"
    "            entries[index++] = Partial(definition.Type, OrdinaryCore | slice);\n"
    "        }\n\n"
    "        if (index != entries.Length)\n"
    "            throw new InvalidOperationException(\"Vanilla NPC coverage catalog count drifted.\");\n\n"
    "        return entries;\n"
    "    }\n\n"
    "    private static bool HasNegativeNetVariant"
)

stepper = "src/TerraRuntime.Core/Npcs/VanillaNpcTargetingAiStepper.cs"
replace_once(
    stepper,
    "    private readonly VanillaBrainCreeperNpcBehaviorStrategy _brainCreeper;\n"
    "    private readonly IVanillaNpcRandom _random;",
    "    private readonly VanillaBrainCreeperNpcBehaviorStrategy _brainCreeper;\n"
    "    private readonly IVanillaNpcBehaviorStrategy _vulture = new VanillaVultureNpcBehaviorStrategy();\n"
    "    private readonly VanillaSpikeBallNpcBehaviorStrategy _spikeBall;\n"
    "    private readonly IVanillaNpcBehaviorStrategy _blazingWheel = new VanillaBlazingWheelNpcBehaviorStrategy();\n"
    "    private readonly IVanillaNpcRandom _random;"
)
replace_once(
    stepper,
    "        _brainOfCthulhu = new VanillaBrainOfCthulhuNpcBehaviorStrategy(_random);\n"
    "        _brainCreeper = new VanillaBrainCreeperNpcBehaviorStrategy(_random);",
    "        _brainOfCthulhu = new VanillaBrainOfCthulhuNpcBehaviorStrategy(_random);\n"
    "        _brainCreeper = new VanillaBrainCreeperNpcBehaviorStrategy(_random);\n"
    "        _spikeBall = new VanillaSpikeBallNpcBehaviorStrategy(_random);"
)
replace_once(
    stepper,
    "            VanillaNpcBehaviorFamily.BrainOfCthulhu => _brainOfCthulhu,\n"
    "            VanillaNpcBehaviorFamily.BrainCreeper => _brainCreeper,\n"
    "            _ => null",
    "            VanillaNpcBehaviorFamily.BrainOfCthulhu => _brainOfCthulhu,\n"
    "            VanillaNpcBehaviorFamily.BrainCreeper => _brainCreeper,\n"
    "            VanillaNpcBehaviorFamily.Vulture => _vulture,\n"
    "            VanillaNpcBehaviorFamily.SpikeBall => _spikeBall,\n"
    "            VanillaNpcBehaviorFamily.BlazingWheel => _blazingWheel,\n"
    "            _ => null"
)

print("Muse NPC AI conflicts resolved against Brain vertical")
