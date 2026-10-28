"""One reproducible offline workflow; check/mutate never modify production files."""
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path
import re
import struct
import subprocess
import sys
import tempfile
import time

from . import capture as facts
from .catalog import TARGETS, KEYS, NPC_FLOATS, NPC_ARRAY, ITEM_ARRAY, compare, emit, expected_rows, read_actual, read_items, read_npcs, single
from .csharp import mask


def write_new(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('xb') as stream:
        stream.write(data)


def _capture(args) -> dict:
    if args.output.exists():
        raise ValueError('Capture output exists; choose a new output path')
    admitted = facts.reference()
    if facts.digest(args.assembly) != admitted['assemblySha256']:
        raise ValueError('Official assembly SHA256 differs from tools/vanilla_data/reference.json')
    def check_dependencies():
        for name, expected in admitted['dependenciesSha256'].items():
            if facts.digest(args.assembly.parent / name) != expected:
                raise ValueError('Official dependency SHA256 differs: ' + name)
    check_dependencies()
    inputs = facts.pins()
    project = facts.ROOT / 'tools/vanilla_data/ReferenceProbe/ReferenceProbe.csproj'
    started = time.perf_counter()
    log = args.output.with_suffix('.build.log')
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open('xb') as stream:
        build = subprocess.run(['dotnet', 'build', str(project), '-c', 'Release', '-warnaserror', '-v', 'quiet'],
                               cwd=facts.ROOT, stdout=stream, stderr=subprocess.STDOUT)
    if build.returncode:
        raise ValueError('ReferenceProbe build failed; see ' + str(log))
    probe = project.parent / 'bin/Release/net11.0/ReferenceProbe.dll'
    probe_hash = facts.digest(probe)
    built = time.perf_counter()
    with tempfile.TemporaryDirectory(prefix='vanilla-capture-') as temporary:
        raw = Path(temporary) / 'capture.json'
        with args.output.with_suffix('.probe.log').open('xb') as stream:
            result = subprocess.run(['dotnet', str(probe), str(args.assembly.resolve()),
                                     admitted['assemblySha256'], str(raw), str(facts.PIN_PATH)],
                                    cwd=facts.ROOT, stdout=stream, stderr=subprocess.STDOUT)
        if result.returncode:
            raise ValueError('Official capture failed; see ' + str(args.output.with_suffix('.probe.log')))
        data = facts.read_json(raw)
    facts.validate(data, provenance=False)
    check_dependencies()
    if facts.pins() != inputs or facts.digest(args.assembly) != admitted['assemblySha256'] or facts.digest(probe) != probe_hash:
        raise ValueError('Capture inputs or executed probe changed during the run')
    data['captureInputs'] = inputs
    data['probeSha256'] = probe_hash
    data['dataSha256'] = facts.data_digest(data['datasets'])
    facts.validate(data)
    write_new(args.output, facts.canonical(data) + b'\n')
    return {'accepted': True, 'operation': 'capture', 'output': str(args.output),
            'assemblySha256': admitted['assemblySha256'], 'probeSha256': probe_hash,
            'buildWallSeconds': built - started, 'captureWallSeconds': time.perf_counter() - built,
            'rows': {name: len(rows) for name, rows in data['datasets'].items()},
            'scope': 'Official defaults in the named constructor profile; no AI/update/lifecycle claim'}


def _datasets(name: str) -> list[str]:
    return list(TARGETS) if name == 'all' else [name]


def check(data: dict, dataset: str, path: Path) -> dict:
    try:
        result = compare(dataset, expected_rows(data, dataset), read_actual(dataset, path))
        result['sourceSha256'] = facts.digest(path)
        return result
    except ValueError as error:
        return {'accepted': False, 'dataset': dataset, 'error': str(error)}


def _check(args, data: dict) -> dict:
    results = []
    for dataset in _datasets(args.dataset):
        path = args.actual or args.repo / TARGETS[dataset]
        results.append(check(data, dataset, path))
    return {'accepted': all(result['accepted'] for result in results), 'operation': 'check',
            'referenceSha256': facts.digest(args.reference), 'results': results,
            'scope': 'Complete represented data arrays only; no lifecycle/AI parity inference'}


def _mutate(args, data: dict) -> dict:
    results = []
    for dataset in _datasets(args.dataset):
        path = args.repo / TARGETS[dataset]
        before = path.read_bytes()
        baseline = check(data, dataset, path)
        if not baseline['accepted']:
            results.append({'dataset': dataset, 'accepted': False, 'baseline': baseline})
            continue
        rows = read_actual(dataset, path)
        template = path.read_text(encoding='utf-8-sig')
        reader = read_npcs if dataset == 'npc-defaults' else read_items
        expected = expected_rows(data, dataset)
        # Every column at both ends, plus missing/duplicate rows. This is a named
        # bounded mutation set, not a claim to corrupt every possible program/cell.
        mutations = []
        for index in sorted({0, len(rows) - 1}):
            for field, value in rows[index].items():
                mutant = copy.deepcopy(rows)
                if field == KEYS[dataset]:
                    identities = [row[field] for row in rows]
                    mutant[index][field] = min(identities) - 1 if index == 0 else max(identities) + 1
                else:
                    mutant[index][field] = not value if type(value) is bool else value + 1
                if dataset == 'npc-defaults' and field in NPC_FLOATS:
                    mutant[index][field] = single(mutant[index][field])
                mutations.append((f'row={rows[index][KEYS[dataset]]}/{field}', emit(dataset, mutant, template)))
        mutations.append(('missing-first-row', emit(dataset, copy.deepcopy(rows[1:]), template)))
        # Duplicate directly in source: the generator refuses duplicate keys, so
        # counting its refusal as a checker kill would never exercise the checker.
        code, _ = mask(template)
        array = re.search(NPC_ARRAY if dataset == 'npc-defaults' else ITEM_ARRAY, code, re.S)
        assert array is not None
        if dataset == 'npc-defaults':
            first = re.search(r'new\s*\([^()]*\)', array['body'])
            assert first is not None
            duplicate = template[:array.start('body')] + '\n        ' + first[0] + ',' + template[array.start('body'):]
        else:
            tokens = re.findall(r'0x[0-9a-fA-F]{2}\b', array['body'])
            duplicate = template[:array.start('body')] + '\n        ' + ', '.join(tokens[:8]) + ',' + template[array.start('body'):]
            for name, delta in {'Count': 1, 'TilePlacingCount': int(rows[0]['createTile'] >= 0),
                                'WallPlacingCount': int(rows[0]['createWall'] >= 0)}.items():
                current_code, _ = mask(duplicate)
                match = re.search(r'\bconst int ' + name + r'\s*=\s*(\d+)\s*;', current_code)
                assert match is not None
                a, b = match.span(1)
                duplicate = duplicate[:a] + str(int(match[1]) + delta) + duplicate[b:]
        mutations.append(('duplicate-first-row', duplicate))
        verdicts = []
        for name, text in mutations:
            try:
                verdict = compare(dataset, expected, reader(text))
                detected = not verdict['accepted']
            except ValueError:
                detected = True
            verdicts.append({'mutation': name, 'detected': detected})
        if path.read_bytes() != before:
            raise ValueError('Production input changed during mutation checks')
        results.append({'dataset': dataset, 'accepted': all(row['detected'] for row in verdicts),
                        'sourceSha256': facts.digest(path), 'mutants': verdicts})
    return {'accepted': all(row['accepted'] for row in results), 'operation': 'mutate', 'results': results,
            'scope': 'In-memory first/last-row column mutations and missing/duplicate rows; production files untouched'}


def _worldgen(args) -> dict:
    if args.action == 'run-pass':
        from .pass_probe import run_pass
        return run_pass(args.assembly, args.output_dir, args.seed, args.control)
    if args.action == 'compare-pass':
        from . import pass_trace
        official, official_hash = pass_trace.load_with_hash(args.official, 'official')
        runtime, runtime_hash = pass_trace.load_with_hash(args.runtime, 'runtime')
        report = pass_trace.compare(official, runtime)
        report['officialObservationSha256'] = official_hash
        report['runtimeObservationSha256'] = runtime_hash
        return report
    if args.action == 'replay-reset':
        from .worldgen_replay import replay_reset
        return replay_reset(args.tests_dll, args.output_dir)
    from . import worldgen
    if args.action == 'capture':
        if args.output.exists():
            raise ValueError('Worldgen capture output exists; choose a new file')
        data = worldgen.capture(args.source_root)
        worldgen.validate(data)
        write_new(args.output, facts.canonical(data) + b'\n')
        return {'accepted': True, 'operation': 'worldgen-capture', 'output': str(args.output),
                'sha256': facts.digest(args.output), 'coverage': data['coverage'],
                'scope': 'Lexical registration metadata only; no pass/world execution'}
    data = facts.read_json(args.reference)
    if not isinstance(data, dict):
        raise ValueError('Worldgen reference must be a JSON object')
    if args.action == 'check':
        return worldgen.check(data, args.repo, args.source_root)
    return worldgen.mutate(data, args.repo)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    capture = commands.add_parser('capture', help='Build an isolated probe and invoke pinned official defaults')
    capture.add_argument('--assembly', type=Path, required=True)
    capture.add_argument('--output', type=Path, required=True)
    worldgen = commands.add_parser('worldgen', help='Source pass catalog, compiled Reset replay and fresh isolated cell/RNG comparison')
    worldgen_commands = worldgen.add_subparsers(dest='action', required=True)
    worldgen_capture = worldgen_commands.add_parser('capture')
    worldgen_capture.add_argument('--source-root', type=Path, required=True)
    worldgen_capture.add_argument('--output', type=Path, required=True)
    for action in ('check', 'mutate'):
        command = worldgen_commands.add_parser(action)
        command.add_argument('--reference', type=Path, required=True)
        command.add_argument('--repo', type=Path, default=facts.ROOT)
        command.add_argument('--report', type=Path)
        if action == 'check':
            command.add_argument('--source-root', type=Path)
    replay = worldgen_commands.add_parser('replay-reset')
    replay.add_argument('--tests-dll', type=Path, required=True)
    replay.add_argument('--output-dir', type=Path, required=True)
    compare_pass = worldgen_commands.add_parser('compare-pass')
    compare_pass.add_argument('--official', type=Path, required=True)
    compare_pass.add_argument('--runtime', type=Path, required=True)
    compare_pass.add_argument('--report', type=Path)
    run_pass = worldgen_commands.add_parser('run-pass', help='Fresh isolated Grass Wall observations and cell/RNG comparison')
    run_pass.add_argument('--assembly', type=Path, required=True)
    run_pass.add_argument('--output-dir', type=Path, required=True)
    run_pass.add_argument('--seed', type=int, default=1458)
    run_pass.add_argument('--control', choices=('normal', 'omit-pass', 'extra-rng'), default='normal')
    for name in ('check', 'emit', 'mutate', 'verify'):
        command = commands.add_parser(name)
        command.add_argument('dataset', choices=[*TARGETS, 'all'] if name != 'emit' else list(TARGETS))
        command.add_argument('--reference', type=Path, required=True)
        command.add_argument('--repo', type=Path, default=facts.ROOT)
        if name != 'emit':
            command.add_argument('--report', type=Path)
        if name == 'check':
            command.add_argument('--actual', type=Path, help='One candidate source file; requires one dataset')
        if name == 'emit':
            command.add_argument('--output', type=Path, required=True, help='New candidate file; existing files are refused')
    args = parser.parse_args(argv)
    started = time.perf_counter()
    try:
        if args.command == 'capture':
            report = _capture(args)
        elif args.command == 'worldgen':
            report = _worldgen(args)
        else:
            data = facts.load(args.reference)
            if args.command == 'check':
                if args.actual and args.dataset == 'all':
                    raise ValueError('--actual requires one dataset')
                report = _check(args, data)
            elif args.command == 'emit':
                path = args.repo / TARGETS[args.dataset]
                text = emit(args.dataset, expected_rows(data, args.dataset), path.read_text(encoding='utf-8-sig'))
                # Re-read the generated bytes through the independent actual-layout reader.
                actual = read_npcs(text) if args.dataset == 'npc-defaults' else read_items(text)
                if not compare(args.dataset, expected_rows(data, args.dataset), actual)['accepted']:
                    raise ValueError('Generated candidate does not reproduce the complete reference')
                write_new(args.output, text.encode('utf-8'))
                report = {'accepted': True, 'operation': 'emit', 'output': str(args.output),
                          'sha256': facts.digest(args.output), 'scope': 'Candidate data only; original implementation is not overwritten'}
            elif args.command == 'mutate':
                report = _mutate(args, data)
            else:
                args.actual = None
                checked = _check(args, data)
                mutations = _mutate(args, data) if checked['accepted'] else None
                report = {'accepted': checked['accepted'] and mutations is not None and mutations['accepted'],
                          'operation': 'verify', 'check': checked, 'mutation': mutations}
        report['wallSeconds'] = time.perf_counter() - started
        if getattr(args, 'report', None):
            write_new(args.report, json.dumps(report, indent=2, allow_nan=False).encode('utf-8') + b'\n')
        # Full field diagnostics remain in --report. Keep routine terminal output bounded.
        summary = {key: value for key, value in report.items() if key not in (
            'results', 'check', 'mutation', 'dllsBefore', 'dllsAfter', 'xmlAudit', 'command', 'commands')}
        if 'results' in report:
            summary['datasets'] = [{'dataset': row['dataset'], 'accepted': row['accepted'],
                                    'differences': len(row.get('differences', [])),
                                    **({'error': row['error']} if 'error' in row else {})} for row in report['results']]
        print(json.dumps(summary, allow_nan=False))
        if not report['accepted'] and not getattr(args, 'report', None):
            details = copy.deepcopy(report)
            for container in (details, details.get('check', {})):
                for result in container.get('results', []):
                    if 'differences' in result:
                        count = len(result['differences'])
                        result['differences'] = result['differences'][:10]
                        result['omittedDifferences'] = max(0, count - 10)
            print(json.dumps(details, indent=2, allow_nan=False))
        return 0 if report['accepted'] else 1
    except (ValueError, KeyError, TypeError, OSError, OverflowError, struct.error) as error:
        print('vanilla-data: ' + str(error), file=sys.stderr)
        return 2
