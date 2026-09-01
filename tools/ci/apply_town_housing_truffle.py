from pathlib import Path


def replace_once(path, old, new):
    p = Path(path)
    text = p.read_text()
    if text.count(old) != 1:
        raise SystemExit(f'{path}: expected exactly one anchor, found {text.count(old)}')
    p.write_text(text.replace(old, new))


def replace_between(path, start, end, replacement):
    p=Path(path); text=p.read_text(); i=text.find(start); j=text.find(end, i)
    if i < 0 or j < 0: raise SystemExit(f'{path}: missing block anchor')
    p.write_text(text[:i] + replacement + text[j:])

replace_once('src/TerraRuntime.Gameplay/Npcs/VanillaTownNpcSpawnEligibility1458.cs',
'''    bool PartyGirlRollSucceeded)\n{\n    public bool IsValid =>''',
'''    bool PartyGirlRollSucceeded)\n{\n    /// <summary>Persisted NPC.unlockedTruffleSpawn bit from the pinned world metadata.</summary>\n    public bool UnlockedTruffleSpawn { get; init; }\n\n    public bool IsValid =>''')

replace_once('src/TerraRuntime/RuntimeTownNpcWorldFactsProjection1458.cs',
'''            BestiaryCompletionPercent: 0f,\n            PartyGirlRollSucceeded: false);''',
'''            BestiaryCompletionPercent: 0f,\n            PartyGirlRollSucceeded: false)\n        {\n            UnlockedTruffleSpawn = metadata.UnlockedTruffleSpawn\n        };''')

replace_once('src/TerraRuntime/VanillaHousingValidator1458.cs',
'''    private readonly WorldTileStore tiles;\n\n    public VanillaHousingValidator1458(WorldTileStore tiles) =>\n        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));''',
'''    private readonly WorldTileStore tiles;\n    private bool truffleUnlocked;\n\n    public VanillaHousingValidator1458(WorldTileStore tiles) =>\n        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));\n\n    internal void SetTruffleUnlocked(bool unlocked) => truffleUnlocked = unlocked;''')

start='''    private bool PassesSpecialNpcCondition(\n'''
end='''    private int CalculateBaseRoomScore'''
replacement='''    private bool PassesSpecialNpcCondition(\n        NpcTypeId npcType,\n        int roomY2,\n        int startX,\n        int endX,\n        int startY,\n        int endY)\n    {\n        if (npcType != VanillaNpcIds.Truffle)\n            return true;\n\n        double worldSurface = tiles.WorldSurfaceTiles ?? Math.Max(1d, tiles.Dimensions.HeightTiles / 3d);\n        bool noFunctionalSurface = worldSurface <= 30d;\n        if (!truffleUnlocked && roomY2 > worldSurface && !noFunctionalSurface)\n            return false;\n\n        const int mushroomTileThreshold = 100;\n        int mushroomTiles = 0;\n        for (int x = startX + 1; x < endX; x++)\n        {\n            for (int y = startY + 2; y < endY + 2; y++)\n            {\n                WorldTile tile = tiles.Get(x, y);\n                if (IsNActive(in tile) && tile.Type is 70 or 71 or 72 or 528)\n                {\n                    mushroomTiles++;\n                    if (mushroomTiles >= mushroomTileThreshold)\n                        return true;\n                }\n            }\n        }\n\n        return false;\n    }\n\n'''
replace_between('src/TerraRuntime/VanillaHousingValidator1458.cs', start, end, replacement)

replace_once('src/TerraRuntime/RuntimeTownHouseCandidateIndex1458.cs',
'''    public int CandidateCount => candidates.Count;\n\n    public void Scan(int tileBudget)''',
'''    public int CandidateCount => candidates.Count;\n\n    public void SetTruffleUnlocked(bool unlocked) => validator.SetTruffleUnlocked(unlocked);\n\n    public void Scan(int tileBudget)''')

replace_once('src/TerraRuntime/RuntimeTownNpcMoveInCoordinator1458.cs',
'''        this.houses = houses;\n        this.worldFacts = worldFacts;''',
'''        this.houses = houses;\n        houses.SetTruffleUnlocked(worldFacts.UnlockedTruffleSpawn || townNpcs.ContainsNpcType(VanillaNpcIds.Truffle));\n        this.worldFacts = worldFacts;''')

replace_once('src/TerraRuntime/RuntimeTownNpcMoveInCoordinator1458.cs',
'''            if (!townNpcs.TryAddResident(type, in placement, npcs, out NpcSnapshot snapshot, out RuntimeTownNpcHomeCommit home))\n                continue;\n\n            replication?.TryPublishTownHome(in home);''',
'''            if (!townNpcs.TryAddResident(type, in placement, npcs, out NpcSnapshot snapshot, out RuntimeTownNpcHomeCommit home))\n                continue;\n\n            if (type == VanillaNpcIds.Truffle)\n                houses.SetTruffleUnlocked(true);\n\n            replication?.TryPublishTownHome(in home);''')

