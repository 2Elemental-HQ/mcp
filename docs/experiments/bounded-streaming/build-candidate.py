#!/usr/bin/env python3
"""Build local-only, commit-identified packages; never publish them."""
import argparse, hashlib, json, pathlib, subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--output", required=True, type=pathlib.Path)
args = parser.parse_args()
repo = pathlib.Path(__file__).resolve().parents[3]
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=False)

def run(parts):
    return subprocess.check_output(parts, cwd=repo, text=True).strip()

source = run(["git", "rev-parse", "HEAD"])
if run(["git", "status", "--porcelain", "--", "src", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"]):
    raise SystemExit("Commit all build/runtime inputs before creating an identified candidate.")
version = f"2.2.0-elemental.1.g{source[:12]}"
projects = ["ModelContextProtocol.Core", "ModelContextProtocol", "ModelContextProtocol.AspNetCore"]
for project in projects:
    command = ["dotnet", "pack", f"src/{project}/{project}.csproj", "-c", "Release", "-o", str(output),
               f"-p:Version={version}", "-p:ContinuousIntegrationBuild=true", "-p:Deterministic=true",
               "-p:RepositoryUrl=https://github.com/2Elemental-HQ/mcp", f"-p:RepositoryCommit={source}",
               "-p:PublishRepositoryUrl=true", f"-p:ArtifactsDir={output / 'build'}/"]
    with (output / f"{project}.build.log").open("w") as log:
        subprocess.run(command, cwd=repo, stdout=log, stderr=subprocess.STDOUT, check=True)
manifest = {"source": source, "version": version, "sdk": run(["dotnet", "--version"]),
            "upstreamBase": "3338e88e15c42cfc27465143c140d7d10f1a2707",
            "packages": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(output.glob("*.nupkg"))}}
(output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
print(json.dumps(manifest, indent=2))
