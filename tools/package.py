#!/usr/bin/env python3
"""Package a Release build into the Jellyfin plugin ZIP used by Releases.

The layout matches tools/package.ps1 and the published 1.x assets:
DLL, manifest.json, LICENSE, README.md and docs/*.md at the archive root.
Archive timestamps are fixed so the same DLL always produces the same ZIP.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ZIP_DATE = (2020, 1, 1, 0, 0, 0)


def digest(path: Path, algorithm: str) -> str:
    value = hashlib.new(algorithm)
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def package_entries(dll: Path) -> tuple[str, list[tuple[Path, str]]]:
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    version = manifest["version"]
    files = [
        (dll, dll.name),
        (ROOT / "manifest.json", "manifest.json"),
        (ROOT / "LICENSE", "LICENSE"),
        (ROOT / "README.md", "README.md"),
    ]
    files += [(path, path.relative_to(ROOT).as_posix()) for path in sorted((ROOT / "docs").glob("*.md"))]
    missing = [str(source) for source, _ in files if not source.is_file()]
    if missing:
        raise SystemExit("missing package input: " + ", ".join(missing))
    return version, files


def write_zip(target: Path, files: list[tuple[Path, str]]) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
        for source, name in files:
            info = zipfile.ZipInfo(name, ZIP_DATE)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            archive.writestr(info, source.read_bytes())


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--dll",
        type=Path,
        default=ROOT / "plugin/bin/Release/net10.0/Jellyfin.Plugin.Danmaku.dll",
        help="path to the Release build of Jellyfin.Plugin.Danmaku.dll",
    )
    parser.add_argument("--output-dir", type=Path, default=ROOT / "artifacts")
    args = parser.parse_args()

    args.dll = args.dll.resolve()
    args.output_dir = args.output_dir.resolve()
    if not args.dll.is_file():
        raise SystemExit(f"missing Release build: {args.dll} (run dotnet build first)")
    version, files = package_entries(args.dll)
    target = args.output_dir / f"jellyfin-danmaku-{version}.zip"
    write_zip(target, files)
    sha256 = digest(target, "sha256")
    md5 = digest(target, "md5")
    checksums = args.output_dir / "SHA256SUMS.txt"
    checksums.write_text(f"{sha256}  {target.name}\n", encoding="utf-8")
    try:
        shown = target.relative_to(ROOT)
    except ValueError:
        shown = target
    print(f"zip={shown}")
    print(f"sha256={sha256}")
    print(f"md5={md5}")


if __name__ == "__main__":
    main()
