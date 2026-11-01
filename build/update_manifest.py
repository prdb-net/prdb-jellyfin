#!/usr/bin/env python3
"""Adds a released version to the Jellyfin plugin repository manifest (manifest.json).

Jellyfin reads the manifest from the repository URL an administrator enters, lists the plugin's
versions whose targetAbi the server satisfies, downloads sourceUrl and checks it against the MD5
checksum before installing.
"""
import argparse
import datetime
import hashlib
import json
import pathlib
import sys

PLUGIN_GUID = "74e0efe4-afea-43e4-8b19-1cf114c7755b"
TARGET_ABI = "12.0.0.0"


def md5(path: pathlib.Path) -> str:
    digest = hashlib.md5()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 16), b""):
            digest.update(block)
    return digest.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=pathlib.Path, default=pathlib.Path("manifest.json"))
    parser.add_argument("--version", required=True, help="four-part plugin version, e.g. 0.1.0.0")
    parser.add_argument("--zip", type=pathlib.Path, required=True, help="the release archive")
    parser.add_argument("--url", required=True, help="where Jellyfin downloads the archive from")
    parser.add_argument("--changelog", default="")
    parser.add_argument("--timestamp", default=None, help="ISO 8601 UTC; defaults to now")
    args = parser.parse_args()

    parts = args.version.split(".")
    if len(parts) != 4 or not all(part.isdigit() for part in parts):
        print(f"version must have four numeric parts, got {args.version!r}", file=sys.stderr)
        return 2

    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    plugin = next((entry for entry in manifest if entry.get("guid") == PLUGIN_GUID), None)
    if plugin is None:
        print(f"{args.manifest} has no entry for plugin {PLUGIN_GUID}", file=sys.stderr)
        return 2

    timestamp = args.timestamp or datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    entry = {
        "version": args.version,
        "changelog": args.changelog,
        "targetAbi": TARGET_ABI,
        "sourceUrl": args.url,
        "checksum": md5(args.zip),
        "timestamp": timestamp,
    }

    # A re-run for the same version replaces its entry rather than listing it twice.
    versions = [v for v in plugin.get("versions", []) if v.get("version") != args.version]
    versions.append(entry)
    versions.sort(key=lambda v: tuple(int(p) for p in v["version"].split(".")), reverse=True)
    plugin["versions"] = versions

    args.manifest.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
