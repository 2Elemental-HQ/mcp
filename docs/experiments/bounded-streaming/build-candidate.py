#!/usr/bin/env python3
"""Build local-only, commit-identified packages; never publish them."""
import argparse, hashlib, json, pathlib, subprocess, re, zipfile

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
               "-p:PublishRepositoryUrl=true", "-p:DebugType=none", "-p:DebugSymbols=false", "-p:IncludeSymbols=false", f"-p:ArtifactsDir={output / 'build'}/", f"-p:PathMap={output}=/_/candidate%2C{repo}=/_"]
    with (output / f"{project}.build.log").open("w") as log:
        subprocess.run(command, cwd=repo, stdout=log, stderr=subprocess.STDOUT, check=True)
# NuGet embeds random OPC relationship ids/metadata filenames and wall-clock ZIP times.
# Canonicalize only that unsigned package container; never change assemblies or license contents.
for package in sorted([*output.glob("*.nupkg"), *output.glob("*.snupkg")]):
    with zipfile.ZipFile(package) as archive:
        if ".signature.p7s" in archive.namelist():
            raise SystemExit("Refusing to rewrite a signed package.")
        entries = {name: archive.read(name) for name in archive.namelist()}
    metadata = next((name for name in entries if name.endswith(".psmdcp")), None)
    if metadata:
        stable = "package/services/metadata/core-properties/metadata.psmdcp"
        entries[stable] = entries.pop(metadata)
        relationships = entries["_rels/.rels"].decode().replace(metadata, stable)
        # Relationship ids are local references with no inbound uses in a NuGet package.
        counter = iter(range(1, 100))
        relationships = re.sub(r'Id="[^"]+"', lambda _: f'Id="r{next(counter)}"', relationships)
        entries["_rels/.rels"] = relationships.encode()
    temporary = package.with_suffix(".tmp")
    with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, contents in sorted(entries.items()):
            info = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, contents)
    temporary.replace(package)
manifest = {"source": source, "version": version, "sdk": run(["dotnet", "--version"]),
            "upstreamBase": "3338e88e15c42cfc27465143c140d7d10f1a2707",
            "symbols": "none; upstream logging-generator document order is not deterministic",
            "packages": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(output.glob("*.nupkg"))}}
(output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
print(json.dumps(manifest, indent=2))
