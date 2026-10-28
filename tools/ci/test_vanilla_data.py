"""Data-tool regressions; no game DLL, server, Rust or production build required."""
from __future__ import annotations

import copy
import gzip
import io
import json
from pathlib import Path
import struct
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
from vanilla_data import capture, catalog, cli
from vanilla_data.csharp import mask


def official_npcs():
    path = ROOT / 'tests/TerraRuntime.Tests/Fixtures/npc-source-default-roster-official-1458.json.gz'
    with gzip.open(path, 'rt', encoding='utf-8') as stream:
        rows = json.load(stream)
    return [{name: row[name] for name in ('requested',) + catalog.NPC_FIELDS} for row in rows]


def valid_capture():
    pin = capture.reference()
    data = dict(schemaVersion=1, gameVersion=pin['gameVersion'], assemblySha256=pin['assemblySha256'],
                dependenciesSha256=pin['dependenciesSha256'],
                bounds={name: pin[name] for name in ('npcPositiveCount', 'npcNegativeCount', 'itemCount')},
                profile=dict(name='classic-constructor-coreclr', seed=1458, gameMenu=True, netMode=2,
                             hardMode=False, getGoodWorld=False, remixWorld=False,
                             hostOs='fixture-host', framework='.NET fixture'),
                datasets={'npc-defaults': official_npcs(), 'item-placement': [
                    dict(id=identity, createTile=-1, createWall=-1, placeStyle=0, consumable=False)
                    for identity in range(1, pin['itemCount'])]},
                captureInputs=capture.pins(), probeSha256='a' * 64)
    data['dataSha256'] = capture.data_digest(data['datasets'])
    return data


class NpcDataTests(unittest.TestCase):
    def test_entire_independent_official_fixture_matches_committed_metadata(self):
        data = {'datasets': {'npc-defaults': official_npcs()}}
        rows = catalog.expected_rows(data, 'npc-defaults')
        result = catalog.compare('npc-defaults', rows, catalog.read_actual('npc-defaults', ROOT / catalog.TARGETS['npc-defaults']))
        self.assertTrue(result['accepted'], result)
        self.assertEqual(756, result['actualRows'])
        self.assertEqual(761, len(data['datasets']['npc-defaults']))

    def test_changes_at_first_and_last_identity_and_one_single_ulp_are_visible(self):
        rows = catalog.read_actual('npc-defaults', ROOT / catalog.TARGETS['npc-defaults'])
        for index, field in [(0, 'lifeMax'), (-1, 'damage'), (0, 'knockBackResist')]:
            with self.subTest(index=index, field=field):
                mutant = copy.deepcopy(rows)
                if field == 'knockBackResist':
                    bits = struct.unpack('<I', struct.pack('<f', mutant[index][field]))[0]
                    mutant[index][field] = struct.unpack('<f', struct.pack('<I', bits + 1))[0]
                else:
                    mutant[index][field] += 1
                result = catalog.compare('npc-defaults', rows, mutant)
                self.assertFalse(result['accepted'])
                self.assertEqual(field, result['differences'][0]['field'])

    def test_npc_column_reordering_and_unknown_expressions_fail(self):
        text = (ROOT / catalog.TARGETS['npc-defaults']).read_text(encoding='utf-8')
        for broken in [text.replace('int Width, int Height', 'int Height, int Width'),
                       text.replace('new(-65,', 'new(unknown(),', 1),
                       text.replace('),\n', '),,\n', 1), text.replace('short NetId', 'int NetId', 1)]:
            with self.assertRaises(ValueError):
                catalog.read_npcs(broken)

    def test_emitted_npcs_keep_all_fields_and_non_data_code(self):
        template = (ROOT / catalog.TARGETS['npc-defaults']).read_text(encoding='utf-8')
        rows = catalog.expected_rows({'datasets': {'npc-defaults': official_npcs()}}, 'npc-defaults')
        generated = catalog.emit('npc-defaults', rows, template)
        self.assertTrue(catalog.compare('npc-defaults', rows, catalog.read_npcs(generated))['accepted'])
        self.assertEqual(template[template.index('    public static bool TryGet'):],
                         generated[generated.index('    public static bool TryGet'):])
        self.assertNotIn('0.474000006', generated)


