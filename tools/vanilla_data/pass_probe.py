"""Build the current isolated-pass backend and compare its fresh observations."""
from __future__ import annotations

import json
from pathlib import Path
import subprocess
import time

from . import capture as facts
from . import pass_trace

PROJECT = 'tools/vanilla_data/PassProbe/PassProbe.csproj'
CONTROLS = ('normal', 'omit-pass', 'extra-rng')
PROFILES = {
    'grass-wall': ('SurfaceDirtWallsToGrassWalls', 'grass-wall-stain-v1', 'Grass Wall', 'GrassWallPass1458'),
    'quick-cleanup': ('QuickCleanup', 'quick-cleanup-ordinary-v1', 'Quick Cleanup', 'QuickCleanupPass1458'),
    'quick-cleanup-shapes': ('QuickCleanup', 'quick-cleanup-independent-shapes-v1', 'Quick Cleanup', 'QuickCleanupPass1458'),
}


def source_inputs() -> dict:
    names = set()
    for extra in ([], ['--others', '--exclude-standard']):
        raw = subprocess.check_output(['git', 'ls-files', *extra, '-z', '--', 'src'], cwd=facts.ROOT)
        names.update(name for name in raw.decode('utf-8').split('\0') if name)
    names.update(path.relative_to(facts.ROOT).as_posix() for path in
                 (facts.ROOT / 'tools/vanilla_data').rglob('*')
                 if path.is_file() and path.suffix in ('.cs', '.csproj', '.py', '.json')
                 and not {'bin', 'obj', '__pycache__'}.intersection(path.parts))
    names.update(('tools/vanilla.py', 'global.json', 'Directory.Build.props'))
    return {name: facts.digest(facts.ROOT / name) for name in sorted(names)}


def dll_pins(folder: Path) -> dict:
    return {path.relative_to(folder).as_posix(): facts.digest(path) for path in sorted(folder.rglob('*.dll'))}


def run_pass(assembly: Path, output_dir: Path, seed: int = 1458, control: str = 'normal', profile: str = 'grass-wall') -> dict:
    if type(seed) is not int or not -0x80000000 <= seed <= 0x7fffffff or control not in CONTROLS or profile not in PROFILES:
        raise ValueError('Unsupported seed/control/profile')
    pass_id, scenario, registered_name, runtime_type = PROFILES[profile]
    assembly = assembly.resolve(strict=True)
    admitted = facts.reference()
    if facts.digest(assembly) != admitted['assemblySha256']:
        raise ValueError('Official assembly differs from the admitted reference')
    for name, expected in admitted['dependenciesSha256'].items():
        if facts.digest(assembly.parent / name) != expected:
            raise ValueError('Official dependency differs: ' + name)
    output_dir = output_dir.resolve()
    if output_dir.exists():
        raise FileExistsError('Pass output exists; choose a new directory')
    before = source_inputs()
    output_dir.mkdir(parents=True, exist_ok=False)
    (output_dir / 'source-before.json').write_bytes(facts.canonical(before) + b'\n')
    commands, errors = [], []
    started = time.perf_counter()

    def execute(name: str, command: list[str], timeout: int) -> bool:
        with (output_dir / (name + '.log')).open('xb') as stream:
            try:
                result = subprocess.run(command, cwd=facts.ROOT, stdout=stream, stderr=subprocess.STDOUT, timeout=timeout)
                commands.append({'name': name, 'command': command, 'exitCode': result.returncode})
                if result.returncode == 0:
                    return True
                errors.append(name + ': process exit ' + str(result.returncode))
            except (OSError, subprocess.TimeoutExpired) as error:
                errors.append(name + ': ' + str(error))
        return False

    built = execute('build', ['dotnet', 'build', str(facts.ROOT / PROJECT), '-c', 'Release', '-warnaserror', '-v', 'quiet'], 120)
    binary = facts.ROOT / 'tools/vanilla_data/PassProbe/bin/Release/net11.0/PassProbe.dll'
    pins = dll_pins(binary.parent) if built else {}
    compared = None
    if built and not pins:
        errors.append('Fresh backend has no DLL closure')
    if built and pins:
        for engine in ('official', 'runtime'):
            mode = control if engine == 'runtime' else 'normal'
            if not execute(engine, ['dotnet', str(binary), engine, str(assembly), str(facts.PIN_PATH),
                                   str(output_dir / (engine + '.json')), str(seed), mode, profile], 30):
                break
        if not errors:
            try:
                official = pass_trace.load(output_dir / 'official.json', 'official')
                runtime = pass_trace.load(output_dir / 'runtime.json', 'runtime')
                for observation in (official, runtime):
                    if observation.get('executedDlls') != pins:
                        raise ValueError('Observation does not identify the executed DLL closure')
                    if observation['pass'] != pass_id or observation['scenario'] != scenario or observation['context']['seed'] != seed:
                        raise ValueError('Observation is not the requested pass/scenario/seed')
                    provenance = observation['provenance']
                    mode = control if observation['engine'] == 'runtime' else 'normal'
                    if provenance.get('control') != mode or provenance.get('probeSha256') != pins.get('PassProbe.dll'):
                        raise ValueError('Observation control/probe identity differs')
                if official['provenance'].get('entry') != 'registered-GenPass.Apply' or official['provenance'].get('registeredName') != registered_name or official['provenance'].get('officialAssemblySha256') != admitted['assemblySha256'] or official['provenance'].get('dependenciesSha256') != admitted['dependenciesSha256']:
                    raise ValueError('Official entry/artifact identity differs')
                if runtime['provenance'].get('entry') != 'TerraRuntime.WorldGeneration.Vanilla.' + runtime_type + '.Apply' or runtime['provenance'].get('runtimeAssemblySha256') != pins.get('TerraRuntime.WorldGeneration.dll'):
                    raise ValueError('Runtime entry/artifact identity differs')
                compared = pass_trace.compare(official, runtime)
                (output_dir / 'comparison.json').write_text(json.dumps(compared, indent=2) + '\n', encoding='utf-8')
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
        after = {}
        errors.append('Could not recheck backend DLLs: ' + str(error))
    if not unchanged:
        errors.append('Source/tool inputs changed during build/capture')
    if pins and after != pins:
        errors.append('Executed backend DLL closure changed')
    for name, expected in {'TerrariaServer.exe': admitted['assemblySha256'], **admitted['dependenciesSha256']}.items():
        path = assembly if name == 'TerrariaServer.exe' else assembly.parent / name
        try:
            if facts.digest(path) != expected:
                errors.append('Official input changed: ' + name)
        except OSError as error:
            errors.append('Could not recheck official input: ' + str(error))
    accepted = not errors and compared is not None and compared['accepted']
    report = {'schemaVersion': 1, 'operation': 'worldgen-run-pass', 'accepted': accepted,
              'classification': 'execution-or-input-failure' if errors else compared['classification'] if compared else 'no-observation',
              'pass': pass_id, 'scenario': scenario, 'profile': profile, 'seed': seed, 'control': control,
              'sourceInputCount': len(before), 'sourceInputsUnchanged': unchanged,
              'sourceInputsSha256': facts.data_digest(before), 'dllsBefore': pins, 'dllsAfter': after,
              'dllsUnchanged': pins == after, 'commands': commands, 'validationErrors': errors,
              'comparison': compared, 'wallSeconds': time.perf_counter() - started, 'scope': pass_trace.SCOPE,
              'outputDir': str(output_dir)}
    (output_dir / 'result.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    return report
