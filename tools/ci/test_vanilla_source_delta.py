"""Exercise source-only migration reports without using real Terraria inputs."""
from contextlib import redirect_stderr, redirect_stdout
from hashlib import sha256
from io import StringIO
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools'))
from vanilla_data import source_delta
from vanilla_data.cli import main


SOURCE = 'Example/Rules.cs'
BEFORE = '''class Rules
{
    public static string Label()
    {
        return "}";
    }
    private static int Tick()
    {
        return 1;
    }
}
'''


class SourceDeltaTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(self.enterContext(tempfile.TemporaryDirectory()))
        self.inputs = []
        pins = {}
        for version, source, newline in [('1.4.5.8', BEFORE, '\r\n'),
                                         ('1.4.6.1', '\n' + BEFORE.replace('return 1;', 'return 2;'), '\n')]:
            tree = self.root / version
            (tree / 'Properties').mkdir(parents=True)
            (tree / 'Properties/AssemblyInfo.cs').write_text(f'[assembly: AssemblyVersion("{version}")]\n')
            assembly = tree / 'TerrariaServer.exe'
            assembly.write_bytes(version.encode())
            pins[version] = sha256(assembly.read_bytes()).hexdigest()
            (tree / SOURCE).parent.mkdir()
            (tree / SOURCE).write_text(source, encoding='utf-8', newline=newline)
            self.inputs.extend([assembly, tree])
        self.enterContext(patch.object(source_delta, 'ASSEMBLIES', pins))

    def test_reports_changed_spans_and_ignores_line_endings_and_offset(self):
        report = source_delta.compare(*self.inputs, SOURCE, ['Label', 'Tick'])
        label, tick = report['methods']
        self.assertEqual('unchanged-text', label['status'])
        self.assertEqual(4, label['after']['startLine'])
        self.assertEqual('changed-text', tick['status'])
        self.assertEqual([{'kind': 'replace', 'before': {'startLine': 9, 'lineCount': 1},
                           'after': {'startLine': 10, 'lineCount': 1}}], tick['changes'])
        self.assertNotIn('return 2', json.dumps(report))

    def test_rejects_swapped_assembly_or_source_version(self):
        before_assembly, before_root, after_assembly, after_root = self.inputs
        with self.assertRaisesRegex(ValueError, 'Assembly SHA-256'):
            source_delta.compare(after_assembly, before_root, after_assembly, after_root, SOURCE, ['Tick'])
        with self.assertRaisesRegex(ValueError, 'AssemblyInfo'):
            source_delta.compare(before_assembly, before_root, after_assembly, before_root, SOURCE, ['Tick'])

    def test_refuses_ambiguous_missing_and_escaping_inputs(self):
        with self.assertRaisesRegex(ValueError, 'canonical relative'):
            source_delta.compare(*self.inputs, '../Rules.cs', ['Tick'])
        with self.assertRaisesRegex(ValueError, 'found 0'):
            source_delta.compare(*self.inputs, SOURCE, ['Missing'])
        (self.inputs[-1] / SOURCE).write_text(BEFORE + '\npublic static int Tick(int value) { return value; }')
        with self.assertRaisesRegex(ValueError, 'found 2'):
            source_delta.compare(*self.inputs, SOURCE, ['Tick'])

    def test_cli_does_not_overwrite_a_report(self):
        report_path = self.root / 'receipt.json'
        args = ['source', 'delta', '--path', SOURCE, '--method', 'Tick', '--report', str(report_path)]
        for flag, path in zip(['--before-assembly', '--before-root', '--after-assembly', '--after-root'], self.inputs):
            args.extend([flag, str(path)])
        with redirect_stdout(StringIO()), redirect_stderr(StringIO()):
            self.assertEqual(0, main(args))
            saved = report_path.read_bytes()
            self.assertEqual(2, main(args))
        self.assertEqual(saved, report_path.read_bytes())
