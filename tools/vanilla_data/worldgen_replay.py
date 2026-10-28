"""Bounded replay of one existing official Reset checkpoint in a supplied test artifact.

This invokes no compiler, original game process or world generation. The artifact
and its DLL closure identify what ran; they do not establish its source revision.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import subprocess
import time
import xml.etree.ElementTree as ET

RESET_METHOD = (
    'TerraRuntime.Tests.VanillaReferenceDifferentialCheckpoint1458Tests.'
    'Official_reference_seed_8675309_keeps_pinned_reset_dungeon_side_and_anchor'
)
TIMEOUT_SECONDS = 60
MAX_XML_BYTES = 16 * 1024 * 1024
SCOPE = ('Compiled artifact replay of the existing official Reset checkpoint. '
         'No fresh official call, compiler, current-tree acceptance, '
         'world generation, NativeAOT or all-pass parity claim.')


def _digest(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def _dll_pins(directory: Path) -> dict[str, str]:
    return {path.relative_to(directory).as_posix(): _digest(path)
            for path in sorted(directory.rglob('*.dll'))}


def audit_xml(path: Path) -> dict:
    """Audit actual results rather than trusting an exit code or method filter."""
    errors = []
    assemblies = []
    tests = []
    observed_errors = 0
    try:
        if path.stat().st_size > MAX_XML_BYTES:
            raise ValueError('One-checkpoint XML exceeds the admitted size limit')
        for _, element in ET.iterparse(path, events=('end',)):
            if element.tag == 'test':
                tests.append({'method': element.get('type', '') + '.' + element.get('method', ''),
                              'result': element.get('result'), 'time': element.get('time')})
                if len(tests) > 1:
                    raise ValueError('Expected exactly one actual test; filter selected extra tests')
            elif element.tag == 'assembly':
                assemblies.append(dict(element.attrib))
            elif element.tag == 'error':
                observed_errors += 1
            element.clear()
        if len(assemblies) != 1:
            errors.append('Expected exactly one actual assembly result')
        else:
            for name, expected in {'total': 1, 'passed': 1, 'failed': 0, 'errors': 0, 'skipped': 0}.items():
                try:
                    actual = int(assemblies[0][name])
                except (KeyError, TypeError, ValueError):
                    errors.append('Missing/invalid assembly count: ' + name)
                    continue
                if actual != expected:
                    errors.append(f'Assembly {name}={actual}, expected {expected}')
        if len(tests) != 1 or tests[0]['method'] != RESET_METHOD or tests[0]['result'] != 'Pass':
            errors.append('Actual result must be the one allowlisted Reset method and Pass')
        if observed_errors:
            errors.append(f'Actual runner errors: {observed_errors}')
    except (OSError, ET.ParseError, ValueError) as error:
        errors.append(str(error))
    return {'accepted': not errors, 'validationErrors': errors,
            'actualTests': tests, 'assemblies': assemblies}


def replay_reset(tests_dll: Path, output_dir: Path) -> dict:
    """Run the fixed checkpoint once; retain successful and rejected evidence.

    Invalid paths/existing output raise before invocation. A started run always
    returns a structured result, including XML/closure/process failures.
    """
    tests_dll = tests_dll.resolve(strict=True)
    if not tests_dll.is_file() or tests_dll.suffix.lower() != '.dll':
        raise ValueError('Expected an existing compiled test DLL')
    output_dir = output_dir.resolve()
    if output_dir.exists():
        raise FileExistsError('Replay output exists; choose a new directory')
    before = _dll_pins(tests_dll.parent)
    if tests_dll.name not in before:
        raise ValueError('Test DLL was not found in the pinned closure')
    output_dir.mkdir(parents=True, exist_ok=False)
    xml = output_dir / 'tests.xml'
    command = ['dotnet', str(tests_dll), '-noLogo', '-noColor', '-maxThreads', '1',
               '-method', RESET_METHOD, '-xml', str(xml)]
    errors = []
    exit_code = None
    started = time.perf_counter()
    with (output_dir / 'stdout.log').open('xb') as stdout, (output_dir / 'stderr.log').open('xb') as stderr:
        try:
            result = subprocess.run(command, cwd=tests_dll.parent, stdout=stdout, stderr=stderr,
                                    timeout=TIMEOUT_SECONDS)
            exit_code = result.returncode
        except (OSError, subprocess.TimeoutExpired) as error:
            errors.append(str(error))
    elapsed = time.perf_counter() - started
    if exit_code != 0:
        errors.append(f'Checkpoint process exit code: {exit_code}')
    try:
        after = _dll_pins(tests_dll.parent)
    except OSError as error:
        after = {}
        errors.append('Could not recheck DLL closure: ' + str(error))
    changed = sorted(name for name in before.keys() | after.keys() if before.get(name) != after.get(name))
    if changed:
        errors.append('Executed DLL closure changed during replay')
    audit = audit_xml(xml)
    report = {'schemaVersion': 1, 'operation': 'worldgen-replay-reset',
              'accepted': not errors and audit['accepted'], 'scope': SCOPE,
              'method': RESET_METHOD, 'testAssemblySha256': before[tests_dll.name],
              'command': command, 'exitCode': exit_code, 'wallSeconds': elapsed,
              'dllCount': len(before), 'dllsUnchanged': not changed,
              'changedDlls': changed, 'dllsBefore': before, 'dllsAfter': after,
              'validationErrors': errors, 'xmlAudit': audit,
              'logSha256': {name: _digest(output_dir / name) for name in ('stdout.log', 'stderr.log')}}
    if xml.is_file():
        report['xmlSha256'] = _digest(xml)
    with (output_dir / 'result.json').open('x', encoding='utf-8') as stream:
        json.dump(report, stream, indent=2, allow_nan=False)
        stream.write('\n')
    return report
