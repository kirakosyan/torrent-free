"""Builds Microsoft Store and Google Play listing files from store/listing-text/*.json.

Run from the repository root: python store/build_listings.py
"""
import csv
import io
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TEXT_DIR = ROOT / "store" / "listing-text"
VERSION = "1.16"
SOURCE_URL = "https://github.com/kirakosyan/torrent-free"
MS_TITLE = "Torrent Client App"

# App locales in the order the Store listing tests expect (en-US, then ordinal order).
MS_LANGUAGES = ["en-US", "ar", "cs-CZ", "da-DK", "de-DE", "es", "fi-FI", "fr", "hi", "hu-HU", "id", "it-IT",
                "ja-JP", "ko-KR", "nb-NO", "nl-NL", "pl-PL", "pt-BR", "ro", "ru", "th", "tr", "vi", "zh-CN"]

# Play Console locale codes for each source file. The Play listing's default language is en-GB.
PLAY_LOCALES = {
    "en-US": ["en-GB"], "ar": ["ar"], "cs-CZ": ["cs-CZ"], "da-DK": ["da-DK"], "de-DE": ["de-DE"],
    "es": ["es-419", "es-ES"], "fi-FI": ["fi-FI"], "fr": ["fr-FR"], "hi": ["hi-IN"], "hu-HU": ["hu-HU"],
    "id": ["id"], "it-IT": ["it-IT"], "ja-JP": ["ja-JP"], "ko-KR": ["ko-KR"], "nb-NO": ["no-NO"], "nl-NL": ["nl-NL"],
    "pl-PL": ["pl-PL"], "pt-BR": ["pt-BR"], "ro": ["ro"], "ru": ["ru-RU"], "th": ["th"], "tr": ["tr-TR"],
    "vi": ["vi"], "zh-CN": ["zh-CN"], "uk": ["uk"],
}

LOGO_ROWS = [
    ("StoreLogo300x300", "602", "microsoft-store/assets/StoreLogo300x300.png"),
    ("StoreLogoOverride150x150", "604", "microsoft-store/assets/StoreLogo150x150.png"),
    ("StoreLogoOverride71x71", "605", "microsoft-store/assets/StoreLogo71x71.png"),
]
ASSET_TYPE = "Relative path (or URL to file in Partner Center)"

errors = []


def load(lang):
    return json.loads((TEXT_DIR / f"{lang}.json").read_text(encoding="utf-8"))


def bullets(t, keys):
    return "\n".join("• " + t[k] for k in keys)


def ms_description(t):
    return "\n\n".join([
        t["intro_ms"],
        t["h_download"] + "\n" + bullets(t, ["b_open_ms", "b_stats", "b_controls", "b_resume"]),
        t["h_control"] + "\n" + bullets(t, ["b_wifi_ms", "b_limits", "b_queue", "b_folder_ms"]),
        t["h_privacy"] + "\n" + t["p_privacy"],
        t["h_more"] + "\n" + bullets(t, ["b_libronest", "b_themes", "b_arm_ms"]),
        t["foot_ms"],
    ])


def play_description(t):
    return "\n\n".join([
        t["intro_play"],
        t["h_download"] + "\n" + bullets(t, ["b_open_play", "b_stats", "b_controls", "b_background_play", "b_resume"]),
        t["h_control"] + "\n" + bullets(t, ["b_wifi_play", "b_awake_play", "b_limits", "b_queue", "b_folder_play"]),
        t["h_privacy"] + "\n" + t["p_privacy"],
        t["h_more"] + "\n" + bullets(t, ["b_libronest", "b_themes"]),
        t["foot_play"],
        f"{t['source']}: {SOURCE_URL}",
    ])


def ms_notes(t):
    return f"{t['version_label']} {VERSION}\n{t['notes_ms']}"


def play_notes(t):
    return "\n".join("• " + line for line in t["notes_play"])


def check(lang, name, value, limit):
    if len(value) > limit:
        errors.append(f"{lang} {name}: {len(value)} > {limit}: {value[:60]}...")


