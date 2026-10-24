"""Execute source-matched CPU tests and paired navigation measurements."""
import json
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'artifacts/navigation'
OUT.mkdir(parents=True, exist_ok=True)


def run(name, args, expected_success=True):
    with (OUT / (name + '.log')).open('w', encoding='utf-8') as stream:
        result = subprocess.run(args, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT, check=False)
    print(name, result.returncode, flush=True)
    if expected_success and result.returncode:
        print((OUT / (name + '.log')).read_text(encoding='utf-8')[-18000:], flush=True)
        raise RuntimeError(name + ' failed')
    return result.returncode


def main():
    phase = sys.argv[1]
    game = 'tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj'
    nav = 'tests/ForgeLine.Navigation.Tests/ForgeLine.Navigation.Tests.csproj'
    probe = 'build/navigation/NavigationProbe.csproj'
    run(phase + '-game-build', ['dotnet', 'build', game, '-c', 'Release'])
    run(phase + '-nav-tests', ['dotnet', 'test', '--project', nav, '-c', 'Release'])
    code = run(phase + '-recovery-tests', ['dotnet', 'test', '--project', game, '-c', 'Release', '--no-build', '--',
        '--filter-class', '*LogisticsNavigationRecoveryTests', '*NavigationWaypointOwnershipTests'], expected_success=phase != 'baseline')
    if phase == 'baseline':
        text = (OUT / 'baseline-recovery-tests.log').read_text(encoding='utf-8')
        if code == 0 or 'WorldChangeDuringPendingSearchRetainsLoadedTruckIntent' not in text:
            raise RuntimeError('Expected named baseline regression was not reproduced; inspect original log')
        run('baseline-game-tests', ['dotnet', 'test', '--project', game, '-c', 'Release', '--no-build'], expected_success=False)
    else:
        run('candidate-game-tests', ['dotnet', 'test', '--project', game, '-c', 'Release', '--no-build'])
    run(phase + '-probe-build', ['dotnet', 'build', probe, '-c', 'Release'])
    target = OUT / (phase + '-probe')
    shutil.copytree(ROOT / 'build/navigation/bin/Release/net10.0', target, dirs_exist_ok=True)
    (OUT / (phase + '-tests.json')).write_text(json.dumps({'phase': phase, 'recoveryExitCode': code,
        'sourceHead': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT).decode().strip()}))


if __name__ == '__main__':
    main()
