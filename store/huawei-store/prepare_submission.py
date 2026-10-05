"""Copy the existing Play listing and graphics to a private AppGallery bundle."""

import argparse
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import (ROOT, PLAY, copy_assets, private_output, read_listing,
                     validate_graphics, validate_locales, write_bundle)
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


def prepare(output):
    output = private_output(output)
    validate_locales(LANGUAGES)
    listings = []
    for locale, language in LANGUAGES.items():
        listing = {"playLocale": locale, "huaweiLanguage": language}
        for field, limit in [("title", 30), ("short_description", 80), ("full_description", 8000)]:
            text = read_listing(locale, [field])[field]
            if not text or len(text) > limit:
                raise ValueError(f"{locale}/{field} exceeds the AppGallery character limit")
            listing[field] = text
        listings.append(listing)
    screenshots = sorted((PLAY / "screenshots" / "phone").glob("*.png"))
    icon = ROOT / "google_play_icon_512x512.png"
    validate_graphics(screenshots, icon, screenshot_count=(3, 8),
                      icon_max_bytes=2 * 1024 * 1024, screenshot_max_bytes=5 * 1024 * 1024)
    assets = copy_assets(output, icon, screenshots)
    bundle = {
        "status": "Prepared locally; this utility does not upload or submit",
        "packageName": "com.torrentfree.app.huawei", "defaultLanguage": "English (UK)",
        "listings": listings, "assets": assets,
        "notes": ["All 26 Play locales map directly to Huawei choices verified in the console.",
                  "Upload the same English screenshots and icon per language if portal validation requires them.",
                  "The app has 24 UI languages; Ukrainian is a listing translation only."]}
    write_bundle(output, bundle, {
        item["playLocale"]: {field: item[field] for field in ("title", "short_description", "full_description")}
        for item in listings})
    print(f"Prepared {len(listings)} listings and {len(assets)} assets in {output}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    prepare(parser.parse_args().output)
