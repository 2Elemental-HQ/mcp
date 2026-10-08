#!/usr/bin/env python3
"""Run separately limited processes; retain errors and verify every returned SHA-256."""
import argparse, hashlib, json, pathlib, subprocess, time, urllib.request

parser = argparse.ArgumentParser()
parser.add_argument('--host', required=True, type=pathlib.Path)
parser.add_argument('--fixtures', required=True, type=pathlib.Path)
parser.add_argument('--output', required=True, type=pathlib.Path)
parser.add_argument('--route', choices=['string', 'stream'], required=True)
parser.add_argument('--image', required=True, help='Pinned .NET 10 runtime image digest')
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)

def command(parts, **kwargs):
    return subprocess.run(parts, text=True, capture_output=True, **kwargs)

limits = ['--memory=1g', '--memory-swap=1g', '--cpus=2', '-e', 'DOTNET_GCHeapHardLimit=0x20000000', '-e', 'DOTNET_GCHeapCount=2']
mounts = ['-v', f'{args.host.resolve()}:/app:ro', '-v', f'{args.fixtures.resolve()}:/fixtures:ro']
for ending in ['lf', 'crlf']:
    expected = hashlib.sha256((args.fixtures / f'{ending}.txt').read_bytes()).hexdigest().upper()
    (args.fixtures / f'{ending}.sha256').write_text(expected)
    for scenario, phases in [('history', [(1, 1), (20, 1), (20, 10)]), ('fresh-ten', [(10, 10)]), ('repeat-ten', [(100, 10)])]:
        name = f'mcp-bounded-{ending}-{scenario}'
        folder = args.output / ending / scenario
        folder.mkdir(parents=True, exist_ok=True)
        server = ['docker', 'run', '-d', '--name', name, *limits, *mounts, '-p', '127.0.0.1::8080', args.image, 'dotnet', '/app/Repro.dll', 'server', f'/fixtures/{ending}.txt', args.route]
        started = command(server)
        (folder / 'start.json').write_text(json.dumps({'command': server, 'returncode': started.returncode, 'stdout': started.stdout, 'stderr': started.stderr}, indent=2))
        if started.returncode: continue
        try:
            port = command(['docker', 'port', name, '8080/tcp']).stdout.strip().rsplit(':', 1)[1]
            endpoint = f'http://127.0.0.1:{port}/metrics'
            for attempt in range(100):
                try:
                    with urllib.request.urlopen(endpoint, timeout=2) as response: before = json.load(response)
                    break
                except Exception: time.sleep(.1)
            else: raise RuntimeError('Server readiness failed')
            for phase, (count, concurrency) in enumerate(phases):
                client = ['docker', 'run', '--rm', *limits, *mounts, '--link', f'{name}:server', args.image, 'dotnet', '/app/Repro.dll', 'client', f'/fixtures/{ending}.sha256', args.route, str(count), str(concurrency), 'http://server:8080']
                result = command(client, timeout=360)
                (folder / f'{phase}-client.json').write_text(json.dumps({'command': client, 'returncode': result.returncode, 'stdout': result.stdout, 'stderr': result.stderr}, indent=2))
                with urllib.request.urlopen(endpoint, timeout=10) as response: after = json.load(response)
                (folder / f'{phase}-server.json').write_text(json.dumps({'before': before, 'after': after}, indent=2))
                before = after
                print(ending, scenario, phase, result.returncode, flush=True)
        except Exception as error:
            (folder / 'failure.txt').write_text(repr(error))
        finally:
            (folder / 'server.log').write_text(command(['docker', 'logs', name]).stdout)
            (folder / 'inspect.json').write_text(command(['docker', 'inspect', name]).stdout)
            command(['docker', 'rm', '-f', name])
