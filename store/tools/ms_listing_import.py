"""Builds a Partner Center listing import from a fresh export.

Usage: python store/tools/ms_listing_import.py <exported listingData.csv> <output.csv>

Upload the current screenshots to the en-us listing first and export again: every
language reuses the en-us DesktopScreenshot1-6 URLs with its own captions. Text comes
from store/listing-text via build_listings.py; logos and other assets in the export are
kept, and languages missing from the export are added as new columns.
"""
import csv
import io
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import build_listings as bl  # noqa: E402

LISTING_LANGUAGES = {
    "default": "en-US", "en-us": "en-US", "en": "en-US", "ar": "ar", "ar-ae": "ar", "cs-cz": "cs-CZ",
    "da-dk": "da-DK", "de-de": "de-DE", "es": "es", "es-es": "es", "fi-fi": "fi-FI", "fr": "fr", "fr-fr": "fr",
    "hi": "hi", "hi-in": "hi", "hu-hu": "hu-HU", "id": "id", "it-it": "it-IT", "ja-jp": "ja-JP", "ko-kr": "ko-KR",
    "nb-no": "nb-NO", "nl-nl": "nl-NL", "pl-pl": "pl-PL", "pt-br": "pt-BR", "ro": "ro", "ru": "ru", "ru-ru": "ru",
    "th": "th", "tr": "tr", "tr-tr": "tr", "vi": "vi", "zh-cn": "zh-CN",
}
ASSET_FIELDS = ["StoreLogo720x1080", "StoreLogo1080x1080", "StoreLogo300x300", "OverrideLogosForWin10",
                "StoreLogoOverride150x150", "StoreLogoOverride71x71", "PromoImage1920x1080"]
SCREENSHOTS = 6


def search_terms(t):
    local = re.split(r"\s+[-–&:]\s+|:\s", t["play_title"])[0].strip()
    terms = []
    for term in [local, "torrent client", "torrent downloader", "bittorrent", "magnet link", "file download",
                 "p2p download"]:
        if term.lower() not in (existing.lower() for existing in terms):
            terms.append(term[:30])
    return terms[:7]


def main(export_path, output_path):
    rows = list(csv.reader(open(export_path, encoding="utf-8-sig")))
    header = rows[0]
    for column in LISTING_LANGUAGES:
        if column not in header:
            header.append(column)
    for row in rows[1:]:
        row.extend([""] * (len(header) - len(row)))
    fields = {row[0]: row for row in rows[1:] if row and row[0]}
    en = header.index("en-us")
    shots = [fields[f"DesktopScreenshot{i}"][en] for i in range(1, SCREENSHOTS + 1)]
    if not all(shots):
        sys.exit("Upload the screenshots to the en-us listing and export again first.")

    texts = {}
    for column, source in LISTING_LANGUAGES.items():
        t = texts.setdefault(source, bl.load(source))
        col = header.index(column)
        values = {
            "Title": bl.MS_TITLE, "SortTitle": bl.MS_TITLE, "Description": bl.ms_description(t),
            "ShortDescription": t["ms_short"], "ReleaseNotes": bl.ms_notes(t),
        }
        values.update({f"Feature{i + 1}": (t["features"][i] if i < len(t["features"]) else "") for i in range(20)})
        terms = search_terms(t)
        values.update({f"SearchTerm{i + 1}": (terms[i] if i < len(terms) else "") for i in range(7)})
        for i in range(SCREENSHOTS):
            values[f"DesktopScreenshot{i + 1}"] = shots[i]
            values[f"DesktopScreenshotCaption{i + 1}"] = t["captions_windows"][i]
        for field, value in values.items():
            fields[field][col] = value
        for field in ASSET_FIELDS:
            if not fields[field][col]:
                fields[field][col] = fields[field][en]

    buffer = io.StringIO()
    csv.writer(buffer, lineterminator="\r\n").writerows(rows)
    Path(output_path).write_text(buffer.getvalue(), encoding="utf-8-sig")
    print(f"Wrote {output_path} with {len(header) - 3} language columns.")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