replace_once('docs/en/town-npc-housing-shops.md',
'''Truffle assignment currently fails closed because its complete mushroom-scene/unlock condition is not yet runtime-owned.''',
'''Truffle assignment now follows the pinned 1.4.5.8 special housing gate: first unlocks require a functional surface room, while an already-unlocked Truffle may move below the surface, and every accepted room requires at least 100 active mushroom-biome tiles (`70`, `71`, `72`, `528`) inside the source-tested housing bounds.''')

ru=Path('docs/ru/town-npc-housing-shops.md')
text=ru.read_text()
for old in [
    'Назначение Truffle сейчас fail-closed, потому что полное условие mushroom scene/unlock ещё не принадлежит runtime.',
    'Назначение Truffle сейчас завершается fail-closed, потому что полное условие mushroom-scene/unlock ещё не принадлежит runtime.',
    'Truffle assignment сейчас fail-closed, потому что полное mushroom-scene/unlock условие ещё не принадлежит runtime.'
]:
    if old in text:
        text=text.replace(old, 'Назначение Truffle теперь следует pinned-условию 1.4.5.8: до первого unlock нужна комната на функциональной поверхности, после unlock допустима комната ниже surface, а в source-tested границах жилья всегда требуется минимум 100 активных mushroom tiles типов `70`, `71`, `72`, `528`.')
        break
else:
    text += '\n\nTruffle housing: до первого unlock нужна комната на функциональной поверхности; после unlock допустимо жильё ниже surface; в tested bounds требуется минимум 100 активных mushroom tiles типов `70`, `71`, `72`, `528`.\n'
ru.write_text(text)

