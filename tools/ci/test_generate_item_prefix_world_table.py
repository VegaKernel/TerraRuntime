"""Source-backed tests for the numeric contextual-prefix table generator; no official DLL needed."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
_spec = importlib.util.spec_from_file_location(
    'item_prefix_world_generator', Path(__file__).with_name('generate_item_prefix_world_table.py'))
_generator = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_generator)
generate = _generator.generate

FIXTURE = ROOT / 'tests/TerraRuntime.Tests/Fixtures/prefix-world-acceptance-official-1458.json'
NORMAL = ROOT / 'src/TerraRuntime.Gameplay/Items/VanillaItemPrefixTable1458.cs'
GENERATED = ROOT / 'src/TerraRuntime.Gameplay/Items/VanillaItemPrefixWorld1458.Generated.cs'


class ItemPrefixWorldGeneratorTests(unittest.TestCase):
    def test_source_cells_reproduce_committed_output(self):
        expected = GENERATED.read_text(encoding='utf-8-sig')
        self.assertEqual(expected, generate(FIXTURE, NORMAL))
        self.assertEqual(expected, generate(FIXTURE, NORMAL))

    def test_lf_and_crlf_inputs_reproduce_identical_output(self):
        expected = generate(FIXTURE, NORMAL)
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            for label, ending in [('LF', '\n'), ('CRLF', '\r\n')]:
                with self.subTest(ending=label):
                    fixture = folder / 'cells.json'
                    normal = folder / 'normal.cs'
                    fixture.write_text(FIXTURE.read_text(encoding='utf-8-sig'), encoding='utf-8', newline=ending)
                    normal.write_text(NORMAL.read_text(encoding='utf-8-sig'), encoding='utf-8', newline=ending)
                    self.assertEqual(expected, generate(fixture, normal))

    def test_malformed_source_cells_are_refused(self):
        original = json.loads(FIXTURE.read_text(encoding='utf-8-sig'))
        cases = []
        cells = copy.deepcopy(original)
        cells['cells'].pop()
        cases.append(('missing-context', cells, 'exactly 200'))
        cells = copy.deepcopy(original)
        cells['cells'][1] = copy.deepcopy(cells['cells'][0])
        cases.append(('duplicate-context', cells, 'duplicate source context'))
        cells = copy.deepcopy(original)
        cells['cells'][1]['damage'] += 1
        cases.append(('inconsistent-variant-damage', cells, 'inconsistent selected'))
        cells = copy.deepcopy(original)
        cells['cells'][0]['family'].reverse()
        cases.append(('changed-family', cells, 'inconsistent default'))
        cells = copy.deepcopy(original)
        next(row for row in cells['cells'] if row['item'] == 544 and row['bits'] == 3)['variant'] = 'None'
        cases.append(('invalid-priority-mask', cells, 'unsupported source selector'))
        cells = copy.deepcopy(original)
        cells['cells'][0]['noKnockBack'] = 0
        cases.append(('malformed-boolean', cells, 'must be booleans'))
        with tempfile.TemporaryDirectory() as temporary:
            fixture = Path(temporary) / 'cells.json'
            for label, cells, message in cases:
                with self.subTest(case=label):
                    fixture.write_text(json.dumps(cells), encoding='utf-8', newline='\n')
                    with self.assertRaisesRegex(ValueError, message):
                        generate(fixture, NORMAL)


if __name__ == '__main__':
    unittest.main()
