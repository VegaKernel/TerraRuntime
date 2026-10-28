"""Validation/comparison tests, not substitutes for fresh original/runtime execution."""
from copy import deepcopy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from tools.vanilla_data import npc_trace as trace


def observation(engine='official'):
    npc = {name: ['00000000'] * size for name, size in trace.VECTORS.items()}
    npc.update({name: '00000000' for name in trace.FLOATS})
    npc.update({name: 0 for name in trace.INTS})
    npc.update({name: False for name in trace.BOOLS})
    npc.update(type=243, netId=243, aiStyle=3, width=30, height=114, life=2000, lifeMax=4000,
               timeLeft=750, scale='3f800000', active=True, target=7, direction=1, spriteDirection=1)
    state = {'npc': npc, 'rng': {'algorithm': 'terraria1458-unifiedrandom-inext21', 'owner': 'Main.rand',
                               'inext': 0, 'seedArray': list(range(56))}}
    context = dict(frames=16, widthTiles=256, heightTiles=128, worldSurface=100, netMode=2, gameMode=0,
                   invasionType=0, tiles='all-empty', carrier=trace.CARRIER, justHitFrames=trace.HIT_FRAMES.copy())
    context.update({name: name == 'dedServ' for name in
                    ('dedServ', 'gameMenu', 'dayTime', 'hardMode', 'expert', 'master', 'goodWorld', 'remix', 'eclipse')})
    context['player'] = dict(slot=7, position=['43c80000', '42c80000'], width=20, height=42,
                             active=True, dead=False, ghost=False, aggro=0, frozen=False)
    digest = 'a' * 64
    provenance = dict(entry=trace.OFFICIAL_ENTRY, control='normal', officialAssemblySha256=digest,
                      dependenciesSha256={'FNA.dll': digest}, probeSha256=digest, methodIlSha256=digest,
                      rawAfterAiJustHit=[index in trace.HIT_FRAMES for index in range(16)], carrierRetirement=trace.RETIREMENT)
    if engine == 'runtime':
        provenance.update(entry=trace.RUNTIME_ENTRY, runtimeAssemblySha256=digest, runtimeRng='current source')
        provenance['rawAfterAiJustHit'] = [False] * 16
    data = dict(schemaVersion=1, kind='npc-behavior-observation', engine=engine, scenario=trace.SCENARIO,
                seed=1458, context=context, input=deepcopy(state), frames=[], provenance=provenance,
                executedDlls={trace.RUNTIME_DLL: digest, 'NpcProbe.dll': digest})
    for index in range(16):
        state = deepcopy(state)
        hit = index in trace.HIT_FRAMES
        state['npc']['justHit'] = hit
        before = deepcopy(state)
        state['npc']['justHit'] = False
        data['frames'].append(dict(index=index, justHit=hit, before=before, after=deepcopy(state)))
    return data


def change_after(data, frame, section, field, value):
    # Carry a mutant forward rather than invalidating the frame-continuity contract.
    for index in range(frame, 16):
        data['frames'][index]['after'][section][field] = deepcopy(value)
        if index > frame:
            data['frames'][index]['before'][section][field] = deepcopy(value)


