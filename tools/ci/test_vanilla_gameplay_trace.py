"""Bounded offline gameplay comparison contract; fixtures are invented, not official evidence."""
from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
from vanilla_data import gameplay_trace as trace


def observation(engine):
    inventory = [dict(type=0, stack=0, ammoFamily=0, consumable=False) for _ in range(58)]
    inventory[0] = dict(type=39, stack=1, ammoFamily=0, consumable=False)
    inventory[1] = dict(type=40, stack=30, ammoFamily=40, consumable=True)
    state = dict(inventory=inventory, cyclingOffset=0, cyclingCooldown=0,
                 rng=dict(algorithm='terraria1458-unifiedrandom-inext21', ownerAlias=['Main.rand'],
                          inext=21, seedArray=list(range(56))))
    frames = []
    for index in range(24):
        before = copy.deepcopy(state)
        state['inventory'][1]['stack'] -= 1
        state['cyclingOffset'] += 1
        state['rng']['inext'] = (21 + index + 1) % 56
        frames.append(dict(step=index, before=before, selectedSlot=1, usedAmmoItemId=40,
                           canShoot=True, conserved=False,
                           launch=dict(projectile=1, damage=9, speedBits=1092626678, knockBackBits=1073741824),
                           state=copy.deepcopy(state)))
    provenance = dict(entry=trace.OFFICIAL_ENTRY if engine == 'official' else trace.RUNTIME_ENTRY,
                      control='normal', probeSha256='a' * 64, officialAssemblySha256='b' * 64,
                      dependenciesSha256={'FNA.dll': 'c' * 64})
    if engine == 'runtime':
        provenance['runtimeAssemblySha256'] = 'd' * 64
    return dict(schemaVersion=1, kind='gameplay-behavior-observation', engine=engine,
                scenario=trace.SCENARIO, seed=1458, context=copy.deepcopy(trace.CONTEXT),
                input=copy.deepcopy(frames[0]['before']), frames=frames, provenance=provenance,
                executedDlls={'GameplayProbe.dll': 'a' * 64, trace.RUNTIME_DLL: 'd' * 64})


class GameplayTraceTests(unittest.TestCase):
    def setUp(self):
        self.official, self.runtime = observation('official'), observation('runtime')

    def test_equal_complete_physical_projection(self):
        report = trace.compare(self.official, self.runtime)
        self.assertTrue(report['accepted'])
        self.assertEqual(24, report['framesCompared'])
        self.assertEqual(0, report['differentFields'])

    def test_last_physical_inventory_slot_difference_detected(self):
        self.runtime['frames'][-1]['state']['inventory'][57] = dict(type=41, stack=1, ammoFamily=40, consumable=True)
        result = trace.compare(self.official, self.runtime)
        self.assertFalse(result['accepted'])
        self.assertIn('inventory[57]', result['firstDifference']['field'])

    def test_all_56_rng_slots_and_cursor_are_compared(self):
        for key, index in (('seedArray', 0), ('seedArray', 55), ('inext', None)):
            with self.subTest(key=key, index=index):
                runtime = copy.deepcopy(self.runtime)
                rng = runtime['frames'][-1]['state']['rng']
                if index is None:
                    rng[key] += 1
                else:
                    rng[key][index] += 1
                self.assertFalse(trace.compare(self.official, runtime)['accepted'])

    def test_launch_compares_single_bits_without_tolerance(self):
        self.runtime['frames'][-1]['launch']['speedBits'] += 1
        result = trace.compare(self.official, self.runtime)
        self.assertEqual('frames[23].launch.speedBits', result['firstDifference']['field'])

    def test_selected_slot_and_conservation_are_observed(self):
        self.runtime['frames'][-1]['selectedSlot'] = 50
        self.runtime['frames'][-1]['conserved'] = True
        self.assertEqual(2, trace.compare(self.official, self.runtime)['differentFields'])

    def test_different_seed_is_incomparable(self):
        self.runtime['seed'] = 42
        self.assertEqual('incomparable-input', trace.compare(self.official, self.runtime)['classification'])

    def test_different_complete_input_is_incomparable(self):
        self.runtime['input']['rng']['seedArray'][55] += 1
        self.runtime['frames'][0]['before'] = copy.deepcopy(self.runtime['input'])
        self.assertEqual('incomparable-input', trace.compare(self.official, self.runtime)['classification'])

    def test_missing_middle_slot_rng_and_frames_rejected(self):
        for edit in (lambda d: d['input']['inventory'].pop(20),
                     lambda d: d['frames'][-1]['state']['rng']['seedArray'].pop(),
                     lambda d: d['frames'].pop(12)):
            value = copy.deepcopy(self.runtime)
            edit(value)
            with self.assertRaises(ValueError):
                trace.validate(value, 'runtime')

    def test_discontinuous_preimage_rejected(self):
        self.runtime['frames'][7]['before']['inventory'][1]['stack'] += 1
        with self.assertRaisesRegex(ValueError, 'preimage'):
            trace.validate(self.runtime, 'runtime')

    def test_wrong_or_unknown_projection_rejected(self):
        edits = (lambda d: d['input'].update(hiddenLedger=1),
                 lambda d: d['context'].update(ammoBox=1),
                 lambda d: d['frames'][2].update(step=1),
                 lambda d: d.update(scenario='unknown'),
                 lambda d: d['frames'][-1]['launch'].update(speedBits=0x7f800000),
                 lambda d: d['input']['rng'].update(inext=True))
        for edit in edits:
            value = copy.deepcopy(self.runtime)
            edit(value)
            with self.assertRaises(ValueError):
                trace.validate(value, 'runtime')

    def test_provenance_entry_and_dll_identity_required(self):
        for edit in (lambda d: d['provenance'].update(entry='invented'),
                     lambda d: d['executedDlls'].update({'../foreign.dll': 'a' * 64}),
                     lambda d: d['provenance'].update(runtimeAssemblySha256='e' * 64),
                     lambda d: d['executedDlls'].pop('GameplayProbe.dll')):
            value = copy.deepcopy(self.runtime)
            edit(value)
            with self.assertRaises(ValueError):
                trace.validate(value, 'runtime')

    def test_load_hashes_exact_bounded_bytes(self):
        raw = json.dumps(self.official).encode()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'trace.json'
            path.write_bytes(raw)
            value, digest = trace.load_with_hash(path, 'official')
            self.assertEqual(self.official, value)
            self.assertEqual(hashlib.sha256(raw).hexdigest(), digest)
            path.write_bytes(b' ' * (trace.MAX_BYTES + 1))
            with self.assertRaises(ValueError):
                trace.load_with_hash(path, 'official')

    def test_duplicate_json_keys_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'trace.json'
            path.write_text('{"schemaVersion":1,"schemaVersion":1}', encoding='utf-8')
            with self.assertRaises(ValueError):
                trace.load_with_hash(path, 'official')


if __name__ == '__main__':
    unittest.main()
