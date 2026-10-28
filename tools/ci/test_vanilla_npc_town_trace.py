"""Dryad profile schema/mutation checks; actual AI acceptance uses fresh probes."""
from copy import deepcopy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from tools.vanilla_data import npc_town_trace as trace


def fixture(engine):
    npc = {name: ['00000000'] * size for name, size in trace.VECTORS.items()}
    npc.update({name: '00000000' for name in trace.FLOATS})
    npc.update({name: 0 for name in trace.INTS}); npc.update({name: False for name in trace.BOOLS})
    npc.update(type=20, netId=20, scale='3f800000', life=250, lifeMax=250, timeLeft=750,
               frameCounter='0000000000000000', breath=200)
    state = dict(npc=npc, rng=dict(algorithm='terraria1458-unifiedrandom-inext21', owner='Main.rand',
                                  inext=0, seedArray=list(range(56))))
    digest = 'a' * 64
    provenance = dict(entry=trace.OFFICIAL_ENTRY if engine == 'official' else trace.RUNTIME_ENTRY,
                      control='normal', officialAssemblySha256=digest, dependenciesSha256={'FNA.dll': digest}, probeSha256=digest)
    if engine == 'runtime': provenance['runtimeAssemblySha256'] = digest
    data = dict(schemaVersion=1, kind='npc-behavior-observation', engine=engine, scenario=trace.SCENARIO, seed=1458,
                context=deepcopy(trace.CONTEXT), input=deepcopy(state), frames=[], provenance=provenance,
                executedDlls={'NpcProbe.dll': digest, trace.RUNTIME_DLL: digest})
    for i in range(trace.FRAME_COUNT):
        events = [dict(id=0, slot=0, lifetime=60, emote=87)] if i == 59 else [dict(id=1, slot=0, lifetime=90, emote=3)] if i == 149 else []
        data['frames'].append(dict(index=i, before=deepcopy(state), after=deepcopy(state), emotes=events))
    return data


class TownTraceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = fixture('official'); cls.current = fixture('runtime')

    def test_all496_frames_match(self):
        self.assertTrue(trace.compare(self.source, self.current)['accepted'])

    def test_last_binary32_bit_is_detected(self):
        runtime = deepcopy(self.current); runtime['frames'][-1]['after']['npc']['velocity'][0] = '00000001'
        result = trace.compare(self.source, runtime)
        self.assertEqual('npc-state-mismatch', result['classification']); self.assertEqual(495, result['frame'])

    def test_last_binary64_frame_clock_bit_is_detected(self):
        runtime = deepcopy(self.current); runtime['frames'][-1]['after']['npc']['frameCounter'] = '0000000000000001'
        self.assertEqual('npc-state-mismatch', trace.compare(self.source, runtime)['classification'])

    def test_event_value_difference_is_detected(self):
        runtime = deepcopy(self.current); runtime['frames'][59]['emotes'][0]['emote'] = 86
        result = trace.compare(self.source, runtime)
        self.assertEqual('emote-mismatch', result['classification']); self.assertEqual(59, result['frame'])

    def test_missing_emote_is_detected(self):
        runtime = deepcopy(self.current); runtime['frames'][149]['emotes'] = []
        self.assertEqual('emote-mismatch', trace.compare(self.source, runtime)['classification'])

    def test_last_rng_slot_is_detected(self):
        runtime = deepcopy(self.current); runtime['frames'][-1]['after']['rng']['seedArray'][-1] += 1
        self.assertEqual('rng-state-mismatch', trace.compare(self.source, runtime)['classification'])

    def test_missing_frame_or_incomplete_rng_refused(self):
        for mutation in ('frame', 'rng'):
            runtime = deepcopy(self.current)
            if mutation == 'frame': runtime['frames'].pop()
            else: runtime['input']['rng']['seedArray'].pop()
            with self.subTest(mutation=mutation), self.assertRaises(ValueError): trace.validate(runtime, 'runtime')

    def test_unknown_context_or_npc_fields_refused(self):
        for place in ('context', 'npc'):
            runtime = deepcopy(self.current)
            target = runtime['context'] if place == 'context' else runtime['input']['npc']
            target['invented'] = 0
            with self.subTest(place=place), self.assertRaises(ValueError): trace.validate(runtime, 'runtime')

    def test_broken_continuity_refused(self):
        runtime = deepcopy(self.current); runtime['frames'][4]['before']['npc']['life'] -= 1
        with self.assertRaises(ValueError): trace.validate(runtime, 'runtime')

    def test_nonfinite_double_refused(self):
        runtime = deepcopy(self.current); runtime['input']['npc']['frameCounter'] = '7ff0000000000000'
        with self.assertRaises(ValueError): trace.validate(runtime, 'runtime')

    def test_unknown_field_cannot_be_expected_default(self):
        runtime = deepcopy(self.current); runtime['input']['npc']['knockBackResist'] = None
        with self.assertRaises(ValueError): trace.validate(runtime, 'runtime')

    def test_seed_difference_is_incomparable(self):
        runtime = deepcopy(self.current); runtime['seed'] = 42
        self.assertEqual('incomparable-input', trace.compare(self.source, runtime)['classification'])

    def test_home_boolean_not_integer(self):
        runtime = deepcopy(self.current); runtime['context']['home']['x'] = 40.0
        with self.assertRaises(ValueError): trace.validate(runtime, 'runtime')

    def test_bounded_reader_hashes_consumed_bytes(self):
        import hashlib
        raw = json.dumps(self.source).encode()
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'town.json'; path.write_bytes(raw)
            _, digest = trace.load_with_hash(path, 'official'); self.assertEqual(hashlib.sha256(raw).hexdigest(), digest)
            with patch.object(trace, 'MAX_BYTES', len(raw) - 1):
                with self.assertRaises(ValueError): trace.load_with_hash(path, 'official')


if __name__ == '__main__': unittest.main()
