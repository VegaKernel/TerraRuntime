"""Independent ingress/evidence rejection controls for read-only value candidates."""
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
from vanilla_data import scalar_candidate as scalar
from vanilla_data import source_analysis as analysis


class ValueCandidateTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.folder = Path(temporary.name)
        (self.folder / 'C.cs').write_text('class C{}', encoding='utf-8')
        assembly = self.folder / 'Reference.exe'; assembly.write_bytes(b'never executed')
        self.request = {'schemaVersion': 2, 'analysis': {'schemaVersion': 1,
            'sourceRoot': str(self.folder), 'assembly': str(assembly), 'sources': ['C.cs'],
            'entries': [{'path': 'C.cs', 'type': 'C', 'method': 'Compute', 'parameterTypes': ['Test.Item', 'float']}],
            'maxDepth': 0, 'maxMethods': 1}, 'returnType': 'float',
            'fields': [{'symbol': 'C.factor', 'target': 'Factor', 'type': 'float', 'receiver': 'this', 'access': 'readOnly'},
                       {'symbol': 'Test.Item.flag', 'target': 'Flag', 'type': 'bool', 'receiver': 'item', 'access': 'readOnly'}],
            'cases': [{'state': {'Factor': {'bits': 1073741824}, 'Flag': True},
                       'arguments': [{'receiver': 'item'}, {'bits': 1077936128}], 'rngSeed': 1458}]}

    def observation(self):
        state = copy.deepcopy(self.request['cases'][0]['state'])
        fingerprint = {'sha256': 'a' * 64, 'fields': 17}
        return {'schemaVersion': 2, 'kind': 'source-value-candidate-verification', 'accepted': True,
                'candidateOnly': True, 'entry': 'C.Compute(Test.Item, float)', 'returnType': 'float',
                'cases': 1, 'originalMethodCalls': 1, 'originalCarrierCompiled': True, 'wholeOriginalTypeCompiled': False,
                'methodQualified': True, 'compilationErrorCount': 12, 'allDirectActorFieldsAndArraysUnchanged': True,
                'originalRngCursorAndAll56SlotsUnchanged': True,
                'observations': [{'index': 0, 'rngSeed': 1458, 'initial': state,
                    'arguments': copy.deepcopy(self.request['cases'][0]['arguments']),
                    'original': {'bits': 1086324736}, 'carrier': {'bits': 1086324736}, 'candidate': {'bits': 1086324736},
                    'originalState': copy.deepcopy(state), 'carrierState': copy.deepcopy(state), 'candidateState': copy.deepcopy(state),
                    'actorBefore': fingerprint.copy(), 'actorAfter': fingerprint.copy(),
                    'receiverBefore': fingerprint.copy(), 'receiverAfter': fingerprint.copy(),
                    'rngBefore': {'cursor': 0, 'slots': [0] * 56}, 'rngAfter': {'cursor': 0, 'slots': [0] * 56}}],
                'mutants': [{'mutation': 'omit-operation-0', 'cases': 1, 'differences': 1, 'errors': 0}]}

    def test_valid_request_is_copied_and_complete_evidence_is_accepted(self):
        result = scalar.validate_request(self.request)
        self.assertEqual(self.request, result)
        result['cases'][0]['state']['Flag'] = False
        self.assertIs(True, self.request['cases'][0]['state']['Flag'])
        self.assertIs(self.observation()['accepted'], True)
        scalar.validate_observation(self.request, self.observation())

    def test_raw_finite_bits_preserve_negative_zero_subnormal_and_extreme(self):
        for bits in [0, 0x80000000, 1, 0x80000001, 0x007fffff, 0x00800000, 0x7f7fffff, 0xff7fffff]:
            request = copy.deepcopy(self.request); request['cases'][0]['state']['Factor'] = {'bits': bits}
            with self.subTest(bits=bits): self.assertEqual(bits, scalar.validate_request(request)['cases'][0]['state']['Factor']['bits'])

    def test_nonfinite_coercible_or_ambiguous_float_inputs_refuse(self):
        for value in [0., 1, True, {'bits': True}, {'bits': -1}, {'bits': 2**32}, {'bits': 0x7f800000},
                      {'bits': 0xff800000}, {'bits': 0x7fc00001}, {'bits': 0, 'value': 0}, {'bits': '0'}]:
            request = copy.deepcopy(self.request); request['cases'][0]['state']['Factor'] = value
            with self.subTest(value=value), self.assertRaises(ValueError): scalar.validate_request(request)

    def test_capabilities_cannot_alias_write_or_guess_a_receiver(self):
        for change in [{'access': 'readWrite'}, {'receiver': 'unknown'}, {'receiver': '__state'}, {'target': 'Factor'},
                       {'target': 'Nested.Value'}, {'target': '__state'}, {'symbol': 'C.factor'}, {'extra': 3}, {'type': 'double'}]:
            request = copy.deepcopy(self.request); request['fields'][1].update(change)
            with self.subTest(change=change), self.assertRaises(ValueError): scalar.validate_request(request)

    def test_null_rebound_or_nonexact_reference_argument_refuses(self):
        for value in [None, 0, 'item', {'receiver': 'other'}, {'receiver': 'item', 'value': None}]:
            request = copy.deepcopy(self.request); request['cases'][0]['arguments'][0] = value
            with self.subTest(value=value), self.assertRaises(ValueError): scalar.validate_request(request)

    def test_no_missing_state_defaults_or_untyped_scalar_arguments(self):
        for change in [{'state': {'Flag': True}}, {'arguments': [{'receiver': 'item'}, 3.]}, {'rngSeed': True}]:
            request = copy.deepcopy(self.request); request['cases'][0].update(change)
            with self.subTest(change=change), self.assertRaises(ValueError): scalar.validate_request(request)

    def test_schema_versions_and_closed_envelopes_are_not_coerced(self):
        for change in [{'schemaVersion': True}, {'schemaVersion': 1}, {'schemaVersion': 3}, {'returnType': 'double'}, {'extra': 1}]:
            with self.subTest(change=change), self.assertRaises(ValueError): scalar.validate_request(self.request | change)

    def test_multiple_reference_aliases_or_ref_parameters_are_outside_contract(self):
        for types in [['Test.Item', 'Test.Item'], ['ref Test.Item', 'float'], ['Test.Item', 'double'], ['float', 'float']]:
            request = copy.deepcopy(self.request); request['analysis']['entries'][0]['parameterTypes'] = types
            with self.subTest(types=types), self.assertRaises(ValueError): scalar.validate_request(request)

    def test_bool_int_results_and_float_bits_cannot_be_confused(self):
        for value in [6., 6, True, {'bits': True}, {'bits': 0x7f800000}]:
            observation = self.observation(); observation['observations'][0]['candidate'] = value
            with self.subTest(value=value), self.assertRaises(ValueError): scalar.validate_observation(self.request, observation)

    def test_same_numeric_zero_with_different_float_sign_fails(self):
        observation = self.observation(); row = observation['observations'][0]
        row['original'] = row['carrier'] = {'bits': 0}; row['candidate'] = {'bits': 0x80000000}
        with self.assertRaises(ValueError): scalar.validate_observation(self.request, observation)

    def test_equal_by_python_coercion_is_not_a_typed_observation_preimage(self):
        observation = self.observation(); observation['observations'][0]['initial']['Flag'] = 1
        with self.assertRaises(ValueError): scalar.validate_observation(self.request, observation)
        request = copy.deepcopy(self.request); request['cases'][0]['arguments'][1] = {'bits': 1}
        observation = self.observation(); observation['observations'][0]['arguments'][1] = {'bits': True}
        with self.assertRaises(ValueError): scalar.validate_observation(request, observation)

    def test_original_actor_or_receiver_mutation_cannot_be_hidden(self):
        for key in ['actorAfter', 'receiverAfter']:
            observation = self.observation(); observation['observations'][0][key]['sha256'] = 'b' * 64
            with self.subTest(key=key), self.assertRaises(ValueError): scalar.validate_observation(self.request, observation)

    def test_mapped_readonly_state_mutation_fails_even_when_results_match(self):
        observation = self.observation(); observation['observations'][0]['originalState']['Flag'] = False
        with self.assertRaises(ValueError): scalar.validate_observation(self.request, observation)

    def test_full_physical_rng_rejects_single_slot_drift_and_missing_slots(self):
        for state in [{'cursor': 0, 'slots': [0] * 55}, {'cursor': 56, 'slots': [0] * 56}, {'cursor': 0, 'slots': [1] + [0] * 55}]:
            observation = self.observation(); observation['observations'][0]['rngAfter'] = state
            with self.subTest(state=state), self.assertRaises(ValueError): scalar.validate_observation(self.request, observation)

    def test_false_control_acceptance_or_loader_errors_are_rejected(self):
        for control in [[], [{'mutation': 'omit-operation-0', 'cases': 1, 'differences': 0, 'errors': 0}],
                        [{'mutation': 'omit-operation-0', 'cases': 1, 'differences': 1, 'errors': 1}],
                        [{'mutation': 'unknown', 'cases': 1, 'differences': 1, 'errors': 0}]]:
            observation = self.observation(); observation['mutants'] = control
            with self.subTest(control=control), self.assertRaises(ValueError): scalar.validate_observation(self.request, observation)

    def test_wrong_entry_incomplete_projection_and_aggregate_claims_refuse(self):
        for change in [{'entry': 'C.Other(Test.Item, float)'}, {'wholeOriginalTypeCompiled': True}, {'originalCarrierCompiled': False},
                       {'allDirectActorFieldsAndArraysUnchanged': False}, {'cases': True}, {'returnType': 'int'}]:
            with self.subTest(change=change), self.assertRaises(ValueError): scalar.validate_observation(self.request, self.observation() | change)

    def test_malformed_request_is_rejected_before_any_subprocess_or_output(self):
        request = self.folder / 'bad.json'; output = self.folder / 'output'
        malformed = copy.deepcopy(self.request); malformed['cases'][0]['state']['Factor'] = {'bits': 0x7fc00000}
        request.write_text(json.dumps(malformed), encoding='utf-8')
        with patch('vanilla_data.scalar_candidate.subprocess.run') as run:
            with self.assertRaises(ValueError): scalar.adapt(request, output)
            run.assert_not_called()
        self.assertFalse(output.exists())

    def test_new_validator_is_in_the_executed_tool_source_guard(self):
        self.assertIn('tools/vanilla_data/scalar_values.py', analysis._tool_inputs())


if __name__ == '__main__':
    unittest.main()
