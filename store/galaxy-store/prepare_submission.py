"""Copy public Play assets and prepare Galaxy listing text in a local output folder.

Usage: python store/galaxy-store/prepare_submission.py --output <folder>
This creates a preparation bundle, not an API request or a submitted listing.
"""
import argparse
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import (ROOT, PLAY, copy_assets, private_output, read_listing,
                     validate_graphics, validate_locales, write_bundle)
# Samsung's listing language choices are not the same as Play's locales.
LANGUAGES = {
    "ENG": "en-GB", "ARA": "ar", "CES": "cs-CZ", "DAN": "da-DK",
    "DEU": "de-DE", "SPA": "es-ES", "FIN": "fi-FI", "FRA": "fr-FR",
    "HUN": "hu-HU", "IND": "id", "ITA": "it-IT", "JPN": "ja-JP",
    "KOR": "ko-KR", "NOR": "no-NO", "NLD": "nl-NL", "POL": "pl-PL",
    "POR": "pt-BR", "RON": "ro", "RUS": "ru-RU", "THA": "th",
    "TUR": "tr-TR", "UKR": "uk", "VIE": "vi", "ZHO": "zh-CN",
}


def prepare(output):
    output = private_output(output)
    validate_locales(set(LANGUAGES.values()) | {"hi-IN", "es-419"})
    listings = []
    adjustments = []
    for code, locale in LANGUAGES.items():
        source_listing = read_listing(locale, ["title", "full_description"])
        title = source_listing["title"]
        description = source_listing["full_description"]
        if len(description.encode("utf-8")) > 4000:
            # Preserve complete prose; the source URL is also supplied separately.
            body, separator, source = description.rpartition("\n\n")
            if not separator or "https://github.com/kirakosyan/torrent-free" not in source:
                raise ValueError(f"Review {locale}: description exceeds Samsung's byte limit")
            description = body
            adjustments.append(f"{locale}: source link moved to the open-source URL field")
        if not title or not description or len(title.encode("utf-8")) > 100 or len(description.encode("utf-8")) > 4000:
            raise ValueError(f"Review {locale}: Samsung text byte limit exceeded")
        listings.append({"languagecode": code, "playLocale": locale,
                         "appTitle": title, "description": description})

    if read_listing("es-419", ["full_description"]) != read_listing("es-ES", ["full_description"]):
        raise ValueError("Spanish variants differ; review before mapping to one Samsung listing")

    screenshots = sorted((PLAY / "screenshots" / "phone").glob("*.png"))
    icon = ROOT / "google_play_icon_512x512.png"
    validate_graphics(screenshots, icon, screenshot_count=(4, 8),
                      icon_max_bytes=1024 * 1024, screenshot_max_ratio=2)
    assets = copy_assets(output, icon, screenshots)

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
    write_bundle(output, bundle, {
        item["languagecode"]: {"title": item["appTitle"], "description": item["description"]}
        for item in listings})
    print(f"Prepared {len(listings)} listings, {len(screenshots)} screenshots and one icon in {output}")
    for adjustment in adjustments:
        print(adjustment)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    prepare(parser.parse_args().output.resolve())
