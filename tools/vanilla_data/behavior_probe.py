"""Fresh offline NPC/gameplay execution with common source and artifact guards."""
from __future__ import annotations

import importlib
import json
from pathlib import Path
import subprocess
import time

from . import capture as facts
from .pass_probe import source_inputs, dll_pins

PROJECTS = {'npc': 'NpcProbe', 'gameplay': 'GameplayProbe'}
CONTROLS = ('normal', 'omit-step', 'extra-rng')


def adapter(domain: str):
    if domain not in PROJECTS:
        raise ValueError('Unsupported behavior domain')
    return importlib.import_module('.' + domain + '_trace', __package__)


def run_behavior(domain: str, assembly: Path, output_dir: Path,
                 seed: int = 1458, control: str = 'normal') -> dict:
    trace = adapter(domain)
    if type(seed) is not int or not -0x80000000 <= seed <= 0x7fffffff or control not in CONTROLS:
        raise ValueError('Unsupported seed/control')
    assembly = assembly.resolve(strict=True)
    admitted = facts.reference()
    official = {assembly: admitted['assemblySha256'], **{
        assembly.parent / name: expected for name, expected in admitted['dependenciesSha256'].items()}}
    for path, expected in official.items():
        if facts.digest(path) != expected:
            raise ValueError('Official input differs from the admitted reference: ' + path.name)
    output_dir = output_dir.resolve()
    if output_dir.exists():
        raise FileExistsError('Behavior output exists; choose a new directory')
    before = source_inputs()
    output_dir.mkdir(parents=True, exist_ok=False)
    (output_dir / 'source-before.json').write_bytes(facts.canonical(before) + b'\n')
    started = time.perf_counter()
    commands, errors = [], []

    def execute(name, command, timeout):
        with (output_dir / (name + '.log')).open('xb') as stream:
            try:
                process = subprocess.run(command, cwd=facts.ROOT, stdout=stream,
                                         stderr=subprocess.STDOUT, timeout=timeout)
                commands.append({'name': name, 'command': command, 'exitCode': process.returncode})
                if process.returncode == 0:
                    return True
                errors.append(name + ': process exit ' + str(process.returncode))
            except (OSError, subprocess.TimeoutExpired) as error:
                errors.append(name + ': ' + str(error))
        return False

    project_name = PROJECTS[domain]
    folder = facts.ROOT / 'tools/vanilla_data' / project_name
    built = execute('build', ['dotnet', 'build', str(folder / (project_name + '.csproj')),
                             '-c', 'Release', '-warnaserror', '-v', 'quiet'], 120)
    binary = folder / 'bin/Release/net11.0' / (project_name + '.dll')
    pins, after, compared = {}, {}, None
    if built:
        try:
            pins = dll_pins(binary.parent)
            if not pins or project_name + '.dll' not in pins or trace.RUNTIME_DLL not in pins:
                errors.append('Fresh backend lacks its required DLL closure')
        except OSError as error:
            errors.append('Could not pin backend DLLs: ' + str(error))
    if built and not errors:
        for engine in ('official', 'runtime'):
            mode = control if engine == 'runtime' else 'normal'
            if not execute(engine, ['dotnet', str(binary), engine, str(assembly), str(facts.PIN_PATH),
                                   str(output_dir / (engine + '.json')), str(seed), mode], 30):
                break
        if not errors:
            try:
                captures = {}
                hashes = {}
                for engine in ('official', 'runtime'):
                    observation, raw_hash = trace.load_with_hash(output_dir / (engine + '.json'), engine)
                    if observation.get('scenario') != trace.SCENARIO or observation.get('seed') != seed:
                        raise ValueError('Observation is not the requested scenario/seed')
                    if observation.get('executedDlls') != pins:
                        raise ValueError('Observation does not identify the executed DLL closure')
                    provenance = observation['provenance']
                    expected_entry = trace.OFFICIAL_ENTRY if engine == 'official' else trace.RUNTIME_ENTRY
                    mode = control if engine == 'runtime' else 'normal'
                    if provenance.get('entry') != expected_entry or provenance.get('control') != mode:
                        raise ValueError('Observation entry/control differs')
                    if provenance.get('probeSha256') != pins[project_name + '.dll']:
                        raise ValueError('Observation probe identity differs')
                    if provenance.get('officialAssemblySha256') != admitted['assemblySha256'] or provenance.get('dependenciesSha256') != admitted['dependenciesSha256']:
                        raise ValueError('Official artifact identity differs')
                    if engine == 'runtime' and provenance.get('runtimeAssemblySha256') != pins[trace.RUNTIME_DLL]:
                        raise ValueError('Runtime artifact identity differs')
                    captures[engine], hashes[engine] = observation, raw_hash
                compared = trace.compare(captures['official'], captures['runtime'])
                compared.update(officialObservationSha256=hashes['official'], runtimeObservationSha256=hashes['runtime'])
                (output_dir / 'comparison.json').write_bytes((json.dumps(compared, indent=2, allow_nan=False) + '\n').encode())
            except (ValueError, OSError, KeyError, TypeError) as error:
                errors.append('Observation comparison: ' + str(error))
    try:
        unchanged = source_inputs() == before
    except OSError as error:
        unchanged = False
        errors.append('Could not recheck source inputs: ' + str(error))
    try:
        after = dll_pins(binary.parent) if pins else {}
    except OSError as error:
        errors.append('Could not recheck backend DLLs: ' + str(error))
    if not unchanged:
        errors.append('Source/tool inputs changed during build/capture')
    if pins and after != pins:
        errors.append('Executed backend DLL closure changed')
    for path, expected in official.items():
        try:
            if facts.digest(path) != expected:
                errors.append('Official input changed: ' + path.name)
        except OSError as error:
            errors.append('Could not recheck official input: ' + str(error))
    accepted = not errors and compared is not None and compared['accepted']
    report = {'schemaVersion': 1, 'operation': domain + '-run', 'accepted': accepted,
              'classification': 'execution-or-input-failure' if errors else compared['classification'] if compared else 'no-observation',
              'scenario': trace.SCENARIO, 'seed': seed, 'control': control,
              'sourceInputCount': len(before), 'sourceInputsUnchanged': unchanged,
              'sourceInputsSha256': facts.data_digest(before), 'dllsBefore': pins, 'dllsAfter': after,
              'dllsUnchanged': pins == after, 'commands': commands, 'validationErrors': errors,
              'comparison': compared, 'wallSeconds': time.perf_counter() - started,
              'scope': trace.SCOPE, 'outputDir': str(output_dir)}
    (output_dir / 'result.json').write_bytes((json.dumps(report, indent=2, allow_nan=False) + '\n').encode())
    return report
