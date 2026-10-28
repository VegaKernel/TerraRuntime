"""Observation comparison guards; no official binary or world generation in CI."""
from __future__ import annotations

import base64
import copy
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
from vanilla_data import pass_trace as trace


def observation(engine):
    cells = trace.CELL.pack(1, 2, -18, 36, 1, 80, 3, 4, 0, 2, 0) * 4
    state = dict(cellsBase64=base64.b64encode(cells).decode('ascii'),
                 cellsSha256=hashlib.sha256(cells).hexdigest(),
                 rng=dict(algorithm='terraria1458-unifiedrandom-inext21',
                          ownerAlias=['Main.rand', 'WorldGen.genRand'], inext=21, seedArray=list(range(56))))
    return dict(schemaVersion=1, kind='worldgen-pass-observation', engine=engine,
                **{'pass': 'SyntheticPass'}, scenario='unit-fixture',
                context=dict(width=2, height=2, worldSurface=1, seed=1458, netMode=2,
                             dedServ=True, gameMenu=False, skyblock=False, getGoodWorld=False,
                             dontStarve=False, remix=False, predecessorState='explicit-synthetic-not-natural-prefix'),
                projection=copy.deepcopy(trace.PROJECTION), input=copy.deepcopy(state), output=copy.deepcopy(state),
                provenance={'scope': 'invented comparison fixture, not a game capture'})


def set_cell(data, state, index, field, value):
    payload = bytearray(base64.b64decode(data[state]['cellsBase64']))
    values = list(trace.CELL.unpack_from(payload, index * trace.CELL.size))
    values[trace.FIELDS.index(field)] = value
    trace.CELL.pack_into(payload, index * trace.CELL.size, *values)
    data[state]['cellsBase64'] = base64.b64encode(payload).decode('ascii')
    data[state]['cellsSha256'] = hashlib.sha256(payload).hexdigest()


def cleanup_observation(engine):
    data = observation(engine)
    data['pass'] = 'QuickCleanup'
    data['scenario'] = 'quick-cleanup-ordinary-v1'
    data['context'].update(rockLayer=1, beachDistance=1, oceanLevel=41,
                           notTheBees=False, dualDungeons=False, roundLandmasses=False,
                           contentFactFields=list(trace.CONTENT_FIELDS),
                           contentFacts=[[1, 1, 0, 1, 0, 1]])
    return data


