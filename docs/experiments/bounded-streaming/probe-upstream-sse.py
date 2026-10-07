#!/usr/bin/env python3
"""Compare the real SSE fixture with exact upstream without changing candidate runtime code."""
import argparse, json, os, pathlib, subprocess, sys

parser = argparse.ArgumentParser()
parser.add_argument('--configuration', default='Release')
args = parser.parse_args()
repo = pathlib.Path(__file__).resolve().parents[3]
base = '3338e88e15c42cfc27465143c140d7d10f1a2707'
output = repo / 'artifacts/testresults/upstream-sse'
output.mkdir(parents=True, exist_ok=True)
checkout = repo / 'artifacts/upstream-sse-checkout'
subprocess.run(['git', 'clone', '--shared', '--no-checkout', str(repo), str(checkout)], check=True)
subprocess.run(['git', 'checkout', '--detach', base], cwd=checkout, check=True)
files = ['tests/Common/Utils/NodeHelpers.cs',
         'tests/ModelContextProtocol.Tests/EverythingSseServerFixture.cs',
         'tests/ModelContextProtocol.Tests/EverythingSseServerTests.cs']
for name in files:
    (checkout / name).write_bytes((repo / name).read_bytes())
assert not subprocess.check_output(['git', 'diff', '--', 'src'], cwd=checkout)
command = ['dotnet', 'test', 'tests/ModelContextProtocol.Tests', '-f', 'net472', '-c', args.configuration,
           '--filter', 'FullyQualifiedName~EverythingSseServerTests', '--logger', 'trx;LogFileName=upstream-sse.trx',
           '--results-directory', str(output), '--blame-hang-timeout', '3m']
with (output / 'upstream-sse.log').open('w') as log:
    install = subprocess.run(['npm.cmd' if os.name == 'nt' else 'npm', 'ci'], cwd=checkout, stdout=log, stderr=subprocess.STDOUT)
    if install.returncode:
        raise SystemExit(install.returncode)
    try:
        result = subprocess.run(command, cwd=checkout, stdout=log, stderr=subprocess.STDOUT, timeout=480)
        code = result.returncode
    except subprocess.TimeoutExpired:
        code = 124
summary = {'upstream': base, 'runtimeChanged': False, 'fixtureFiles': files, 'command': command, 'exitCode': code,
           'purpose': 'Diagnostic comparison only. Candidate full-suite failures remain blocking and are not suppressed.'}
(output / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
print(json.dumps(summary, indent=2))
# A baseline failure is evidence, not a waiver for the separate candidate test gate.
