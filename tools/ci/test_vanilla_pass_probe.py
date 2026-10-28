"""Fresh-run orchestration guards; no game binary/compiler execution in CI."""
from __future__ import annotations

import copy
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
from vanilla_data import pass_probe as probe
from test_vanilla_pass_trace import observation, cleanup_observation


class PassProbeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)
        self.assembly = self.folder / 'TerrariaServer.exe'
        self.assembly.write_bytes(b'fixture, never executed')
        self.pins = {'PassProbe.dll': 'a' * 64, 'TerraRuntime.WorldGeneration.dll': 'b' * 64}
        self.reference = dict(assemblySha256='c' * 64, dependenciesSha256={})

    def process(self, wrong=None):
        def run(args, **kwargs):
            if args[1] == 'build': return SimpleNamespace(returncode=0)
            engine, output = args[2], Path(args[5])
            profile = args[8]
            pass_id, scenario, registered, runtime_type = probe.PROFILES[profile]
            data = cleanup_observation(engine) if profile == 'quick-cleanup' else observation(engine)
            data['pass'] = pass_id
            data['scenario'] = scenario
            data['executedDlls'] = dict(self.pins)
            data['provenance'] = dict(control='normal', probeSha256=self.pins['PassProbe.dll'])
            if engine == 'official':
                data['provenance'].update(entry='registered-GenPass.Apply', registeredName=registered,
                                          officialAssemblySha256=self.reference['assemblySha256'], dependenciesSha256={})
            else:
                data['provenance'].update(entry='TerraRuntime.WorldGeneration.Vanilla.' + runtime_type + '.Apply',
                                          runtimeAssemblySha256=self.pins['TerraRuntime.WorldGeneration.dll'])
            if wrong == 'pass': data['pass'] = 'WrongPass'
            if wrong == 'seed': data['context']['seed'] = 42
            if wrong == 'closure': data['executedDlls'] = {'Foreign.dll': '0' * 64}
            if wrong == 'control': data['provenance']['control'] = 'omit-pass'
            if wrong == 'entry': data['provenance']['entry'] = 'Wrong.Apply'
            if wrong == 'registered': data['provenance']['registeredName'] = 'Wrong Pass'
            output.write_text(json.dumps(data), encoding='utf-8')
            return SimpleNamespace(returncode=0)
        return run

    def invoke(self, wrong=None, drift=False, profile='grass-wall'):
        sources = [{'src/a.cs': '1' * 64}, {'src/a.cs': ('2' if drift else '1') * 64}]
        with patch.object(probe.facts, 'reference', return_value=self.reference), \
             patch.object(probe.facts, 'digest', return_value='c' * 64), \
             patch.object(probe, 'source_inputs', side_effect=sources), \
             patch.object(probe, 'dll_pins', return_value=self.pins), \
             patch.object(probe.subprocess, 'run', side_effect=self.process(wrong)):
            return probe.run_pass(self.assembly, self.folder / 'result', profile=profile)

    def test_requested_fresh_observations_are_compared(self):
        report = self.invoke()
        self.assertTrue(report['accepted'], report)
        self.assertEqual(3, len(report['commands']))
        self.assertTrue((self.folder / 'result/source-before.json').is_file())

    def test_equal_wrong_pass_seed_control_or_dll_closure_never_pass(self):
        for wrong in ('pass', 'seed', 'closure', 'control'):
            with self.subTest(wrong=wrong):
                self.folder = Path(self.temporary.name) / wrong
                self.folder.mkdir()
                report = self.invoke(wrong)
                self.assertFalse(report['accepted'])
                self.assertEqual('execution-or-input-failure', report['classification'])

    def test_source_drift_rejects_even_equal_observed_outputs(self):
        report = self.invoke(drift=True)
        self.assertFalse(report['accepted'])
        self.assertTrue(report['comparison']['accepted'])
        self.assertFalse(report['sourceInputsUnchanged'])

    def test_cleanup_profile_selects_requested_entry_and_context(self):
        report = self.invoke(profile='quick-cleanup')
        self.assertTrue(report['accepted'], report)
        self.assertEqual('QuickCleanup', report['pass'])
        self.assertEqual('quick-cleanup', report['profile'])

    def test_cleanup_wrong_registered_or_runtime_entry_is_rejected(self):
        for wrong in ('entry', 'registered'):
            with self.subTest(wrong=wrong):
                self.folder = Path(self.temporary.name) / wrong
                self.folder.mkdir()
                self.assertFalse(self.invoke(wrong, profile='quick-cleanup')['accepted'])

    def test_unknown_profile_does_not_start_a_compiler(self):
        with patch.object(probe.subprocess, 'run') as run:
            with self.assertRaisesRegex(ValueError, 'profile'):
                probe.run_pass(self.assembly, self.folder / 'bad', profile='unrepresented')
            run.assert_not_called()

    def test_refused_binary_does_not_start_a_compiler(self):
        with patch.object(probe.facts, 'reference', return_value=self.reference), \
             patch.object(probe.facts, 'digest', return_value='0' * 64), \
             patch.object(probe.subprocess, 'run') as run:
            with self.assertRaisesRegex(ValueError, 'admitted reference'):
                probe.run_pass(self.assembly, self.folder / 'bad')
            run.assert_not_called()


if __name__ == '__main__': unittest.main()