def write_text(path, text, bom=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes((("﻿" if bom else "") + text).encode("utf-8"))


def main():
    texts = {lang: load(lang) for lang in PLAY_LOCALES}

    for lang, t in texts.items():
        check(lang, "play_title", t["play_title"], 30)
        check(lang, "play_short", t["play_short"], 80)
        check(lang, "play_description", play_description(t), 4000)
        check(lang, "play_notes", play_notes(t), 500)
        for i, caption in enumerate(t["captions_android"]):
            check(lang, f"captions_android[{i}]", caption, 70)
        if t.get("play_only"):
            continue
        check(lang, "ms_short", t["ms_short"], 270)
        check(lang, "ms_description", ms_description(t), 10000)
        check(lang, "ms_notes", ms_notes(t), 1500)
        if len(t["features"]) != 14:
            errors.append(f"{lang}: expected 14 features, found {len(t['features'])}")
        for i, feature in enumerate(t["features"]):
            check(lang, f"feature[{i}]", feature, 200)
        for i, caption in enumerate(t["captions_windows"]):
            check(lang, f"captions_windows[{i}]", caption, 200)

    if errors:
        print("\n".join(errors))
        sys.exit(1)

    # Microsoft Store listing import (text rows plus Store logos).
    columns = ["default"] + [lang.lower() for lang in MS_LANGUAGES]
    per_lang = lambda fn: [fn(texts["en-US"])] + [fn(texts[lang]) for lang in MS_LANGUAGES]
    rows = [["Field", "ID", "Type (Type)"] + columns,
            ["Title", "4", "Text"] + [MS_TITLE] * len(columns),
            ["Description", "2", "Text"] + per_lang(ms_description),
            ["ShortDescription", "8", "Text"] + per_lang(lambda t: t["ms_short"]),
            ["ReleaseNotes", "3", "Text"] + per_lang(ms_notes)]
    for i in range(14):
        rows.append([f"Feature{i + 1}", str(700 + i), "Text"] + per_lang(lambda t, i=i: t["features"][i]))
    for field, field_id, path in LOGO_ROWS:
        rows.append([field, field_id, ASSET_TYPE] + [path] * len(columns))
    buffer = io.StringIO()
    csv.writer(buffer, lineterminator="\r\n").writerows(rows)
    write_text(ROOT / "store" / "microsoft-store" / "listings.csv", buffer.getvalue(), bom=True)

    notes = {lang: ms_notes(texts[lang]) for lang in MS_LANGUAGES}
    write_text(ROOT / "store" / "microsoft-store" / f"release-notes-{VERSION}.json",
               json.dumps(notes, ensure_ascii=False, indent=2) + "\n")

    captions = {lang: texts[lang]["captions_windows"] for lang in MS_LANGUAGES}
    write_text(ROOT / "store" / "microsoft-store" / "screenshot-captions.json",
               json.dumps(captions, ensure_ascii=False, indent=2) + "\n")

    # Human-readable Microsoft Store copy.
    md = ["# Microsoft Store description", "",
          "Generated by `store/build_listings.py` from `store/listing-text`. Edit the JSON files, not this document.", ""]
    for lang in MS_LANGUAGES:
        t = texts[lang]
        md += [f"## {lang}", "", "### Short description", "", t["ms_short"], "",
               "### Full description", "", ms_description(t), "", "### Features", ""]
        md += [f"{i + 1}. {feature}" for i, feature in enumerate(t["features"])]
        md.append("")
    write_text(ROOT / "STORE_DESCRIPTION.md", "\n".join(md))

    # Google Play listing text and release notes.
    play_root = ROOT / "store" / "google-play"
    release = []
    for lang, locales in PLAY_LOCALES.items():
        t = texts[lang]
        for locale in locales:
            folder = play_root / "listings" / locale
            write_text(folder / "title.txt", t["play_title"] + "\n")
            write_text(folder / "short_description.txt", t["play_short"] + "\n")
            write_text(folder / "full_description.txt", play_description(t) + "\n")
            release.append(f"<{locale}>\n{play_notes(t)}\n</{locale}>")
    write_text(play_root / f"release-notes-{VERSION}.txt", "\n".join(release) + "\n")
    write_text(play_root / "screenshot-captions.json",
               json.dumps({lang: texts[lang]["captions_android"] for lang in PLAY_LOCALES}, ensure_ascii=False, indent=2) + "\n")
    print(f"Built listings for {len(MS_LANGUAGES)} Microsoft Store and {sum(map(len, PLAY_LOCALES.values()))} Google Play locales.")


if __name__ == "__main__":
    main()
