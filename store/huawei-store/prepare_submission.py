"""Copy the existing Play listing and graphics to a private AppGallery bundle."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct

ROOT = Path(__file__).resolve().parents[2]
PLAY = ROOT / "store" / "google-play"
LANGUAGES = {
    "en-GB": "English (UK)", "ar": "Arabic", "zh-CN": "Chinese (PRC)",
    "cs-CZ": "Czech", "da-DK": "Danish", "nl-NL": "Dutch", "fi-FI": "Finnish",
    "fr-FR": "French (France)", "de-DE": "German", "hi-IN": "Hindi",
    "hu-HU": "Hungarian", "id": "Indonesian", "it-IT": "Italian",
    "ja-JP": "Japanese", "ko-KR": "Korean (South Korea)", "no-NO": "Norwegian",
    "pl-PL": "Polish", "pt-BR": "Portuguese (Brazil)", "ro": "Romanian",
    "ru-RU": "Russian", "es-419": "Spanish (Latin America)", "es-ES": "Spanish (Spain)",
    "th": "Thai", "tr-TR": "Turkish", "uk": "Ukrainian", "vi": "Vietnamese",
}


def png_size(path):
    with path.open("rb") as stream:
        header = stream.read(24)
    if header[:8] != b"\x89PNG\r\n\x1a\n" or header[12:16] != b"IHDR":
        raise ValueError(f"Invalid PNG: {path.name}")
    return struct.unpack(">II", header[16:24])


def prepare(output):
    output = output.resolve()
    if output == ROOT or ROOT in output.parents:
        raise ValueError("Choose a private output directory outside the repository")
    locales = {p.name for p in (PLAY / "listings").iterdir() if p.is_dir()}
    if locales != set(LANGUAGES):
        raise ValueError("Play languages changed; update the AppGallery language mapping")
    listings = []
    for locale, language in LANGUAGES.items():
        listing = {"playLocale": locale, "huaweiLanguage": language}
        for field, limit in [("title", 30), ("short_description", 80), ("full_description", 8000)]:
            text = (PLAY / "listings" / locale / f"{field}.txt").read_text(encoding="utf-8").strip()
            if not text or len(text) > limit:
                raise ValueError(f"{locale}/{field} exceeds the AppGallery character limit")
            listing[field] = text
        listings.append(listing)
    screenshots = sorted((PLAY / "screenshots" / "phone").glob("*.png"))
    if not 3 <= len(screenshots) <= 8:
        raise ValueError("AppGallery requires 3-8 screenshots")
    for path in screenshots:
        size = png_size(path)
        if min(size) < 320 or max(size) > 3840 or path.stat().st_size > 5 * 1024 * 1024:
            raise ValueError(f"Screenshot exceeds AppGallery limits: {path.name}")
    icon = ROOT / "google_play_icon_512x512.png"
    if png_size(icon) != (512, 512) or icon.stat().st_size > 2 * 1024 * 1024:
        raise ValueError("Icon exceeds AppGallery limits")
    output.mkdir(parents=True, exist_ok=True)
    assets = []
    for source, relative in [(icon, "icon-512.png")] + [(p, f"screenshots/{p.name}") for p in screenshots]:
        target = output / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        assets.append({"file": relative, "dimensions": png_size(target),
                       "sha256": hashlib.sha256(target.read_bytes()).hexdigest()})
    for listing in listings:
        target = output / "listings" / listing["playLocale"]
        target.mkdir(parents=True, exist_ok=True)
        for field in ("title", "short_description", "full_description"):
            (target / f"{field}.txt").write_text(listing[field] + "\n", encoding="utf-8")
    bundle = {
        "status": "Prepared locally; this utility does not upload or submit",
        "packageName": "com.torrentfree.app.huawei", "defaultLanguage": "English (UK)",
        "listings": listings, "assets": assets,
        "notes": ["All 26 Play locales map directly to Huawei choices verified in the console.",
                  "Translations inherit the default English screenshots and icon.",
                  "The app has 24 UI languages; Ukrainian is a listing translation only."]}
    (output / "listing-bundle.json").write_text(json.dumps(bundle, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared {len(listings)} listings and {len(assets)} assets in {output}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    prepare(parser.parse_args().output)
