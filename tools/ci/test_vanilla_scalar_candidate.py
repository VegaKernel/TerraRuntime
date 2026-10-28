"""Authored Python ingress tests; real original execution is a separate proof."""
from __future__ import annotations
import copy
from pathlib import Path
import sys
import tempfile
import unittest
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
from vanilla_data import scalar_candidate as scalar


class ScalarRequestTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        folder = Path(temporary.name)
        (folder / 'C.cs').write_text('class C{}', encoding='utf-8')
        assembly = folder / 'Original.exe'; assembly.write_bytes(b'not executed by these tests')
        self.request = {'schemaVersion': 1, 'analysis': {
            'schemaVersion': 1, 'sourceRoot': str(folder), 'assembly': str(assembly), 'sources': ['C.cs'],
            'entries': [{'path': 'C.cs', 'type': 'C', 'method': 'Tick', 'parameterTypes': ['int']}],
            'maxDepth': 0, 'maxMethods': 1},
            'fields': [{'symbol': 'C.timeLeft', 'target': 'Remaining', 'type': 'int'},
                       {'symbol': 'C.flag', 'target': 'Requested', 'type': 'bool'}],
            'cases': [{'state': {'Remaining': 20, 'Requested': False}, 'arguments': [10], 'rngSeed': 1458}]}

    def test_complete_explicit_request_is_copied_and_aliases_normalized(self):
        self.request['analysis']['entries'][0]['parameterTypes'] = ['System.Int32']
        actual = scalar.validate_request(self.request)
        self.assertEqual(['int'], actual['analysis']['entries'][0]['parameterTypes'])
        actual['cases'][0]['state']['Remaining'] = 3
        self.assertEqual(20, self.request['cases'][0]['state']['Remaining'])

    def test_no_unknown_envelope_fields_or_schema_coercion(self):
        for change in [{'extra': True}, {'schemaVersion': True}, {'schemaVersion': 2}]:
            with self.subTest(change=change), self.assertRaises(ValueError):
                scalar.validate_request(self.request | change)

    def test_only_one_explicit_entry_and_zero_depth_one_method(self):
        for key, value in [('maxDepth', 1), ('maxMethods', 2), ('entries', self.request['analysis']['entries'] * 2)]:
            candidate = copy.deepcopy(self.request); candidate['analysis'][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError): scalar.validate_request(candidate)

    def test_exact_target_identities_cannot_alias_or_inject_property_paths(self):
        for change in [{'target': 'Remaining'}, {'target': 'P.Value'}, {'symbol': 'C.timeLeft'}, {'type': 'float'}, {'target': 'x;Run()'},
                       {'target': 'x' * 129}, {'symbol': 'C.' + 'x' * 512}]:
            candidate = copy.deepcopy(self.request); candidate['fields'][1].update(change)
            with self.subTest(change=change), self.assertRaises(ValueError): scalar.validate_request(candidate)

    def test_json_boolean_is_not_integer_and_integer_is_not_boolean(self):
        for name, value in [('Remaining', True), ('Remaining', 1.2), ('Remaining', 2**31), ('Requested', 1), ('Requested', 'false')]:
            candidate = copy.deepcopy(self.request); candidate['cases'][0]['state'][name] = value
            with self.subTest(name=name, value=value), self.assertRaises(ValueError): scalar.validate_request(candidate)

    def test_missing_extra_states_arguments_and_unbounded_seed_refuse(self):
        for change in [{'state': {'Remaining': 20}}, {'state': {'Remaining': 20, 'Requested': False, 'extra': 0}},
                       {'arguments': []}, {'arguments': [True]}, {'rngSeed': 2**31}, {'rngSeed': True}]:
            candidate = copy.deepcopy(self.request); candidate['cases'][0].update(change)
            with self.subTest(change=change), self.assertRaises(ValueError): scalar.validate_request(candidate)

    def test_bounded_lists_and_non_scalar_parameter_refuse(self):
        for key, value in [('fields', []), ('cases', []), ('cases', self.request['cases'] * 4097)]:
            candidate = copy.deepcopy(self.request); candidate[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError): scalar.validate_request(candidate)
        for parameter in ['float', 'ref int', 'object', 'System.String']:
            candidate = copy.deepcopy(self.request); candidate['analysis']['entries'][0]['parameterTypes'] = [parameter]
            with self.subTest(parameter=parameter), self.assertRaises(ValueError): scalar.validate_request(candidate)

    def observation(self):
        state = {'Remaining': 10, 'Requested': True}
        return {'kind': 'source-scalar-candidate-verification', 'accepted': True, 'candidateOnly': True,
                'entry': 'C.Tick(int)', 'cases': 1, 'originalMethodCalls': 1, 'originalCarrierCompiled': True,
                'wholeOriginalTypeCompiled': False, 'methodQualified': True, 'compilationErrorCount': 123,
                'originalRngCursorAndAll56SlotsUnchanged': True,
                'observations': [{'index': 0, 'rngSeed': 1458, 'initial': self.request['cases'][0]['state'].copy(),
                                  'arguments': [10], 'original': state.copy(), 'carrier': state.copy(), 'candidate': state.copy(),
                                  'rngBefore': {'cursor': 0, 'slots': [0] * 56}, 'rngAfter': {'cursor': 0, 'slots': [0] * 56}}],
                'mutants': [{'mutation': 'omit-field-write:' + name, 'cases': 1, 'differences': 1} for name in ['Remaining', 'Requested']]}

    def test_observation_binds_identity_entire_projection_and_visible_global_errors(self):
        data = self.observation()
        self.assertIs(data, scalar.validate_observation(self.request, data))
        self.assertEqual(123, data['compilationErrorCount'])
        for change in [{'entry': 'Other.Tick(int)'}, {'cases': True}, {'wholeOriginalTypeCompiled': True}, {'methodQualified': False}]:
            with self.subTest(change=change), self.assertRaises(ValueError): scalar.validate_observation(self.request, data | change)

    def test_flag_only_mismatch_cannot_hide_behind_matching_time_field(self):
        data = self.observation(); data['observations'][0]['candidate']['Requested'] = False
        with self.assertRaises(ValueError): scalar.validate_observation(self.request, data)
        data = self.observation(); del data['observations'][0]['candidate']['Requested']
        with self.assertRaises(ValueError): scalar.validate_observation(self.request, data)

    def test_full_rng_and_every_mutation_are_required(self):
        for mutate in [lambda d: d['observations'][0]['rngAfter']['slots'].__setitem__(55, 1),
                       lambda d: d['observations'][0]['rngAfter']['slots'].pop(),
                       lambda d: d['mutants'].pop(), lambda d: d['mutants'][1].update(differences=0)]:
            data = self.observation(); mutate(data)
            with self.assertRaises(ValueError): scalar.validate_observation(self.request, data)


if __name__ == '__main__':
    unittest.main()
