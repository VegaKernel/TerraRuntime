"""Initial vanilla imports in real-host workflows opt in; restarts exercise existing custody."""
from pathlib import Path
import re
import shlex
import unittest

ROOT = Path(__file__).resolve().parents[2]
INITIAL_PORTS = {
    'vanilla-world-load.yml': '17780',
    'live-chest-persistence.yml': '17787',
    'live-sign-persistence.yml': '17791',
    'world-checkpoint-recovery.yml': '17801',
}


def host_commands(text):
    logical = re.sub(r'\\\r?\n\s*', ' ', text)
    return [shlex.split(line.strip()) for line in logical.splitlines()
            if line.strip().startswith('dotnet "$server_dll"') and '--world' in line]


class WorkflowWorldCustodyTests(unittest.TestCase):
    def test_initial_official_world_starts_explicitly_import_and_restarts_do_not(self):
        for name, initial_port in INITIAL_PORTS.items():
            with self.subTest(workflow=name):
                commands = host_commands((ROOT / '.github/workflows' / name).read_text())
                self.assertGreaterEqual(len(commands), 2, 'Exercise both cold admission and restart/refusal')
                initial = [command for command in commands if command[command.index('--port') + 1] == initial_port]
                self.assertEqual(1, len(initial))
                self.assertEqual(1, initial[0].count('--import-world-custody'))
                for command in commands:
                    if command is not initial[0]:
                        self.assertNotIn('--import-world-custody', command,
                                         'Restart and corrupt/unsupported recovery must exercise existing authority')

    def test_startup_gate_forwards_only_cold_import_arguments(self):
        text = (ROOT / '.github/workflows/startup-performance-gate.yml').read_text()
        commands = host_commands(text)
        self.assertEqual(1, len(commands))
        self.assertIn('$@', commands[0])
        self.assertNotIn('--import-world-custody', commands[0], 'Shared host function must not force warm import')
        function = text[text.index('          run_host() {'):text.index('          rm -f "$runtime_cache"')]
        self.assertRegex(function, r'local port="\$2"\s+shift 2')
        calls = [shlex.split(line.strip()) for line in text.splitlines()
                 if re.match(r'\s*run_host (cold|warm) ', line)]
        self.assertEqual([['run_host', 'cold', '17831', '--import-world-custody'],
                          ['run_host', 'warm', '17832']], calls)
        self.assertIn('test -s "$world.custody/managed-v1.anchor"', text)

    def test_interrupted_recovery_preserves_matching_authority_without_reimport(self):
        text = (ROOT / '.github/workflows/interrupted-world-save-recovery.yml').read_text()
        logical = re.sub(r'\\\r?\n\s*', ' ', text)
        commands = [shlex.split(line.strip()) for line in logical.splitlines()
                    if line.strip().startswith('dotnet "$host"') and '--world' in line]
        self.assertEqual(6, len(commands))
        for command in commands:
            initial = command[command.index('--port') + 1] == '17826'
            self.assertEqual(initial, '--import-world-custody' in command)
        # All three marker fixtures clone the same exact imported generation before the writer starts.
        self.assertEqual(3, text.count('cp -a "$source.custody" "$world.custody"'))
        self.assertEqual(2, text.count('cp -a "$source.custody" "$world.custody"\n'
                                      '          dotnet "$probe" stall-recovery-ready-file'))
        self.assertEqual(1, text.count('cp -a "$source.custody" "$world.custody"\n'
                                      '          dotnet "$probe" stall-after-publish-file'))
        self.assertIn('cmp "$original" "$world"', text)


if __name__ == '__main__':
    unittest.main()