class PlacementDataTests(unittest.TestCase):
    def setUp(self):
        self.template = (ROOT / catalog.TARGETS['item-placement']).read_text(encoding='utf-8')
        self.rows = [dict(id=2, createTile=0, createWall=-1, placeStyle=0, consumable=True),
                     dict(id=26, createTile=-1, createWall=3, placeStyle=0, consumable=False)]

    def test_independently_known_bytes_match_fields_including_signed_wall_and_style_bit(self):
        text = catalog.emit('item-placement', self.rows, self.template)
        self.assertIn('0x02, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x80', text)
        self.assertIn('0x1A, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x00, 0x00', text)
        self.assertEqual(self.rows, catalog.read_items(text))

    def test_truncation_header_drift_and_unknown_tokens_fail(self):
        text = catalog.emit('item-placement', self.rows, self.template)
        for broken in [text.replace('Count = 2;', 'Count = 3;'),
                       text.replace('0x02, ', '', 1), text.replace('0x02', '0xGG', 1),
                       text.replace('0x02,', '0x02,,', 1)]:
            with self.assertRaises(ValueError):
                catalog.read_items(broken)

    def test_each_placement_field_is_compared(self):
        for field in catalog.ITEM_FIELDS:
            mutant = copy.deepcopy(self.rows)
            value = mutant[-1][field]
            mutant[-1][field] = not value if type(value) is bool else value + 1
            with self.subTest(field=field):
                self.assertFalse(catalog.compare('item-placement', self.rows, mutant)['accepted'])

    def test_whole_committed_binary_layout_reproduces_without_changing_lookups(self):
        rows = catalog.read_items(self.template)
        self.assertEqual(3523, len(rows))
        self.assertEqual(self.template, catalog.emit('item-placement', rows, self.template))

    def test_comments_and_strings_cannot_hide_changed_count_or_fake_arrays(self):
        for fake in ['// public const int Count = 3523;\n',
                     '/* public const int Count = 3523; */\n',
                     'const string fake = "public const int Count = 3523;";\n']:
            broken = fake + self.template.replace('Count = 3523;', 'Count = 3524;')
            with self.subTest(fake=fake), self.assertRaises(ValueError):
                catalog.read_items(broken)
        commented = '// public const int Count = 999;\n' + self.template
        emitted = catalog.emit('item-placement', self.rows, commented)
        self.assertTrue(emitted.startswith('// public const int Count = 999;\n'))
        self.assertEqual(self.rows, catalog.read_items(emitted))

    def test_duplicate_actual_count_declarations_are_refused(self):
        with self.assertRaises(ValueError):
            catalog.read_items('public const int Count = 3523;\n' + self.template)

    def test_conditional_compilation_cannot_select_an_inactive_good_array(self):
        text = '#if false\n' + self.template + '\n#else\nbyte[] Records = [0x01];\n#endif\n'
        with self.assertRaisesRegex(ValueError, 'Conditional compilation'):
            catalog.read_items(text)


class CaptureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.good = valid_capture()

    def test_complete_context_and_provenance_validate(self):
        capture.validate(self.good)

    def test_dropped_duplicate_unknown_and_mistyped_capture_cells_fail(self):
        for operation in ('drop', 'duplicate', 'unknown', 'boolean', 'missing_field'):
            data = copy.deepcopy(self.good)
            rows = data['datasets']['npc-defaults']
            if operation == 'drop': rows.pop()
            elif operation == 'duplicate': rows[-1] = copy.deepcopy(rows[0])
            elif operation == 'unknown': rows[-1]['requested'] = rows[-1]['netId'] = 999
            elif operation == 'boolean': rows[0]['noGravity'] = 0
            else: del rows[0]['width']
            with self.subTest(operation=operation), self.assertRaises(ValueError):
                capture.validate(data)

    def test_source_version_context_data_and_tool_drift_are_refused(self):
        for operation in ('version', 'assembly', 'dependency', 'context', 'data', 'tool', 'schema', 'probe'):
            data = copy.deepcopy(self.good)
            if operation == 'version': data['gameVersion'] = '1.4.4.9'
            elif operation == 'assembly': data['assemblySha256'] = '0' * 64
            elif operation == 'dependency': data['dependenciesSha256']['FNA.dll'] = '0' * 64
            elif operation == 'context': data['profile']['gameMenu'] = False
            elif operation == 'data': data['datasets']['npc-defaults'][0]['damage'] += 1
            elif operation == 'tool': data['captureInputs']['global.json'] = '0' * 64
            elif operation == 'schema': data['schemaVersion'] = True
            else: del data['probeSha256']
            with self.subTest(operation=operation), self.assertRaises(ValueError):
                capture.validate(data)

    def test_duplicate_json_keys_and_nonfinite_numbers_fail(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'bad.json'
            for text in ['{"id":1,"id":2}', '{"id":NaN}']:
                path.write_text(text, encoding='utf-8')
                with self.assertRaises(ValueError): capture.read_json(path)

    def test_nonobject_roots_profiles_and_missing_host_metadata_fail(self):
        values = [None, [], 1, {'profile': None}]
        for field in ('profile', 'datasets'):
            for wrong in (None, [], 1):
                data = copy.deepcopy(self.good)
                data[field] = wrong
                values.append(data)
        data = copy.deepcopy(self.good)
        del data['profile']['hostOs']
        values.append(data)
        for value in values:
            with self.subTest(value=type(value)), self.assertRaises(ValueError):
                capture.validate(value)


class CSharpViewsTests(unittest.TestCase):
    def test_comments_literals_and_interpolations_preserve_offsets(self):
        values = ['// fake\nactual', '/* fake */actual', '"fake // comment" actual',
                  '@"fake ""quote"" /* comment */" actual', '"""fake "quote" """ actual',
                  '$"fake {new[] { "inner" }} text" actual', '$$"""fake {{value}} text""" actual',
                  "'\\\'' actual", '/* line\r\ncomment */actual']
        for text in values:
            with self.subTest(text=text):
                code, comments = mask(text)
                self.assertEqual(len(text), len(code))
                self.assertEqual(len(text), len(comments))
                self.assertEqual(text.index('actual'), code.index('actual'))
                self.assertNotIn('fake', code)
                self.assertEqual([i for i, char in enumerate(text) if char in '\r\n'],
                                 [i for i, char in enumerate(code) if char in '\r\n'])

    def test_unterminated_input_is_refused(self):
        for text in ['/* open', '"open', '@"open', '"""open', '$"{ open']:
            with self.subTest(text=text), self.assertRaises(ValueError): mask(text)


class WorkflowTests(unittest.TestCase):
    def test_unadmitted_binary_is_refused_before_build_or_execution(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'not-the-game.exe'
            path.write_bytes(b'not an admitted assembly')
            with patch('sys.stderr', new_callable=io.StringIO), patch.object(cli.subprocess, 'run') as run:
                self.assertEqual(2, cli.main(['capture', '--assembly', str(path), '--output',
                                              str(Path(temporary) / 'capture.json')]))
                run.assert_not_called()

    def test_mutation_checker_detects_all_named_mutants_without_touching_production(self):
        data = {'datasets': {'npc-defaults': official_npcs()}}
        path = ROOT / catalog.TARGETS['npc-defaults']
        before = path.read_bytes()
        report = cli._mutate(SimpleNamespace(dataset='npc-defaults', repo=ROOT), data)
        self.assertTrue(report['accepted'], report)
        self.assertEqual(32, len(report['results'][0]['mutants']))
        self.assertEqual(before, path.read_bytes())

    def test_a_checker_that_always_succeeds_cannot_pass_mutations(self):
        data = {'datasets': {'npc-defaults': official_npcs()}}
        with patch.object(cli, 'compare', return_value={'accepted': True}):
            report = cli._mutate(SimpleNamespace(dataset='npc-defaults', repo=ROOT), data)
        self.assertFalse(report['accepted'])
        self.assertTrue(any(not mutant['detected'] for mutant in report['results'][0]['mutants']))

    def test_generator_failure_is_not_misreported_as_a_checker_kill(self):
        data = {'datasets': {'npc-defaults': official_npcs()}}
        with patch.object(cli, 'emit', side_effect=ValueError('broken emitter')):
            with self.assertRaisesRegex(ValueError, 'broken emitter'):
                cli._mutate(SimpleNamespace(dataset='npc-defaults', repo=ROOT), data)

    def test_cli_nonzero_for_invalid_reference_and_existing_output_is_preserved(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'capture.json'
            path.write_text('{}', encoding='utf-8')
            with patch('sys.stderr', new_callable=io.StringIO):
                self.assertEqual(2, cli.main(['check', 'all', '--reference', str(path)]))
            with self.assertRaises(FileExistsError): cli.write_new(path, b'replacement')
            self.assertEqual('{}', path.read_text())


if __name__ == '__main__':
    unittest.main()
