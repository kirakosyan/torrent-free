import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

STORE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(STORE))
from _common import ROOT, PLAY, png_size, validate_graphics, validate_locales


def load_preparer(store):
    spec = importlib.util.spec_from_file_location(store, STORE / store / "prepare_submission.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class SubmissionTests(unittest.TestCase):
    def test_store_bundles_preserve_languages_text_and_graphics(self):
        for name, count in [("huawei-store", 26), ("galaxy-store", 24)]:
            with self.subTest(store=name), tempfile.TemporaryDirectory() as directory:
                output = Path(directory)
                load_preparer(name).prepare(output)
                bundle = json.loads((output / "listing-bundle.json").read_text(encoding="utf-8"))
                self.assertEqual(len(bundle["listings"]), count)
                self.assertEqual(len(bundle["assets"]), 6)
                for asset in bundle["assets"]:
                    source = (ROOT / "google_play_icon_512x512.png" if asset["file"] == "icon-512.png"
                              else PLAY / "screenshots/phone" / Path(asset["file"]).name)
                    self.assertEqual((output / asset["file"]).read_bytes(), source.read_bytes())
                    self.assertEqual(asset["sha256"], hashlib.sha256(source.read_bytes()).hexdigest())
                for listing in bundle["listings"]:
                    source = PLAY / "listings" / listing["playLocale"]
                    if name == "huawei-store":
                        for field in ("title", "short_description", "full_description"):
                            self.assertEqual(listing[field], (source / f"{field}.txt").read_text(encoding="utf-8").strip())
                    else:
                        self.assertEqual(listing["appTitle"], (source / "title.txt").read_text(encoding="utf-8").strip())
                        self.assertTrue((source / "full_description.txt").read_text(encoding="utf-8").startswith(listing["description"]))
                        self.assertLessEqual(len(listing["description"].encode("utf-8")), 4000)
                self.assertEqual(len(list((output / "listings").iterdir())), count)

    def test_both_scripts_reject_repository_outputs_before_writing(self):
        for name in ("huawei-store", "galaxy-store"):
            for output in (ROOT, ROOT / "artifacts" / "submission-guard-test"):
                with self.subTest(store=name, output=output):
                    with self.assertRaisesRegex(ValueError, "outside the repository"):
                        load_preparer(name).prepare(output)

    def test_locale_changes_require_explicit_mapping(self):
        with self.assertRaisesRegex(ValueError, "language mapping"):
            validate_locales({"en-GB"})

    def test_invalid_and_truncated_png_headers_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "bad.png"
            for content in (b"not a PNG", b"\x89PNG\r\n\x1a\n" + b"\0\0\0\rIHDR"):
                path.write_bytes(content)
                with self.assertRaises(ValueError):
                    png_size(path)

    def test_store_specific_graphics_limits(self):
        with tempfile.TemporaryDirectory() as directory:
            def png(name, width, height):
                path = Path(directory) / name
                path.write_bytes(b"\x89PNG\r\n\x1a\n\0\0\0\rIHDR" + struct.pack(">II", width, height))
                return path

            icon = png("icon.png", 512, 512)
            screenshot = png("phone.png", 1000, 2500)
            options = dict(screenshot_count=(3, 8), icon_max_bytes=1024)
            validate_graphics([screenshot] * 3, icon, **options)
            for limits in ({"screenshot_max_ratio": 2}, {"screenshot_max_bytes": 23}):
                with self.assertRaises(ValueError):
                    validate_graphics([screenshot] * 3, icon, **options, **limits)
            with self.assertRaises(ValueError):
                validate_graphics([screenshot] * 2, icon, **options)
            with self.assertRaises(ValueError):
                validate_graphics([screenshot] * 3, icon, screenshot_count=(3, 8), icon_max_bytes=23)
            too_small = png("small.png", 319, 500)
            with self.assertRaises(ValueError):
                validate_graphics([too_small] * 3, icon, **options)


if __name__ == "__main__":
    unittest.main()
