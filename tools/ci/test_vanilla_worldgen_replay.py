"""Replay guards, without a game DLL or a build/test process in CI."""
from __future__ import annotations

from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
from vanilla_data import worldgen_replay as replay


def result_xml(*, method=replay.RESET_METHOD, result='Pass', total=1, errors=0, extra=''):
    kind, name = method.rsplit('.', 1)
    passed, failed, skipped = int(result == 'Pass'), int(result == 'Fail'), int(result == 'Skip')
    return (f'<assemblies><assembly name="Tests" total="{total}" passed="{passed}" '
            f'failed="{failed}" errors="{errors}" skipped="{skipped}">'
            f'<collection><test type="{kind}" method="{name}" result="{result}" time="0.1" />'
            f'{extra}</collection></assembly></assemblies>')


class WorldgenReplayTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)
        self.dll = self.folder / 'bin/TerraRuntime.Tests.dll'
        self.dll.parent.mkdir()
        self.dll.write_bytes(b'fixture test assembly')
        self.dependency = self.dll.parent / 'Core.dll'
        self.dependency.write_bytes(b'fixture dependency')
        self.output = self.folder / 'replay'

    def process(self, xml=None, *, exit_code=0, change=None):
        def run(command, **kwargs):
            self.assertEqual(replay.RESET_METHOD, command[command.index('-method') + 1])
            self.assertEqual('1', command[command.index('-maxThreads') + 1])
            self.assertEqual(replay.TIMEOUT_SECONDS, kwargs['timeout'])
            kwargs['stdout'].write(b'actual process output')
            if xml is not None:
                Path(command[command.index('-xml') + 1]).write_text(xml, encoding='utf-8')
            if change:
                change()
            return SimpleNamespace(returncode=exit_code)
        return run

    def test_one_exact_pass_and_unchanged_closure_are_accepted(self):
        with patch.object(replay.subprocess, 'run', side_effect=self.process(result_xml())):
            report = replay.replay_reset(self.dll, self.output)
        self.assertTrue(report['accepted'], report)
        self.assertEqual(2, report['dllCount'])
        self.assertTrue(report['dllsUnchanged'])
        self.assertEqual(replay.RESET_METHOD, report['xmlAudit']['actualTests'][0]['method'])
        self.assertTrue((self.output / 'result.json').is_file())
        self.assertEqual(b'actual process output', (self.output / 'stdout.log').read_bytes())

    def test_foreign_method_or_extra_actual_method_is_rejected(self):
        cases = [result_xml(method='Other.Tests.Wrong'),
                 result_xml(total=2, extra='<test type="Other.Tests" method="Wrong" result="Pass" />')]
        for index, xml in enumerate(cases):
            with self.subTest(index=index), patch.object(replay.subprocess, 'run', side_effect=self.process(xml)):
                report = replay.replay_reset(self.dll, self.folder / f'foreign-{index}')
            self.assertFalse(report['accepted'])
            self.assertTrue(report['xmlAudit']['validationErrors'])

    def test_failed_skipped_and_runner_error_results_are_rejected(self):
        for label, xml in [('fail', result_xml(result='Fail')), ('skip', result_xml(result='Skip')),
                           ('error', result_xml(errors=1)),
                           ('hidden-error', result_xml(extra='<error type="Runner" />'))]:
            with self.subTest(label=label), patch.object(replay.subprocess, 'run', side_effect=self.process(xml)):
                report = replay.replay_reset(self.dll, self.folder / label)
            self.assertFalse(report['accepted'])

    def test_changed_added_and_removed_dlls_are_rejected(self):
        changes = [lambda: self.dependency.write_bytes(b'changed'),
                   lambda: (self.dll.parent / 'Added.dll').write_bytes(b'new'),
                   lambda: self.dependency.unlink()]
        for index, change in enumerate(changes):
            with self.subTest(index=index), patch.object(replay.subprocess, 'run',
                side_effect=self.process(result_xml(), change=change)):
                report = replay.replay_reset(self.dll, self.folder / f'changed-{index}')
            self.assertFalse(report['accepted'])
            self.assertFalse(report['dllsUnchanged'])
            self.assertTrue(report['changedDlls'])

    def test_exit_code_missing_and_malformed_xml_are_rejected_with_retained_results(self):
        cases = [(result_xml(), 1), (None, 0), ('<broken', 0)]
        for index, (xml, code) in enumerate(cases):
            output = self.folder / f'invalid-{index}'
            with self.subTest(index=index), patch.object(replay.subprocess, 'run',
                side_effect=self.process(xml, exit_code=code)):
                report = replay.replay_reset(self.dll, output)
            self.assertFalse(report['accepted'])
            self.assertTrue((output / 'result.json').is_file())

    def test_timeout_is_a_rejected_result_not_a_restarted_run(self):
        with patch.object(replay.subprocess, 'run', side_effect=subprocess.TimeoutExpired('dotnet', 60)) as run:
            report = replay.replay_reset(self.dll, self.output)
        self.assertFalse(report['accepted'])
        run.assert_called_once()

    def test_existing_output_or_missing_input_never_launches_a_process(self):
        self.output.mkdir()
        with patch.object(replay.subprocess, 'run') as run:
            with self.assertRaises(FileExistsError): replay.replay_reset(self.dll, self.output)
            with self.assertRaises(FileNotFoundError): replay.replay_reset(self.folder / 'missing.dll', self.folder / 'new')
            run.assert_not_called()

    def test_oversized_xml_and_inconsistent_summary_are_rejected(self):
        xml = self.folder / 'oversized.xml'
        xml.write_text(result_xml(), encoding='utf-8')
        with patch.object(replay, 'MAX_XML_BYTES', 16):
            self.assertFalse(replay.audit_xml(xml)['accepted'])
        xml.write_text(result_xml(total=2), encoding='utf-8')
        self.assertFalse(replay.audit_xml(xml)['accepted'])


if __name__ == '__main__':
    unittest.main()
