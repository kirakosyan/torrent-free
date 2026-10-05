"""Shared validation and file handling for private store submission bundles."""

import hashlib
import json
from pathlib import Path
import shutil
import struct

ROOT = Path(__file__).resolve().parent.parent
PLAY = ROOT / "store" / "google-play"


def private_output(output):
    output = output.resolve()
    if output == ROOT or ROOT in output.parents:
        raise ValueError("Choose a private output directory outside the repository")
    return output


def validate_locales(expected):
    actual = {p.name for p in (PLAY / "listings").iterdir() if p.is_dir()}
    if actual != set(expected):
        raise ValueError("Play languages changed; update the store language mapping")


def read_listing(locale, fields):
    return {field: (PLAY / "listings" / locale / f"{field}.txt")
            .read_text(encoding="utf-8").strip() for field in fields}


def png_size(path):
    with path.open("rb") as stream:
        header = stream.read(24)
    if len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n" or header[12:16] != b"IHDR":
        raise ValueError(f"Invalid PNG: {path.name}")
    return struct.unpack(">II", header[16:24])


def validate_graphics(screenshots, icon, *, screenshot_count, icon_max_bytes,
                      screenshot_max_bytes=None, screenshot_max_ratio=None):
    minimum, maximum = screenshot_count
    if not minimum <= len(screenshots) <= maximum:
        raise ValueError(f"Store requires {minimum}-{maximum} screenshots")
    for path in screenshots:
        size = png_size(path)
        if (min(size) < 320 or max(size) > 3840
                or (screenshot_max_bytes is not None and path.stat().st_size > screenshot_max_bytes)
                or (screenshot_max_ratio is not None and max(size) > screenshot_max_ratio * min(size))):
            raise ValueError(f"Screenshot exceeds store limits: {path.name}")
    if png_size(icon) != (512, 512) or icon.stat().st_size > icon_max_bytes:
        raise ValueError("Icon must be 512x512 PNG and within the store size limit")


def copy_assets(output, icon, screenshots):
    output = private_output(output)
    assets = []
    for source, relative in [(icon, "icon-512.png")] + [(p, f"screenshots/{p.name}") for p in screenshots]:
        target = output / relative
        private_output(target)
        if not target.resolve().is_relative_to(output):
            raise ValueError("An output asset points outside the bundle")
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        assets.append({"file": relative, "dimensions": png_size(target),
                       "sha256": hashlib.sha256(target.read_bytes()).hexdigest()})
    return assets


def write_bundle(output, bundle, listings):
    output = private_output(output)
    files = {"listing-bundle.json": json.dumps(bundle, ensure_ascii=False, indent=2)}
    for locale, fields in listings.items():
        for field, value in fields.items():
            files[f"listings/{locale}/{field}.txt"] = value
    for relative, text in files.items():
        target = output / relative
        private_output(target)
        if not target.resolve().is_relative_to(output):
            raise ValueError("An output listing points outside the bundle")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text + "\n", encoding="utf-8")
