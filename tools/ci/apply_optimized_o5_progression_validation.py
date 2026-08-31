from pathlib import Path


def patch(path_name: str, replacements: list[tuple[str, str, str]]) -> None:
    path = Path(path_name)
    text = path.read_text(encoding="utf-8")
    for old, new, label in replacements:
        count = text.count(old)
        if count != 1:
            raise SystemExit(f"{path_name}: {label}: expected 1 occurrence, found {count}")
        text = text.replace(old, new)
    path.write_text(text, encoding="utf-8")


patch(
    "src/TerraRuntime.World/Generation/Optimized/OptimizedProgressionValidationWorldGenerationProvider.cs",
    [
        (
            """            OptimizedProgressionValidationReport report =
                OptimizedProgressionWorldValidator.Validate(
                    context.Workspace,
                    metadata,
                    in context.Request,
                    context.CancellationToken);""",
            """            WorldGenerationRequest request = context.Request;
            OptimizedProgressionValidationReport report =
                OptimizedProgressionWorldValidator.Validate(
                    context.Workspace,
                    metadata,
                    in request,
                    context.CancellationToken);""",
            "request property ref binding",
        ),
    ],
)

patch(
    "src/TerraRuntime/BuiltInWorldGeneratorSource.cs",
    [
        (
            "private readonly OptimizedLandmarkWorldGenerationProvider optimized = new();",
            "private readonly OptimizedProgressionValidationWorldGenerationProvider optimized = new();",
            "optimized provider registration",
        ),
        (
            "OptimizedLandmarkWorldGenerationProvider.GeneratorId,",
            "OptimizedProgressionValidationWorldGenerationProvider.GeneratorId,",
            "optimized generator id",
        ),
    ],
)

test_path = Path("tests/TerraRuntime.Tests/OptimizedWorldGenerationProviderTests.cs")
tests = test_path.read_text(encoding="utf-8")
old = "Assert.IsType<OptimizedLandmarkWorldGenerationProvider>(provider);"
new = "Assert.IsType<OptimizedProgressionValidationWorldGenerationProvider>(provider);"
if tests.count(old) != 1:
    raise SystemExit(f"provider type assertion: expected 1 occurrence, found {tests.count(old)}")
tests = tests.replace(old, new)
old = """        RuntimeWorldGenerationWorkspace world = result.Candidate!;

        Assert.Equal(320, result.Metadata.Spawn.X);"""
new = """        RuntimeWorldGenerationWorkspace world = result.Candidate!;
        OptimizedProgressionValidationReport progression = OptimizedProgressionWorldValidator.Validate(
            world,
            world,
            in request,
            TestContext.Current.CancellationToken);
        Assert.Equal(8, progression.ReachableTargetCount);
        Assert.True(progression.CopperTiles > 0);
        Assert.True(progression.IronTiles > 0);
        Assert.True(progression.SilverTiles > 0);
        Assert.True(progression.GoldTiles > 0);
        Assert.True(progression.HellstoneTiles > 0);
        Assert.True(progression.DungeonInteriorCells >= 24);
        Assert.True(progression.HiveInteriorCells >= 18);
        Assert.True(progression.TempleInteriorCells >= 24);

        Assert.Equal(320, result.Metadata.Spawn.X);"""
if tests.count(old) != 1:
    raise SystemExit(f"progression report assertion insertion: expected 1 occurrence, found {tests.count(old)}")
test_path.write_text(tests.replace(old, new), encoding="utf-8")

patch(
    "docs/roadmap/optimized-worldgen.md",
    [
        (
            "split optimized generation into base, playability and landmark providers instead of growing one monolith;",
            "split optimized generation into base, playability, landmark and final progression-validation providers instead of growing one monolith;",
            "provider layering roadmap",
        ),
        (
            "- [ ] validate path/reachability graph from spawn to surface biomes and major structure entrances;",
            "- [x] validate an excavation-aware path/reachability graph from spawn to surface biomes and major structure entrances;",
            "reachability checkbox",
        ),
        (
            "- [ ] validate minimum ore/resource quantities instead of presence only;",
            "- [x] validate area-scaled minimum Copper/Iron/Silver/Gold/Hellstone quantities instead of presence only;",
            "resource checkbox",
        ),
        (
            "- [ ] validate dungeon/temple/hive interior traversal;",
            "- [x] validate connected dungeon/temple/hive interior components and explicit Temple/dungeon access openings;",
            "interior checkbox",
        ),
        (
            "- [ ] validate no required structure was overwritten by a later pass beyond the current material/object checks;",
            "- [x] validate final post-landmark structure footprints, material minima and complete 3x2 progression objects;",
            "final structure checkbox",
        ),
    ],
)

