"""Worldgen metadata tool tests: no original source tree or world generation needed."""
from __future__ import annotations

import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
from vanilla_data import capture as facts, worldgen


def source_tree(root: Path):
    # Small invented C# grammar fixtures, not copied game method bodies.
    texts = {
        'Terraria/Main.cs': 'public class Main { public static string versionNumber = "v1.4.5.8"; public const int curRelease = 326; }',
        'Terraria.ID/GenPassNameID.cs': 'public static class Names { public static readonly string Terrain = "Terrain"; public static readonly string Jungle = "Jungle"; public static readonly string Alpha = "Synthetic Alpha"; }',
        'Terraria.GameContent.Biomes/TerrainPass.cs': 'class TerrainPass { public TerrainPass() : base(GenPassNameID.Terrain, 2.25) { } }',
        'Terraria.GameContent.Biomes/JunglePass.cs': 'class JunglePass { public JunglePass() : base(GenPassNameID.Jungle, 3.5) { } }',
        'Terraria.GameContent.Biomes/DitherSnakePass.cs': 'class DitherSnakePass { public DitherSnakePass(string passName) : base(passName, 1.0) { } }',
        'Terraria.GameContent.Generation/PassLegacy.cs': '''class PassLegacy {
private static readonly Dictionary<string, float> _weightMap = new Dictionary<string, float> {
{ "Synthetic Alpha", 0.1f }, { "Jungle", 9f }
};
private static float GetWeight(string name) { if (!_weightMap.TryGetValue(name, out var value)) { return 1f; } return value; }
}''',
        'Terraria/WorldGen.cs': '''class WorldGen {
private static void AddGenerationPass(string name, WorldGenLegacyMethod method) { _generator.Append(new PassLegacy(name, method)); }
private static void AddGenerationPass(GenPass pass) { _generator.Append(pass); }
private static void AddGenerationPass(string name, double weight, WorldGenLegacyMethod method) { _generator.Append(new PassLegacy(name, method, weight)); }
public static void AddPasses()
{
    AddGenerationPass(new TerrainPass());
    if (skyblockWorldGen && !Skyblock.denyAllGeneration)
    {
        AddGenerationPass(new JunglePass());
    }
    AddGenerationPass(GenPassNameID.Alpha, delegate(GenerationProgress p, GameConfiguration c) { p.Message = "if (fake) { AddGenerationPass(bogus); }"; });
}
public static void DisablePassesForSpecialSeeds(List<GenPass> passes)
{
    bool enabled = SecretSeed.dualDungeons.Enabled;
    if (!enabled) { return; }
    if (enabled && (name == GenPassNameID.Alpha || name == GenPassNameID.Jungle)) { pass.Disable(); }
}
}''',
    }
    for name, text in texts.items():
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding='utf-8')


def runtime_tree(root: Path, reference: dict):
    constants = '\n'.join(f'public const string {name} = "{value}";' for name, value in reference['fingerprints'].items())
    names = ',\n'.join('        ' + json.dumps(row['name']) for row in reference['passes']) + ','
    filtered = ',\n'.join('        ' + json.dumps(name) for name in reference['specialSeedFiltering']['disabledNames']) + ','
    text = constants + '\nprivate static readonly string[] PassNames =\n[\n' + names + '\n];\n'
    text += 'private static readonly string[] DualDungeonDisabledPassNames =\n[\n' + filtered + '\n];\n'
    path = root / worldgen.TARGET
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8')
    return path


class WorldgenToolTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)
        self.source = self.folder / 'source'
        self.repo = self.folder / 'runtime'
        source_tree(self.source)
        self.reference = worldgen.capture(self.source)
        self.catalog = runtime_tree(self.repo, self.reference)

    def test_complete_order_and_explicit_unknown_runtime_weight_coverage(self):
        rows = self.reference['passes']
        self.assertEqual(['Terrain', 'Jungle', 'Synthetic Alpha'], [row['name'] for row in rows])
        self.assertEqual([2.25, 3.5, 0.10000000149011612], [row['weight'] for row in rows])
        self.assertEqual(['skyblockWorldGen && !Skyblock.denyAllGeneration'], rows[1]['registrationGuards'])
        result = worldgen.check(self.reference, self.repo, self.source)
        self.assertTrue(result['accepted'], result)
        self.assertEqual('not-represented-by-runtime-catalog', result['coverage']['runtimeWeightComparison'])
        self.assertEqual('not-executed', result['coverage']['worldCellRngComparison'])

    def test_named_mutants_all_fail_and_source_is_never_written(self):
        before = self.catalog.read_bytes()
        result = worldgen.mutate(self.reference, self.repo)
        self.assertTrue(result['accepted'], result)
        self.assertEqual(5, len(result['mutants']))
        self.assertEqual(before, self.catalog.read_bytes())

    def test_always_green_checker_cannot_pass_mutation_checks(self):
        with patch.object(worldgen, '_check_text', return_value={'accepted': True}):
            result = worldgen.mutate(self.reference, self.repo)
        self.assertFalse(result['accepted'])

    def test_special_seed_filter_mutant_targets_filter_not_primary_names(self):
        text = self.catalog.read_text()
        mutant = worldgen._remove_filter_entry(text, 'Jungle')
        self.assertEqual(worldgen._runtime_array(text, 'PassNames'), worldgen._runtime_array(mutant, 'PassNames'))
        result = worldgen._check_text(self.reference, mutant)
        self.assertFalse(result['accepted'])
        self.assertEqual('DualDungeonDisabledPassNames', result['differences'][0]['field'])

    def test_changed_weight_source_expires_cached_reference(self):
        path = self.source / 'Terraria.GameContent.Generation/PassLegacy.cs'
        path.write_text(path.read_text().replace('0.1f', '0.2f'))
        result = worldgen.check(self.reference, self.repo, self.source)
        self.assertFalse(result['accepted'])
        self.assertEqual(['Terraria.GameContent.Generation/PassLegacy.cs'], result['sourceDifferences'])

    def test_without_source_root_freshness_is_not_claimed(self):
        result = worldgen.check(self.reference, self.repo)
        self.assertTrue(result['accepted'])
        self.assertEqual('not-rechecked-without-source-root', result['sourceFreshness'])

    def test_unknown_outer_statements_and_guard_calls_fail(self):
        path = self.source / 'Terraria/WorldGen.cs'
        original = path.read_text()
        for replacement in ['if (CustomGate())', 'while (skyblockWorldGen)']:
            path.write_text(original.replace('if (skyblockWorldGen && !Skyblock.denyAllGeneration)', replacement))
            with self.subTest(replacement=replacement), self.assertRaises(ValueError):
                worldgen.capture(self.source)

    def test_commented_out_array_or_constant_cannot_be_a_green_declaration(self):
        text = self.catalog.read_text()
        for broken in [text.replace('private static readonly string[] PassNames', '// private static readonly string[] PassNames'),
                       text.replace('public const string AddPassesSha256', '// public const string AddPassesSha256')]:
            self.catalog.write_text(broken)
            with self.subTest(broken=broken[:35]):
                self.assertFalse(worldgen.check(self.reference, self.repo)['accepted'])

    def test_conditional_compilation_is_rejected_instead_of_choosing_a_branch(self):
        text = self.catalog.read_text()
        self.catalog.write_text('#if false\n' + text + '\n#else\nprivate static string[] Other = [];\n#endif\n')
        self.assertFalse(worldgen.check(self.reference, self.repo)['accepted'])

    def test_unmodelled_pass_expression_is_rejected(self):
        path = self.source / 'Terraria/WorldGen.cs'
        path.write_text(path.read_text().replace('GenPassNameID.Alpha, delegate', 'ComputedName(), delegate'))
        with self.assertRaises(ValueError): worldgen.capture(self.source)

    def test_bad_root_containers_and_corrupt_provenance_are_value_errors(self):
        for bad in (None, [], 42, 'reference'):
            with self.subTest(bad=bad), self.assertRaises(ValueError): worldgen.validate(bad)
        for field in ('passes', 'sourceInputs', 'specialSeedFiltering', 'fingerprints', 'coverage'):
            bad = copy.deepcopy(self.reference)
            bad[field] = []
            bad['dataSha256'] = facts.data_digest(worldgen._payload(bad))
            with self.subTest(field=field), self.assertRaises(ValueError): worldgen.validate(bad)
        bad = copy.deepcopy(self.reference)
        bad['toolInputs']['tools/vanilla_data/worldgen.py'] = '0' * 64
        bad['dataSha256'] = facts.data_digest(worldgen._payload(bad))
        with self.assertRaisesRegex(ValueError, 'inputs changed'): worldgen.validate(bad)

    def test_nonfinite_weight_and_incomplete_index_are_rejected(self):
        for mutation in ('weight', 'index', 'guard'):
            bad = copy.deepcopy(self.reference)
            if mutation == 'weight': bad['passes'][0]['weight'] = True
            elif mutation == 'index': bad['passes'][0]['index'] = 7
            else: bad['passes'][0]['registrationGuards'] = ['CustomGate()']
            bad['dataSha256'] = facts.data_digest(worldgen._payload(bad))
            with self.subTest(mutation=mutation), self.assertRaises(ValueError): worldgen.validate(bad)


if __name__ == '__main__':
    unittest.main()