Path('tests/TerraRuntime.Tests/VanillaTruffleHousing1458Tests.cs').write_text(r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaTruffleHousing1458Tests
{
    [Fact]
    public void Locked_truffle_requires_functional_surface_room_and_mushroom_threshold()
    {
        WorldTileStore surface = CreateRoom(top: 20, mushroomTiles: 100, worldSurface: 40d);
        var validator = new VanillaHousingValidator1458(surface);
        Assert.True(validator.Validate(25, 25, VanillaNpcIds.Truffle).IsValid);

        WorldTileStore notEnough = CreateRoom(top: 20, mushroomTiles: 99, worldSurface: 40d);
        Assert.Equal(
            VanillaHousingValidationResult.SpecialNpcConditionFailed,
            new VanillaHousingValidator1458(notEnough).Validate(25, 25, VanillaNpcIds.Truffle).Result);

        WorldTileStore underground = CreateRoom(top: 60, mushroomTiles: 100, worldSurface: 40d);
        Assert.Equal(
            VanillaHousingValidationResult.SpecialNpcConditionFailed,
            new VanillaHousingValidator1458(underground).Validate(25, 65, VanillaNpcIds.Truffle).Result);
    }

    [Fact]
    public void Unlocked_truffle_may_use_underground_mushroom_room()
    {
        WorldTileStore tiles = CreateRoom(top: 60, mushroomTiles: 100, worldSurface: 40d);
        var validator = new VanillaHousingValidator1458(tiles);
        validator.SetTruffleUnlocked(true);

        VanillaHousingPlacement placement = validator.Validate(25, 65, VanillaNpcIds.Truffle);
        Assert.True(placement.IsValid, placement.Result.ToString());
    }

    [Fact]
    public void No_functional_surface_uses_source_exception_before_first_unlock()
    {
        WorldTileStore tiles = CreateRoom(top: 60, mushroomTiles: 100, worldSurface: 30d);
        VanillaHousingPlacement placement = new VanillaHousingValidator1458(tiles)
            .Validate(25, 65, VanillaNpcIds.Truffle);
        Assert.True(placement.IsValid, placement.Result.ToString());
    }

    [Fact]
    public void World_fact_projection_carries_persisted_truffle_unlock()
    {
        var metadata = new WorldFileRuntimeMetadata { UnlockedTruffleSpawn = true };
        VanillaTownSpawnWorldFacts1458 facts = RuntimeTownNpcWorldFactsProjection1458.FromMetadata(metadata);
        Assert.True(facts.UnlockedTruffleSpawn);
    }

    private static WorldTileStore CreateRoom(int top, int mushroomTiles, double worldSurface)
    {
        var tiles = new WorldTileStore(new WorldDimensions(120, 120));
        Assert.True(tiles.TryAttachWorldSurface(worldSurface));
        const int left = 20;
        const int right = 31;
        int bottom = top + 9;
        for (int x = left; x <= right; x++)
        for (int y = top; y <= bottom; y++)
        {
            bool boundary = x == left || x == right || y == top || y == bottom;
            tiles.Set(x, y, new WorldTile
            {
                Type = boundary ? (ushort)1 : (ushort)0,
                Wall = 1,
                Flags = boundary ? WorldTileFlags.Active : WorldTileFlags.None
            });
        }
        Place(tiles, 22, top + 6, 15);
        Place(tiles, 24, top + 6, 14);
        Place(tiles, 26, top + 3, 4);
        Place(tiles, 28, top + 5, 10);

        int written = 0;
        for (int x = 40; x < 60 && written < mushroomTiles; x++)
        for (int y = Math.Max(6, top - 10); y < Math.Min(114, top + 30) && written < mushroomTiles; y++)
        {
            Place(tiles, x, y, (ushort)(written % 4 switch { 0 => 70, 1 => 71, 2 => 72, _ => 528 }));
            written++;
        }
        Assert.Equal(mushroomTiles, written);
        return tiles;
    }

    private static void Place(WorldTileStore tiles, int x, int y, ushort type) =>
        tiles.Set(x, y, new WorldTile { Type = type, Wall = 1, Flags = WorldTileFlags.Active });
}
''')

Path('tools/ci/check_truffle_housing_source.py').write_text(r'''#!/usr/bin/env python3
from argparse import ArgumentParser
from pathlib import Path

p = ArgumentParser()
p.add_argument('--worldgen', required=True)
p.add_argument('--scene-metrics', required=True)
p.add_argument('--main', required=True)
a = p.parse_args()
worldgen = Path(a.worldgen).read_text(errors='ignore')
scene = Path(a.scene_metrics).read_text(errors='ignore')
main = Path(a.main).read_text(errors='ignore')
required = [
    'if (type == 160)',
    '!NPC.unlockedTruffleSpawn && (double)roomY2 > Main.worldSurface && !Main.NoFunctionalSurface',
    'tile.type == 70 || tile.type == 71 || tile.type == 72 || tile.type == 528',
    'num >= SceneMetrics.MushroomTileThreshold',
]
missing = [s for s in required if s not in worldgen]
if 'MushroomTileThreshold = 100' not in scene:
    missing.append('SceneMetrics.MushroomTileThreshold = 100')
if 'NoFunctionalSurface => worldSurface <= 30.0' not in main:
    missing.append('Main.NoFunctionalSurface threshold')
if missing:
    raise SystemExit('Truffle housing source drift: ' + '; '.join(missing))
print('Truffle housing source contract OK')
''')

Path('.github/workflows/truffle-housing-source-contract.yml').write_text(r'''name: Truffle Housing Source Contract

on:
  push:
    branches: [main]
    paths:
      - 'src/TerraRuntime/VanillaHousingValidator1458.cs'
      - 'src/TerraRuntime/RuntimeTownHouseCandidateIndex1458.cs'
      - 'src/TerraRuntime/RuntimeTownNpcMoveInCoordinator1458.cs'
      - 'src/TerraRuntime.Gameplay/Npcs/VanillaTownNpcSpawnEligibility1458.cs'
      - 'src/TerraRuntime/RuntimeTownNpcWorldFactsProjection1458.cs'
      - 'tests/TerraRuntime.Tests/VanillaTruffleHousing1458Tests.cs'
      - 'tools/ci/check_truffle_housing_source.py'
      - '.github/workflows/truffle-housing-source-contract.yml'
  workflow_dispatch:

permissions:
  contents: read

jobs:
  source-contract:
    runs-on: ubuntu-latest
    timeout-minutes: 12
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 11.0.100-preview.7.26381.103
      - name: Download pinned TerrariaServer 1.4.5.8
        shell: bash
        run: |
          set -euo pipefail
          mkdir -p .cache/terraria-1458 artifacts/source-contract .tools
          curl -fL https://terraria.org/api/download/pc-dedicated-server/terraria-server-1458.zip -o .cache/terraria-1458/server.zip
          unzip -q -o .cache/terraria-1458/server.zip -d .cache/terraria-1458/extracted
      - name: Install pinned ILSpy command line
        run: dotnet tool install ilspycmd --tool-path .tools --version 11.0.0.9375
      - name: Verify Truffle housing source contract
        shell: bash
        run: |
          set -euo pipefail
          expected_sha256="d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e"
          assembly=""
          while IFS= read -r -d '' candidate; do
            if [[ "$(sha256sum "$candidate" | awk '{print $1}')" == "$expected_sha256" ]]; then
              assembly="$candidate"
              break
            fi
          done < <(find .cache/terraria-1458/extracted -type f -name 'TerrariaServer.exe' -print0)
          test -n "$assembly"
          refs="$(dirname "$assembly")"
          .tools/ilspycmd --disable-updatecheck -r "$refs" -t Terraria.WorldGen "$assembly" > artifacts/source-contract/WorldGen.cs
          .tools/ilspycmd --disable-updatecheck -r "$refs" -t Terraria.SceneMetrics "$assembly" > artifacts/source-contract/SceneMetrics.cs
          .tools/ilspycmd --disable-updatecheck -r "$refs" -t Terraria.Main "$assembly" > artifacts/source-contract/Main.cs
          python3 tools/ci/check_truffle_housing_source.py \
            --worldgen artifacts/source-contract/WorldGen.cs \
            --scene-metrics artifacts/source-contract/SceneMetrics.cs \
            --main artifacts/source-contract/Main.cs
''')
print('applied truffle housing block')
