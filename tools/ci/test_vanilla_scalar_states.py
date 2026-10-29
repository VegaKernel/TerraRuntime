"""Schema3 ingress/evidence tests; no fabricated original execution evidence."""
from __future__ import annotations
import copy
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
from vanilla_data import scalar_candidate as scalar


class StateCandidateTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(); self.addCleanup(temp.cleanup)
        folder = Path(temp.name)
        (folder / 'C.cs').write_text('class C{}', encoding='utf8')
        (folder / 'Reference.exe').write_bytes(b'not executed')
        self.request = {'schemaVersion': 3, 'analysis': {'schemaVersion': 1, 'sourceRoot': str(folder),
            'assembly': str(folder / 'Reference.exe'), 'sources': ['C.cs'],
            'entries': [{'path': 'C.cs', 'type': 'C', 'method': 'Step', 'parameterTypes': ['Microsoft.Xna.Framework.Vector2', 'float']}],
            'maxDepth': 0, 'maxMethods': 1}, 'returnType': 'void', 'fields': [
            {'path': ['B.velocity', 'Microsoft.Xna.Framework.Vector2.X'], 'index': None, 'target': 'Speed', 'type': 'float', 'access': 'readWrite'},
            {'path': ['C.ai'], 'index': 3, 'target': 'Clock', 'type': 'float', 'access': 'readOnly'}],
            'cases': [{'state': {'Speed': {'bits': 0x80000000}, 'Clock': {'bits': 1}},
                'arguments': [{'x': {'bits': 0}, 'y': {'bits': 1}}, {'bits': 0x3dcccccd}], 'rngSeed': 1458}]}

    def observation(self):
        case = copy.deepcopy(self.request['cases'][0]); state = copy.deepcopy(case['state']); state['Speed'] = {'bits': 0x3dcccccd}
        fingerprint = {'sha256': 'a' * 64, 'fields': 12}
        return {'schemaVersion': 3, 'kind': 'source-state-candidate-verification', 'accepted': True, 'candidateOnly': True,
            'entry': 'C.Step(Microsoft.Xna.Framework.Vector2, float)', 'returnType': 'void', 'cases': 1, 'originalMethodCalls': 1,
            'originalCarrierCompiled': True, 'wholeOriginalTypeCompiled': False, 'methodQualified': True, 'compilationErrorCount': 162,
            'originalRngCursorAndAll56SlotsUnchanged': True, 'allUnmappedDirectActorFieldsAndArraysUnchanged': True,
            'directArrayAliasesRejected': True, 'observations': [{'index': 0, 'rngSeed': 1458, 'initial': case['state'], 'arguments': case['arguments'],
                'original': None, 'carrier': None, 'candidate': None, 'originalState': copy.deepcopy(state), 'carrierState': copy.deepcopy(state),
                'candidateState': copy.deepcopy(state), 'actorBefore': fingerprint.copy(), 'actorAfter': fingerprint.copy(),
                'rngBefore': {'cursor': 0, 'slots': [0] * 56}, 'rngAfter': {'cursor': 0, 'slots': [0] * 56}}],
            'mutants': [{'mutation': 'omit-operation-0', 'cases': 1, 'differences': 1, 'errors': 0}]}

    def reject_request(self, edit):
        request = copy.deepcopy(self.request); edit(request)
        with self.assertRaises(ValueError): scalar.validate_request(request)

    def reject_observation(self, edit):
        result = self.observation(); edit(result)
        with self.assertRaises((ValueError, KeyError)): scalar.validate_observation(self.request, result)

    def test_exact_request_is_copied_and_complete_evidence_accepted(self):
        result = scalar.validate_request(self.request); self.assertEqual(result, self.request)
        result['cases'][0]['state']['Clock']['bits'] = 9
        self.assertEqual(self.request['cases'][0]['state']['Clock']['bits'], 1)
        scalar.validate_observation(self.request, self.observation())

    def test_schema_and_unknown_envelope_are_not_coerced(self):
        for field, value in [('schemaVersion', True), ('schemaVersion', 4), ('returnType', 'double'), ('extra', 1)]:
            with self.subTest(field=field, value=value): self.reject_request(lambda r: r.__setitem__(field, value))

    def test_one_zero_depth_entry_is_required(self):
        for field, value in [('maxDepth', 1), ('maxMethods', 2), ('entries', [])]:
            with self.subTest(field=field): self.reject_request(lambda r: r['analysis'].__setitem__(field, value))

    def test_reference_and_ref_vector_parameters_rejected(self):
        for kind in ['ref Microsoft.Xna.Framework.Vector2', 'in Microsoft.Xna.Framework.Vector2', 'Test.Item', 'float[]', 'double']:
            with self.subTest(kind=kind): self.reject_request(lambda r: r['analysis']['entries'][0]['parameterTypes'].__setitem__(0, kind))

    def test_path_arity_leaf_and_index_shape_are_strict(self):
        for path, index, kind in [([], None, 'float'), (['C.ai', 'Other.X'], None, 'float'),
                (['C.ai'], True, 'float'), (['C.ai'], -1, 'float'), (['C.ai'], 4, 'float'),
                (['C.other'], 0, 'float'), (['C.ai'], 0, 'int'),
                (['B.velocity', 'Microsoft.Xna.Framework.Vector2.X'], 0, 'float'),
                (['B.velocity', 'Microsoft.Xna.Framework.Vector2.X'], None, 'int')]:
            with self.subTest(path=path, index=index, kind=kind):
                self.reject_request(lambda r: r['fields'][0].update(path=path, index=index, type=kind))

    def test_duplicate_targets_and_physical_paths_are_rejected(self):
        self.reject_request(lambda r: r['fields'][1].update(target='Speed'))
        self.reject_request(lambda r: r['fields'][1].update(path=r['fields'][0]['path'], index=None))
        for target in ['Nested.Value', '__state', 'x y', 'a' * 129]:
            with self.subTest(target=target): self.reject_request(lambda r: r['fields'][0].update(target=target))

    def test_finite_raw_bits_accept_signed_zero_and_subnormals(self):
        for bits in [0, 0x80000000, 1, 0x80000001, 0x007fffff, 0x00800000, 0x7f7fffff, 0xff7fffff]:
            request = copy.deepcopy(self.request); request['cases'][0]['state']['Speed']['bits'] = bits
            with self.subTest(bits=bits): scalar.validate_request(request)

    def test_nonfinite_or_noninteger_bits_rejected(self):
        for bits in [True, False, -1, 2**32, 0x7f800000, 0xff800000, 0x7fc00001, 1.5]:
            with self.subTest(bits=bits): self.reject_request(lambda r: r['cases'][0]['state']['Speed'].__setitem__('bits', bits))

    def test_vector_preimage_requires_exact_finite_xy(self):
        for vector in [None, {'x': {'bits': 0}}, {'x': {'bits': 0}, 'y': {'bits': True}},
                {'x': {'bits': 0}, 'y': {'bits': 0x7f800000}}, {'x': {'bits': 0}, 'y': {'bits': 0}, 'z': {'bits': 0}}]:
            with self.subTest(vector=vector): self.reject_request(lambda r: r['cases'][0]['arguments'].__setitem__(0, vector))

    def test_missing_or_extra_state_and_case_values_rejected(self):
        self.reject_request(lambda r: r['cases'][0]['state'].pop('Clock'))
        self.reject_request(lambda r: r['cases'][0]['state'].update(Extra={'bits': 0}))
        self.reject_request(lambda r: r['cases'][0].update(rngSeed=True))
        self.reject_request(lambda r: r['cases'][0].update(arguments=[]))

    def test_scope_flags_and_counts_are_not_coerced(self):
        for flag in ['accepted', 'candidateOnly', 'originalCarrierCompiled', 'methodQualified', 'originalRngCursorAndAll56SlotsUnchanged',
                'allUnmappedDirectActorFieldsAndArraysUnchanged', 'directArrayAliasesRejected']:
            with self.subTest(flag=flag): self.reject_observation(lambda o: o.__setitem__(flag, 1))
        for field in ['cases', 'originalMethodCalls', 'compilationErrorCount']:
            with self.subTest(field=field): self.reject_observation(lambda o: o.__setitem__(field, True))
        self.reject_observation(lambda o: o.update(wholeOriginalTypeCompiled=True))

    def test_complete_all_fields_are_compared(self):
        self.reject_observation(lambda o: o['observations'][0]['candidateState']['Speed'].update(bits=1))
        self.reject_observation(lambda o: o['observations'][0]['candidateState'].pop('Clock'))
        self.reject_observation(lambda o: o['observations'][0]['initial']['Speed'].update(bits=False))
        self.reject_observation(lambda o: o['observations'][0]['arguments'][0]['x'].update(bits=False))

    def test_readonly_capability_cannot_change_even_in_three_agreeing_outputs(self):
        def edit(result):
            for key in ['originalState', 'carrierState', 'candidateState']: result['observations'][0][key]['Clock']['bits'] = 2
        self.reject_observation(edit)

    def test_missing_unmapped_actor_fingerprint_and_physical_rng_are_rejected(self):
        self.reject_observation(lambda o: o['observations'][0].pop('actorBefore'))
        self.reject_observation(lambda o: o['observations'][0]['actorAfter'].update(sha256='b' * 64))
        self.reject_observation(lambda o: o['observations'][0]['rngAfter']['slots'].__setitem__(5, 1))
        self.reject_observation(lambda o: o['observations'][0]['rngAfter'].update(slots=[0] * 55))
        self.reject_observation(lambda o: o['observations'][0]['rngAfter'].update(cursor=True))

    def test_no_loader_error_or_zero_difference_counts_as_mutation_evidence(self):
        self.reject_observation(lambda o: o.update(mutants=[]))
        for field, value in [('errors', 1), ('differences', 0), ('differences', True), ('cases', True), ('mutation', '../bad')]:
            with self.subTest(field=field): self.reject_observation(lambda o: o['mutants'][0].__setitem__(field, value))
        self.reject_observation(lambda o: o['mutants'].append(o['mutants'][0].copy()))

    def test_scalar_returns_keep_exact_types_and_bits(self):
        for kind, value, invalid in [('float', {'bits': 0x80000000}, {'bits': True}), ('int', 1, True)]:
            request = copy.deepcopy(self.request); request['returnType'] = kind
            result = self.observation(); result['returnType'] = kind
            row = result['observations'][0]
            for key in ('original', 'carrier', 'candidate'): row[key] = copy.deepcopy(value)
            result['mutants'][0]['mutation'] = 'perturb-result'
            with self.subTest(kind=kind):
                scalar.validate_observation(request, result)
                row['candidate'] = invalid
                with self.assertRaises(ValueError): scalar.validate_observation(request, result)


if __name__ == '__main__': unittest.main()
