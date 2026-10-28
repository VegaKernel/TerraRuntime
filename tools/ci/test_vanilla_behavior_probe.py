"""Artifact/input rejection around the shared NPC/gameplay process boundary."""
from __future__ import annotations

from contextlib import ExitStack
from contextlib import redirect_stdout
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools'))
from vanilla_data import behavior_probe as probe
from vanilla_data import cli


class BehaviorProbeTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.folder = Path(temporary.name)
        self.assembly = self.folder / 'TerrariaServer.exe'
        self.assembly.write_bytes(b'never executed')
        self.pins = {'NpcProbe.dll': 'a' * 64, 'TerraRuntime.Core.dll': 'b' * 64}
        self.reference = {'assemblySha256': 'c' * 64, 'dependenciesSha256': {'FNA.dll': 'c' * 64}}
        self.trace = SimpleNamespace(SCENARIO='unit-profile', SCOPE='invented observation fixture',
                                     OFFICIAL_ENTRY='official-call', RUNTIME_ENTRY='runtime-call',
                                     RUNTIME_DLL='TerraRuntime.Core.dll',
                                     load_with_hash=self.load,
                                     compare=lambda a, b: {'accepted': True, 'classification': 'equal-projection'})

    def load(self, path, engine):
        data = path.read_bytes()
        return json.loads(data), hashlib.sha256(data).hexdigest()

    def process(self, wrong=None, fail=None):
        def run(args, **kwargs):
            if args[1] == 'build':
                return SimpleNamespace(returncode=0)
            engine, output = args[2], Path(args[5])
            if fail == engine:
                return SimpleNamespace(returncode=1)
            if fail == 'timeout':
                raise subprocess.TimeoutExpired(args, 30)
            data = dict(scenario=self.trace.SCENARIO, seed=1458, executedDlls=dict(self.pins),
                        provenance=dict(control='normal', probeSha256=self.pins['NpcProbe.dll'],
                                        entry=self.trace.OFFICIAL_ENTRY if engine == 'official' else self.trace.RUNTIME_ENTRY,
                                        officialAssemblySha256=self.reference['assemblySha256'],
                                        dependenciesSha256=self.reference['dependenciesSha256'],
                                        runtimeAssemblySha256=self.pins['TerraRuntime.Core.dll']))
            if wrong == 'scenario': data['scenario'] = 'foreign-profile'
            if wrong == 'seed': data['seed'] = 42
            if wrong == 'closure': data['executedDlls'] = {'foreign.dll': '0' * 64}
            for key in ('entry', 'control', 'probeSha256', 'officialAssemblySha256', 'dependenciesSha256', 'runtimeAssemblySha256'):
                if wrong == key: data['provenance'][key] = 'foreign'
            output.write_bytes(json.dumps(data).encode())
            return SimpleNamespace(returncode=0)
        return run

    def invoke(self, *, wrong=None, fail=None, source_drift=False, dll_drift=False, digest=None):
        with ExitStack() as stack:
            stack.enter_context(patch.object(probe, 'adapter', return_value=self.trace))
            stack.enter_context(patch.object(probe.facts, 'reference', return_value=self.reference))
            stack.enter_context(patch.object(probe.facts, 'digest', side_effect=digest, return_value='c' * 64))
            stack.enter_context(patch.object(probe, 'source_inputs', side_effect=[{'src/a.cs': '1'}, {'src/a.cs': '2' if source_drift else '1'}]))
            after = {**self.pins, 'NpcProbe.dll': '0' * 64} if dll_drift else self.pins
            stack.enter_context(patch.object(probe, 'dll_pins', side_effect=[self.pins, after]))
            stack.enter_context(patch.object(probe.subprocess, 'run', side_effect=self.process(wrong, fail)))
            return probe.run_behavior('npc', self.assembly, self.folder / 'result')

    def test_observation_hashes_bind_the_consumed_files(self):
        report = self.invoke()
        self.assertTrue(report['accepted'], report)
        for engine in ('official', 'runtime'):
            data = (self.folder / 'result' / (engine + '.json')).read_bytes()
            self.assertEqual(hashlib.sha256(data).hexdigest(), report['comparison'][engine + 'ObservationSha256'])

    def test_equal_outputs_cannot_admit_wrong_execution_identity(self):
        for wrong in ('scenario', 'seed', 'closure', 'entry', 'control', 'probeSha256',
                      'officialAssemblySha256', 'dependenciesSha256', 'runtimeAssemblySha256'):
            with self.subTest(wrong=wrong), tempfile.TemporaryDirectory() as temp:
                self.folder = Path(temp)
                report = self.invoke(wrong=wrong)
                self.assertFalse(report['accepted'])
                self.assertEqual('execution-or-input-failure', report['classification'])

    def test_equal_outputs_cannot_hide_source_or_dll_drift(self):
        for kind in ('source_drift', 'dll_drift'):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as temp:
                self.folder = Path(temp)
                report = self.invoke(**{kind: True})
                self.assertFalse(report['accepted'])
                self.assertTrue(report['comparison']['accepted'])

    def test_failed_capture_or_timeout_retains_rejected_result(self):
        for failure in ('official', 'runtime', 'timeout'):
            with self.subTest(failure=failure), tempfile.TemporaryDirectory() as temp:
                self.folder = Path(temp)
                report = self.invoke(fail=failure)
                self.assertFalse(report['accepted'])
                self.assertIsNone(report['comparison'])
                self.assertTrue((self.folder / 'result/result.json').is_file())

    def test_changed_official_input_rejects_equal_captures(self):
        calls = []
        def digest(path):
            calls.append(path)
            return ('c' if len(calls) <= 2 else '0') * 64
        report = self.invoke(digest=digest)
        self.assertFalse(report['accepted'])
        self.assertTrue(report['comparison']['accepted'])

    def test_unadmitted_official_dependency_never_starts_a_compiler(self):
        with patch.object(probe, 'adapter', return_value=self.trace), \
             patch.object(probe.facts, 'reference', return_value=self.reference), \
             patch.object(probe.facts, 'digest', side_effect=['c' * 64, '0' * 64]), \
             patch.object(probe.subprocess, 'run') as run:
            with self.assertRaisesRegex(ValueError, 'FNA.dll'):
                probe.run_behavior('npc', self.assembly, self.folder / 'result')
            run.assert_not_called()

    def test_cli_runs_each_behavior_with_its_requested_control(self):
        for domain in ('npc', 'gameplay'):
            with self.subTest(domain=domain), redirect_stdout(io.StringIO()), \
                 patch.object(probe, 'run_behavior', return_value={'accepted': True}) as run:
                code = cli.main([domain, 'run', '--assembly', str(self.assembly),
                                 '--output-dir', str(self.folder / 'out'), '--seed', '-7', '--control', 'extra-rng'])
                self.assertEqual(0, code)
                run.assert_called_once_with(domain, self.assembly, self.folder / 'out', -7, 'extra-rng')

    def test_cli_compares_supplied_observations_without_running_a_backend(self):
        official = self.folder / 'official.json'
        runtime = self.folder / 'runtime.json'
        report_path = self.folder / 'comparison.json'
        trace = SimpleNamespace(load_with_hash=lambda p, e: ({'engine': e}, e + '-consumed-hash'),
                                compare=lambda a, b: {'accepted': False, 'classification': 'behavior-difference'})
        with redirect_stdout(io.StringIO()), patch.object(probe, 'adapter', return_value=trace), \
             patch.object(probe, 'run_behavior') as run:
            code = cli.main(['gameplay', 'compare', '--official', str(official), '--runtime', str(runtime),
                             '--report', str(report_path)])
        self.assertEqual(1, code)
        run.assert_not_called()
        report = json.loads(report_path.read_bytes())
        self.assertEqual('official-consumed-hash', report['officialObservationSha256'])
        self.assertEqual('runtime-consumed-hash', report['runtimeObservationSha256'])

    def test_cli_pass_profile_reaches_the_fresh_runner(self):
        from vanilla_data import pass_probe
        for profile in ('grass-wall', 'quick-cleanup'):
            with self.subTest(profile=profile), redirect_stdout(io.StringIO()), \
                 patch.object(pass_probe, 'run_pass', return_value={'accepted': True}) as run:
                code = cli.main(['worldgen', 'run-pass', '--assembly', str(self.assembly),
                                 '--output-dir', str(self.folder / 'out'), '--profile', profile])
                self.assertEqual(0, code)
                run.assert_called_once_with(self.assembly, self.folder / 'out', 1458, 'normal', profile=profile)


if __name__ == '__main__': unittest.main()
