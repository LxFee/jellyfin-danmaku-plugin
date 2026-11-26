#!/usr/bin/env python3
"""Add or replace one release in the Jellyfin plugin repository manifest.

Jellyfin expects a top-level JSON array. Each plugin entry lists versions with
sourceUrl plus the MD5 checksum of the ZIP; the server verifies that checksum
before installing. Older versions are preserved so downgrades remain possible.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
METADATA_KEYS = ("guid", "name", "description", "overview", "owner", "category")


def version_key(value: str) -> tuple[int, ...]:
    try:
        return tuple(int(part) for part in value.split("."))
    except ValueError:
        return (0,)


def read_changelog(args: argparse.Namespace) -> str:
    if args.changelog_file:
        return args.changelog_file.read_text(encoding="utf-8").strip()
    return (args.changelog or "").strip()


def read_existing(path: Path | None) -> list[dict]:
    if path is None or not path.exists() or not path.read_text(encoding="utf-8").strip():
        return []
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, list):
        raise SystemExit(f"{path} is not a JSON array")
    return data


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--zip", type=Path, required=True, help="release ZIP to publish")
    parser.add_argument("--source-url", required=True, help="public URL of that ZIP")
    parser.add_argument("--existing", type=Path, help="current repository.json, if any")
    parser.add_argument("--output", type=Path, default=ROOT / "repository.json")
    parser.add_argument("--changelog", default="", help="short text shown by Jellyfin")
    parser.add_argument("--changelog-file", type=Path, help="file with the changelog text")
    parser.add_argument("--timestamp", help="UTC timestamp override, for tests")
    args = parser.parse_args()

    zip_path = args.zip.resolve()
    if not zip_path.is_file():
        raise SystemExit(f"missing ZIP: {zip_path}")

    plugin = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    version = plugin["version"]
    checksum = hashlib.md5(zip_path.read_bytes()).hexdigest()
    timestamp = args.timestamp or datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S")

    entries = read_existing(args.existing)
    entry = next((item for item in entries if item.get("guid") == plugin["guid"]), None)
    if entry is None:
        entry = {"guid": plugin["guid"], "name": plugin["name"], "versions": []}
        entries.append(entry)

    for key in METADATA_KEYS:
        entry[key] = plugin[key]
    versions = [item for item in entry.get("versions", []) if item.get("version") != version]
    versions.append(
        {
            "version": version,
            "changelog": read_changelog(args),
            "targetAbi": plugin["targetAbi"],
            "sourceUrl": args.source_url,
            "checksum": checksum,
            "timestamp": timestamp,
        }
    )
    entry["versions"] = sorted(versions, key=lambda item: version_key(item["version"]), reverse=True)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(entries, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"manifest={args.output}")
    print(f"version={version}")
    print(f"md5={checksum}")


if __name__ == "__main__":
    main()