en = Path("docs/en/optimized-world-generation.md")
text = en.read_text(encoding="utf-8")
old = ('    LVal["landmark validator"]\n' '    Commit["candidate finalization / commit"]\n\n' '    Base --> Play --> Land --> Meta --> PVal --> LVal --> Commit')
new = ('    LVal["landmark validator"]\n' '    Prog["OptimizedProgressionValidationWorldGenerationProvider<br/>resource / structure / reachability gate"]\n' '    Commit["candidate finalization / commit"]\n\n' '    Base --> Play --> Land --> Meta --> PVal --> LVal --> Prog --> Commit')
if text.count(old) != 1:
    raise SystemExit("EN diagram insertion point missing")
text = text.replace(old, new)
old = "This is deliberately stronger than checking for one representative tile. A half-generated landmark set is rejected.\n"
new = (
    old + "\n"
    "A final `OptimizedProgressionValidationWorldGenerationProvider` then scans the post-landmark candidate. It enforces\n"
    "area-scaled minimum quantities for Copper, Iron, Silver, Gold and Hellstone; verifies complete 3x2 Demon/Crimson Altar,\n"
    "Hellforge and Lihzahrd Altar footprints; requires non-trivial connected dungeon, hive and Jungle Temple interiors; and\n"
    "builds a bounded excavation-aware reachability graph from spawn to snow, desert, jungle, world evil, the dungeon\n"
    "entrance, hive interior, Jungle Temple entrance and Underworld Hellforge. Ordinary terrain contributes excavation cost,\n"
    "while dense Lihzahrd barriers and deep Lava are treated as blocking. This is a structural topology gate, not a claim of\n"
    "pixel-exact Terraria player movement or tool progression.\n"
)
text = text.replace(old, new)
text = text.replace("The large landmark slice closes a substantial visual/content gap, but `terraruntime:optimized` is not yet\nproduction-complete.", "The landmark and final progression-validation slices close substantial visual/content and structural gaps, but\n`terraruntime:optimized` is not yet production-complete.")
for line in ["- stronger Jungle Temple/hive/dungeon traversal proofs;\n", "- a real reachability graph from spawn to critical entrances;\n", "- minimum quantity gates for ores and progression resources;\n"]:
    if line not in text:
        raise SystemExit(f"EN remaining-work line missing: {line.strip()}")
    text = text.replace(line, "")
en.write_text(text, encoding="utf-8")

ru = Path("docs/ru/optimized-world-generation.md")
text = ru.read_text(encoding="utf-8")
old = ('    LVal["landmark validator"]\n' '    Commit["candidate finalization / commit"]\n\n' '    Base --> Play --> Land --> Meta --> PVal --> LVal --> Commit')
new = ('    LVal["landmark validator"]\n' '    Prog["OptimizedProgressionValidationWorldGenerationProvider<br/>resource / structure / reachability gate"]\n' '    Commit["candidate finalization / commit"]\n\n' '    Base --> Play --> Land --> Meta --> PVal --> LVal --> Prog --> Commit')
if text.count(old) != 1:
    raise SystemExit("RU diagram insertion point missing")
text = text.replace(old, new)
old = "Это намеренно строже проверки одного representative tile. Наполовину созданный набор landmarks отклоняется.\n"
new = (
    old + "\n"
    "После него финальный `OptimizedProgressionValidationWorldGenerationProvider` сканирует уже post-landmark candidate. Он\n"
    "требует масштабируемые по площади минимумы Copper, Iron, Silver, Gold и Hellstone; проверяет полные 3x2 footprints\n"
    "Demon/Crimson Altar, Hellforge и Lihzahrd Altar; требует нетривиальные связные interiors dungeon, hive и Jungle Temple;\n"
    "а также строит ограниченный excavation-aware reachability graph от spawn до snow, desert, jungle, world evil, dungeon\n"
    "entrance, hive interior, Jungle Temple entrance и Underworld Hellforge. Обычная порода учитывается как стоимость\n"
    "прокапывания, а плотные Lihzahrd barriers и глубокая Lava считаются блокирующими. Это structural topology gate, а не\n"
    "заявление о pixel-exact физике движения игрока или точной tool progression Terraria.\n"
)
text = text.replace(old, new)
text = text.replace("Большой landmark slice закрывает заметный визуальный/content gap, но `terraruntime:optimized` ещё не production-complete.", "Landmark и final progression-validation slices закрывают заметные visual/content и structural gaps, но\n`terraruntime:optimized` ещё не production-complete.")
for line in ["- более строгие traversal proofs для Jungle Temple/hive/dungeon;\n", "- реальный reachability graph от spawn до critical entrances;\n", "- minimum quantity gates для ores и progression resources;\n"]:
    if line not in text:
        raise SystemExit(f"RU remaining-work line missing: {line.strip()}")
    text = text.replace(line, "")
ru.write_text(text, encoding="utf-8")
