"""Copy public Play assets and prepare Galaxy listing text in a local output folder.

Usage: python store/galaxy-store/prepare_submission.py --output <folder>
This creates a preparation bundle, not an API request or a submitted listing.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct

ROOT = Path(__file__).resolve().parents[2]
PLAY = ROOT / "store" / "google-play"
# Samsung's listing language choices are not the same as Play's locales.
LANGUAGES = {
    "ENG": "en-GB", "ARA": "ar", "CES": "cs-CZ", "DAN": "da-DK",
    "DEU": "de-DE", "SPA": "es-ES", "FIN": "fi-FI", "FRA": "fr-FR",
    "HUN": "hu-HU", "IND": "id", "ITA": "it-IT", "JPN": "ja-JP",
    "KOR": "ko-KR", "NOR": "no-NO", "NLD": "nl-NL", "POL": "pl-PL",
    "POR": "pt-BR", "RON": "ro", "RUS": "ru-RU", "THA": "th",
    "TUR": "tr-TR", "UKR": "uk", "VIE": "vi", "ZHO": "zh-CN",
}


def read_text(locale, name):
    return (PLAY / "listings" / locale / name).read_text(encoding="utf-8").strip()


def png_size(path):
    header = path.read_bytes()[:24]
    if header[:8] != b"\x89PNG\r\n\x1a\n" or header[12:16] != b"IHDR":
        raise ValueError(f"Not a PNG: {path.name}")
    return struct.unpack(">II", header[16:24])


def prepare(output):
    listings = []
    adjustments = []
    for code, locale in LANGUAGES.items():
        title = read_text(locale, "title.txt")
        description = read_text(locale, "full_description.txt")
        if len(description.encode("utf-8")) > 4000:
            # Preserve complete prose; the source URL is also supplied separately.
            body, separator, source = description.rpartition("\n\n")
            if not separator or "https://github.com/kirakosyan/torrent-free" not in source:
                raise ValueError(f"Review {locale}: description exceeds Samsung's byte limit")
            description = body
            adjustments.append(f"{locale}: source link moved to the open-source URL field")
        if len(title.encode("utf-8")) > 100 or len(description.encode("utf-8")) > 4000:
            raise ValueError(f"Review {locale}: Samsung text byte limit exceeded")
        listings.append({"languagecode": code, "playLocale": locale,
                         "appTitle": title, "description": description})

    if read_text("es-419", "full_description.txt") != read_text("es-ES", "full_description.txt"):
        raise ValueError("Spanish variants differ; review before mapping to one Samsung listing")

    screenshots = sorted((PLAY / "screenshots" / "phone").glob("*.png"))
    if not 4 <= len(screenshots) <= 8:
        raise ValueError("Samsung requires 4–8 screenshots")
    for path in screenshots:
        width, height = png_size(path)
        if min(width, height) < 320 or max(width, height) > 3840 or max(width, height) > 2 * min(width, height):
            raise ValueError(f"Screenshot dimensions unsupported: {path.name}")
    icon = ROOT / "google_play_icon_512x512.png"
    if png_size(icon) != (512, 512) or icon.stat().st_size > 1024 * 1024:
        raise ValueError("Samsung icon must be 512x512 PNG and at most 1024 KB")

    output.mkdir(parents=True, exist_ok=True)
    screenshot_output = output / "screenshots"
    screenshot_output.mkdir(exist_ok=True)
    assets = []
    for path, target in [(icon, output / "icon-512.png")] + [(p, screenshot_output / p.name) for p in screenshots]:
        if path.resolve() == target.resolve():
            raise ValueError("Output must not overwrite source assets")
        shutil.copy2(path, target)
        assets.append({"file": target.relative_to(output).as_posix(), "dimensions": png_size(target),
                       "sha256": hashlib.sha256(target.read_bytes()).hexdigest()})

    bundle = {
        "status": "Prepared locally; not registered, submitted, or published",
        "packageName": "com.torrentfree.app.galaxy",
        "defaultLanguageCode": "ENG",
        "shortDescription": "Torrent downloads, no ads",
        "openSourceURL": "https://github.com/kirakosyan/torrent-free/blob/main/LICENSE",
        "listings": listings,
        "supportedLanguages": sorted((set(LANGUAGES) - {"UKR"}) | {"HIN"}),
        "languageNotes": [
            "Play's 26 listing locales map to 24 Galaxy listing languages.",
            "Hindi is an app UI language, but is absent from Samsung's listing language choices; use English fallback.",
            "es-419 and es-ES share the same text and map to Samsung's single Spanish listing.",
            "Ukrainian listing is reused from Play; the app does not have Ukrainian UI resources.",
        ],
        "textAdjustments": adjustments,
        "assets": assets,
    }
    (output / "listing-bundle.json").write_text(json.dumps(bundle, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for listing in listings:
        folder = output / "listings" / listing["languagecode"]
        folder.mkdir(parents=True, exist_ok=True)
        (folder / "title.txt").write_text(listing["appTitle"] + "\n", encoding="utf-8")
        (folder / "description.txt").write_text(listing["description"] + "\n", encoding="utf-8")
    print(f"Prepared {len(listings)} listings, {len(screenshots)} screenshots and one icon in {output}")
    for adjustment in adjustments:
        print(adjustment)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    prepare(parser.parse_args().output.resolve())
