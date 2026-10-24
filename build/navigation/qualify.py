"""Run paired source-matched tests without concealing unrelated baseline failures."""
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'artifacts/navigation'
OUT.mkdir(parents=True, exist_ok=True)
RECOVERY = ('LogisticsNavigationRecoveryTests', 'NavigationWaypointOwnershipTests', 'NavigationBlockadeRecoveryTests')
LEGACY = ('ForgeLine.Game.Tests.CompiledNavigationParityTests.',
          'ForgeLine.Game.Tests.CompiledScenarioTests.ProductiveCompiledStartPreservesLegacyCheckpointsAndGeography',
          'ForgeLine.Game.Tests.PersistenceBoundaryTests.RejectedWritePreservesLastReadableFileAndCleansTemporaryFiles')


def run(name, args, expected_success=True):
    with (OUT / (name + '.log')).open('w', encoding='utf-8') as stream:
        result = subprocess.run(args, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT, check=False)
    print(name, result.returncode, flush=True)
    if expected_success and result.returncode:
        print((OUT / (name + '.log')).read_text(encoding='utf-8')[-18000:], flush=True)
        raise RuntimeError(name + ' failed')
    return result.returncode


def failures(name):
    text = (OUT / (name + '.log')).read_text(encoding='utf-8')
    text = re.sub(r'\x1b\[[0-?]*[ -/]*[@-~]', '', text)
    result = {}
    matches = list(re.finditer(r'^failed (.+?)\s+\([^()\r\n]*\)\s*$', text, re.MULTILINE))
    for index, match in enumerate(matches):
        identity = match.group(1)
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        block = text[match.end():end]
        evidence = [line.strip() for line in block.splitlines()
                    if line.strip().startswith(('Expected:', 'Actual:')) or 'Locked replacement succeeded.' in line]
        result[identity] = evidence
    return result


def main():
    phase = sys.argv[1]
    game = 'tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj'
    nav = 'tests/ForgeLine.Navigation.Tests/ForgeLine.Navigation.Tests.csproj'
    probe = 'build/navigation/NavigationProbe.csproj'
    run(phase + '-game-build', ['dotnet', 'build', game, '-c', 'Release'])
    run(phase + '-nav-tests', ['dotnet', 'test', '--project', nav, '-c', 'Release'])
    code = run(phase + '-recovery-tests', ['dotnet', 'test', '--project', game, '-c', 'Release', '--no-build', '--',
        '--filter-class', *['*' + name for name in RECOVERY]], expected_success=phase != 'baseline')
    if phase == 'baseline':
        text = (OUT / 'baseline-recovery-tests.log').read_text(encoding='utf-8')
        if code != 2 or 'WorldChangeDuringPendingSearchRetainsLoadedTruckIntent' not in text:
            raise RuntimeError('Named baseline regression was not reproduced; inspect original log')
    full_code = run(phase + '-game-tests', ['dotnet', 'test', '--project', game, '-c', 'Release', '--no-build'],
                    expected_success=False)
    if full_code not in (0, 2):
        raise RuntimeError('Full Game test execution failed independently of assertions')
    observed = failures(phase + '-game-tests')
    if full_code and not observed:
        raise RuntimeError('Failed test output was not parsed; original log is required')
    legacy = {name: evidence for name, evidence in observed.items() if not any(c in name for c in RECOVERY)}
    if any(not name.startswith(LEGACY) for name in legacy):
        raise RuntimeError('An unreviewed full-suite failure requires investigation: ' + str(legacy))
    if phase == 'candidate':
        before = json.loads((OUT / 'baseline-tests.json').read_text())['legacyFailures']
        if any(name not in before or evidence != before[name] for name, evidence in legacy.items()):
            raise RuntimeError('Candidate introduced or changed a baseline full-suite failure: ' + str(legacy))
        if len(observed) != len(legacy):
            raise RuntimeError('Recovery regressed in the complete Game run')
    run(phase + '-probe-build', ['dotnet', 'build', probe, '-c', 'Release'])
    target = OUT / (phase + '-probe')
    shutil.copytree(ROOT / 'build/navigation/bin/Release/net10.0', target, dirs_exist_ok=True)
    (OUT / (phase + '-tests.json')).write_text(json.dumps({'phase': phase, 'recoveryExitCode': code,
        'fullGameExitCode': full_code, 'fullGamePassed': full_code == 0, 'legacyFailures': legacy,
        'sourceHead': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT).decode().strip()}, indent=2))


if __name__ == '__main__':
    main()