class PassTraceTests(unittest.TestCase):
    def setUp(self):
        self.official, self.runtime = observation('official'), observation('runtime')

    def test_equal_complete_states_compare_all_cells(self):
        report = trace.compare(self.official, self.runtime)
        self.assertTrue(report['accepted'])
        self.assertEqual(4, report['cellsCompared'])

    def test_first_difference_coordinates_and_fields_are_reported_with_full_counts(self):
        set_cell(self.runtime, 'output', 1, 'wall', 63)
        set_cell(self.runtime, 'output', 3, 'frameY', -36)
        report = trace.compare(self.official, self.runtime)
        self.assertFalse(report['accepted'])
        self.assertEqual(2, report['differentCells'])
        self.assertEqual(2, report['differentFields'])
        self.assertEqual({'index': 1, 'x': 1, 'y': 0,
                          'fields': [{'field': 'wall', 'expected': 2, 'actual': 63}]}, report['firstCellDifference'])

    def test_rng_difference_is_visible_when_cells_are_identical(self):
        self.runtime['output']['rng']['seedArray'][55] += 1
        report = trace.compare(self.official, self.runtime)
        self.assertFalse(report['accepted'])
        self.assertEqual(0, report['differentCells'])
        self.assertEqual({'field': 'seedArray', 'index': 55, 'expected': 55, 'actual': 56}, report['firstRngDifference'])

    def test_same_outputs_do_not_excuse_different_preimages(self):
        for mode in ('cell', 'rng', 'context', 'scenario'):
            data = copy.deepcopy(self.runtime)
            if mode == 'cell': set_cell(data, 'input', 0, 'type', 7)
            elif mode == 'rng': data['input']['rng']['inext'] += 1
            elif mode == 'context': data['context']['seed'] += 1
            else: data['scenario'] = 'another-fixture'
            with self.subTest(mode=mode):
                report = trace.compare(self.official, data)
                self.assertFalse(report['accepted'])
                self.assertFalse(report['comparable'])
                self.assertEqual('incomparable-input', report['classification'])
                self.assertNotIn('cellsCompared', report)

    def test_every_represented_field_is_observed(self):
        changes = dict(type=2, wall=3, frameX=-19, frameY=37, flags=3, liquidAmount=81,
                       tileColor=4, wallColor=5, shape=2, liquidKind=3)
        for field, value in changes.items():
            data = copy.deepcopy(self.runtime)
            set_cell(data, 'output', 3, field, value)
            with self.subTest(field=field):
                report = trace.compare(self.official, data)
                self.assertFalse(report['accepted'])
                self.assertEqual(field, report['firstCellDifference']['fields'][0]['field'])

    def test_incomplete_malformed_or_unsupported_state_is_rejected(self):
        for mode in ('missing-slot', 'hash', 'base64', 'projection', 'reserved', 'shape', 'flags', 'provenance'):
            data = copy.deepcopy(self.runtime)
            if mode == 'missing-slot': data['output']['rng']['seedArray'].pop()
            elif mode == 'hash': data['output']['cellsSha256'] = '0' * 64
            elif mode == 'base64': data['output']['cellsBase64'] = '!'
            elif mode == 'projection': data['projection']['fields'].pop()
            elif mode == 'reserved': set_cell(data, 'output', 0, 'reserved', 1)
            elif mode == 'shape': set_cell(data, 'output', 0, 'shape', 6)
            elif mode == 'flags': set_cell(data, 'output', 0, 'flags', 0x8000)
            else: data['provenance'] = {}
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                trace.compare(self.official, data)

    def test_wrong_engine_and_boolean_numeric_fields_are_rejected(self):
        for mode in ('engine', 'width', 'seed', 'netMode', 'dedServ', 'cursor'):
            data = copy.deepcopy(self.runtime)
            if mode == 'engine': data['engine'] = 'official'
            elif mode == 'cursor': data['input']['rng']['inext'] = True
            else: data['context'][mode] = 1 if mode == 'dedServ' else True
            with self.subTest(mode=mode), self.assertRaises(ValueError): trace.compare(self.official, data)

    def test_impossible_physical_rng_cursor_cannot_compare_green(self):
        self.official['output']['rng']['inext'] = 999
        self.runtime['output']['rng']['inext'] = 999
        with self.assertRaisesRegex(ValueError, 'physical RNG cursor'):
            trace.compare(self.official, self.runtime)

    def test_cleanup_complete_explicit_context_and_content_facts_compare(self):
        self.assertTrue(trace.compare(cleanup_observation('official'), cleanup_observation('runtime'))['accepted'])

    def test_cleanup_missing_or_unsupported_context_never_compares_green(self):
        for mode in ('missing-fact', 'missing-context', 'fields', 'duplicate', 'seed-variant', 'beach', 'depth'):
            left, right = cleanup_observation('official'), cleanup_observation('runtime')
            for data in (left, right):
                if mode == 'missing-fact': data['context']['contentFacts'][0][0] = 0
                elif mode == 'missing-context': del data['context']['rockLayer']
                elif mode == 'fields': data['context']['contentFactFields'].pop()
                elif mode == 'duplicate': data['context']['contentFacts'] *= 2
                elif mode == 'seed-variant': data['context']['notTheBees'] = True
                elif mode == 'beach': data['context']['beachDistance'] = True
                else: data['context']['oceanLevel'] += 1
            with self.subTest(mode=mode), self.assertRaises(ValueError): trace.compare(left, right)

    def test_cleanup_content_fact_disagreement_is_incomparable_input(self):
        left, right = cleanup_observation('official'), cleanup_observation('runtime')
        right['context']['contentFacts'][0][1] = 0
        report = trace.compare(left, right)
        self.assertFalse(report['accepted'])
        self.assertEqual(['context'], report['inputDifferences'])

    def test_file_size_and_duplicate_json_guards_apply_before_comparison(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'bad.json'
            path.write_text('{"engine":"runtime","engine":"official"}', encoding='utf-8')
            with self.assertRaises(ValueError): trace.load(path, 'runtime')
            with path.open('wb') as stream: stream.truncate(trace.MAX_FILE_BYTES + 1)
            with self.assertRaisesRegex(ValueError, 'byte bound'): trace.load(path, 'runtime')


if __name__ == '__main__': unittest.main()