class NpcTraceTests(unittest.TestCase):
    def setUp(self):
        self.official, self.runtime = observation(), observation('runtime')

    def test_full_named_projection_matches(self):
        self.assertTrue(trace.compare(self.official, self.runtime)['accepted'])

    def test_changed_float_last_bit_is_detected(self):
        change_after(self.runtime, 3, 'npc', 'velocity', ['00000001', '00000000'])
        result = trace.compare(self.official, self.runtime)
        self.assertEqual('npc-state-mismatch', result['classification'])
        self.assertEqual(3, result['frame'])
        self.assertEqual('00000000', result['expected'])
        self.assertEqual('00000001', result['actual'])

    def test_physical_rng_last_slot_detected(self):
        words = list(range(56)); words[-1] += 1
        change_after(self.runtime, 15, 'rng', 'seedArray', words)
        self.assertEqual('rng-state-mismatch', trace.compare(self.official, self.runtime)['classification'])

    def test_rng_cursor_detected(self):
        change_after(self.runtime, 0, 'rng', 'inext', 1)
        self.assertEqual('rng-state-mismatch', trace.compare(self.official, self.runtime)['classification'])

    def test_missing_last_frame_refused(self):
        self.runtime['frames'].pop()
        with self.assertRaises(ValueError): trace.validate(self.runtime, 'runtime')

    def test_frame_preimage_mutation_is_invalid(self):
        self.runtime['frames'][4]['before']['npc']['life'] -= 1
        with self.assertRaises(ValueError): trace.validate(self.runtime, 'runtime')

    def test_reordered_frames_refused(self):
        self.runtime['frames'][2], self.runtime['frames'][3] = self.runtime['frames'][3], self.runtime['frames'][2]
        with self.assertRaises(ValueError): trace.validate(self.runtime, 'runtime')

    def test_unknown_npc_field_refused(self):
        self.runtime['input']['npc']['invented'] = 1
        with self.assertRaises(ValueError): trace.validate(self.runtime, 'runtime')

    def test_missing_local_ai_refused(self):
        del self.runtime['input']['npc']['localAi']
        with self.assertRaises(ValueError): trace.validate(self.runtime, 'runtime')

    def test_incomplete_rng_refused(self):
        self.runtime['input']['rng']['seedArray'].pop()
        with self.assertRaises(ValueError): trace.validate(self.runtime, 'runtime')

    def test_unknown_context_refused(self):
        self.runtime['context']['arbitrary'] = False
        with self.assertRaises(ValueError): trace.validate(self.runtime, 'runtime')

    def test_nonfinite_float_refused(self):
        for value in ('7f800000', 'ff800000', '7fc00000'):
            with self.subTest(value=value):
                self.runtime['input']['npc']['rotation'] = value
                with self.assertRaises(ValueError): trace.validate(self.runtime, 'runtime')

    def test_numeric_float_instead_of_bits_refused(self):
        self.runtime['input']['npc']['rotation'] = 0.0
        with self.assertRaises(ValueError): trace.validate(self.runtime, 'runtime')

    def test_boolean_not_integer(self):
        self.runtime['input']['npc']['target'] = True
        with self.assertRaises(ValueError): trace.validate(self.runtime, 'runtime')

    def test_other_seed_is_incomparable(self):
        self.runtime['seed'] = 42
        self.assertEqual('incomparable-input', trace.compare(self.official, self.runtime)['classification'])

    def test_other_player_position_is_incomparable(self):
        self.runtime['context']['player']['position'][0] = '43c80001'
        self.assertEqual('incomparable-input', trace.compare(self.official, self.runtime)['classification'])

    def test_other_valid_initial_state_is_incomparable(self):
        self.runtime['input']['npc']['life'] = 1999
        for frame in self.runtime['frames']:
            frame['before']['npc']['life'] = frame['after']['npc']['life'] = 1999
        self.assertEqual('incomparable-input', trace.compare(self.official, self.runtime)['classification'])

    def test_wrong_execution_entry_refused(self):
        self.runtime['provenance']['entry'] = 'compiled-test-replay'
        with self.assertRaises(ValueError): trace.validate(self.runtime, 'runtime')

    def test_raw_direct_ai_difference_cannot_be_silently_removed(self):
        self.official['provenance']['rawAfterAiJustHit'][2] = False
        with self.assertRaises(ValueError): trace.validate(self.official, 'official')

    def test_unknown_retirement_operation_refused(self):
        self.runtime['provenance']['carrierRetirement'] = 'normalize all changed state'
        with self.assertRaises(ValueError): trace.validate(self.runtime, 'runtime')

    def test_retirement_cannot_mask_timer_difference(self):
        change_after(self.runtime, 2, 'npc', 'ai', ['00000000', '00000000', '3f800000', '00000000'])
        self.assertEqual('npc-state-mismatch', trace.compare(self.official, self.runtime)['classification'])

    def test_unsafe_dll_identity_refused(self):
        self.runtime['executedDlls']['../escape.dll'] = 'a' * 64
        with self.assertRaises(ValueError): trace.validate(self.runtime, 'runtime')

    def test_bound_read_and_exact_hash(self):
        import hashlib
        raw = json.dumps(self.official).encode()
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'observation.json'; path.write_bytes(raw)
            value, digest = trace.load_with_hash(path, 'official')
            self.assertEqual(self.official, value)
            self.assertEqual(hashlib.sha256(raw).hexdigest(), digest)
            with patch.object(trace, 'MAX_BYTES', len(raw) - 1):
                with self.assertRaises(ValueError): trace.load_with_hash(path, 'official')

    def test_duplicate_json_keys_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'observation.json'
            path.write_text('{"schemaVersion":1,"schemaVersion":1}', encoding='utf-8')
            with self.assertRaises(ValueError): trace.load_with_hash(path, 'official')

    def test_growth_after_size_check_has_bounded_read(self):
        from types import SimpleNamespace
        raw = json.dumps(self.official).encode()
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'observation.json'; path.write_bytes(raw)
            # Simulate a file growing after the initial stat. The consumed read
            # still has a hard ceiling; the old stat cannot admit the payload.
            with patch.object(trace, 'MAX_BYTES', len(raw) - 1), patch.object(Path, 'stat', return_value=SimpleNamespace(st_size=1)):
                with self.assertRaises(ValueError): trace.load_with_hash(path, 'official')


if __name__ == '__main__':
    unittest.main()
