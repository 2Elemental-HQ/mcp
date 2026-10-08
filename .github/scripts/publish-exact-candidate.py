"""Publish only the three owner-authorized, manifest-pinned archives; never replace a version."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.error
import urllib.request
import urllib.parse


class RegistryRedirect(urllib.request.HTTPRedirectHandler):
    """Never forward the registry credential to a download-storage hostname."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        """Allow HTTPS archive redirects while removing cross-host authentication."""
        if urllib.parse.urlsplit(newurl).scheme != "https":
            raise SystemExit("Refusing an insecure archive redirect")
        redirected = super().redirect_request(req, fp, code, msg, headers, newurl)
        if urllib.parse.urlsplit(newurl).netloc != "nuget.pkg.github.com":
            redirected.remove_header("Authorization")
        return redirected

VERSION = "2.2.0-elemental.1.gc04230cd11f8"
FEED = "https://nuget.pkg.github.com/2Elemental-HQ/index.json"
PACKAGES = {
    "ModelContextProtocol.Core": "b879dbce730dddd44c881096bfa25d18c3511c18fa0b5ede6c18e36181099a23",
    "ModelContextProtocol": "019adb05495d2a2364c5d18e20155e94ca63edfe9c53ac76e0e4cff513f0a9fd",
    "ModelContextProtocol.AspNetCore": "fefc2d62334e14292973b3e8946009be45eac39057cbcc6a1001f7c29fa08b25",
}


def main():
    """Fail closed on byte differences or access errors before publishing any archive."""
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--verify", type=Path)
    mode.add_argument("--publish", type=Path)
    args = parser.parse_args()
    directory = args.verify or args.publish
    for package_id, expected in PACKAGES.items():
        package = directory / f"{package_id}.{VERSION}.nupkg"
        actual = hashlib.sha256(package.read_bytes()).hexdigest()
        if actual != expected:
            raise SystemExit(f"Local archive mismatch: {package.name}: {actual}")
        print(f"Verified local {package.name}: {actual}", flush=True)
    if args.verify:
        return
    token = os.environ["GITHUB_TOKEN"]
    actor = os.environ["GITHUB_ACTOR"]
    authorization = base64.b64encode(f"{actor}:{token}".encode()).decode()
    opener = urllib.request.build_opener(RegistryRedirect())

    def request(url):
        """Read only from the approved registry without forwarding credentials elsewhere."""
        if not url.startswith("https://nuget.pkg.github.com/"):
            raise SystemExit("Unexpected registry URL")
        req = urllib.request.Request(url, headers={"Authorization": f"Basic {authorization}"})
        with opener.open(req, timeout=60) as response:
            return response.read()

    service = json.loads(request(FEED))
    base = next(resource["@id"] for resource in service["resources"]
                if resource["@type"].startswith("PackageBaseAddress/"))

    def remote(package_id):
        """Treat only an explicit not-found result as absence; verify all existing bytes."""
        identifier = package_id.lower()
        url = f"{base.rstrip('/')}/{identifier}/{VERSION}/{identifier}.{VERSION}.nupkg"
        try:
            data = request(url)
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return False
            raise SystemExit(f"Remote read failed for {package_id}: HTTP {error.code}") from None
        actual = hashlib.sha256(data).hexdigest()
        if actual != PACKAGES[package_id]:
            raise SystemExit(f"STOP: remote archive mismatch for {package_id}: {actual}")
        print(f"Verified remote {package_id} {VERSION}: {actual}", flush=True)
        return True

    # Check every collision before the first write, not just before each individual package.
    present = {package_id: remote(package_id) for package_id in PACKAGES}
    config = directory / "publish.config"
    config.write_text(f'<configuration><packageSources><clear/><add key="exact" value="{FEED}"/></packageSources></configuration>')
    env = os.environ.copy()
    env["NuGetPackageSourceCredentials_exact"] = f"Username={actor};Password={token};ValidAuthenticationTypes=Basic"
    for package_id in PACKAGES:
        if present[package_id]:
            continue
        # No overwrite/delete endpoint and no --skip-duplicate: a concurrent publication fails closed.
        package = directory / f"{package_id}.{VERSION}.nupkg"
        result = subprocess.run(["dotnet", "nuget", "push", str(package), "--source", "exact",
                                 "--configfile", str(config), "--api-key", token,
                                 "--no-symbols"], env=env, check=False)
        if result.returncode:
            raise SystemExit(f"Publication failed for {package_id}; preserve earlier results")
        for attempt in range(6):
            if remote(package_id):
                break
            if attempt == 5:
                raise SystemExit(f"Published {package_id}, but remote verification still returns 404")
            time.sleep(10)


if __name__ == "__main__":
    main()
