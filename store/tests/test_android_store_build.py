"""Evaluate the real MAUI project; run after installing the Android workload."""

import json
from pathlib import Path
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "src/TorrentFree/TorrentFree.csproj"


class AndroidStoreBuildTests(unittest.TestCase):
    def msbuild(self, *, configuration="Release", target="ValidateAndroidStore", **properties):
        return subprocess.run([
            "dotnet", "msbuild", str(PROJECT), "-nologo", f"-target:{target}",
            "-property:TargetFramework=net10.0-android",
            "-property:AppTargetFramework=net10.0-android", f"-property:Configuration={configuration}",
            "-getProperty:AndroidStore,ApplicationId,AndroidPackageFormat,AndroidPackageFormats,RuntimeIdentifiers,ApplicationTitle,SupportedOSPlatformVersion",
            *[f"-property:{key}={value}" for key, value in properties.items()],
        ], cwd=ROOT, capture_output=True, text=True, timeout=90)

    def test_release_identities_and_formats(self):
        for store, package, package_format in [
            (None, "com.torrentfree.app", "aab"),
            ("Play", "com.torrentfree.app", "aab"),
            ("Galaxy", "com.torrentfree.app.galaxy", "apk"),
            ("Huawei", "com.torrentfree.app.huawei", "apk"),
        ]:
            with self.subTest(store=store):
                result = self.msbuild(**({"AndroidStore": store} if store else {}))
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                values = json.loads(result.stdout)["Properties"]
                self.assertEqual(values["AndroidStore"], store or "Play")
                self.assertEqual(values["ApplicationId"], package)
                self.assertEqual(values["AndroidPackageFormat"], package_format)
                if store in ("Galaxy", "Huawei"):
                    self.assertEqual(values["AndroidPackageFormats"], "apk")
                else:
                    self.assertIn("aab", values["AndroidPackageFormats"].split(";"))

    def test_all_stores_retain_android_6_compatibility(self):
        for configuration in ("Debug", "Release"):
            for store in (None, "Play", "Galaxy", "Huawei"):
                with self.subTest(configuration=configuration, store=store):
                    result = self.msbuild(configuration=configuration, **({"AndroidStore": store} if store else {}))
                    self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                    minimum = json.loads(result.stdout)["Properties"]["SupportedOSPlatformVersion"]
                    self.assertEqual(minimum, "23.0")

    def test_huawei_has_only_arm64_in_debug_and_release(self):
        for configuration in ("Debug", "Release"):
            with self.subTest(configuration=configuration):
                result = self.msbuild(configuration=configuration, AndroidStore="Huawei")
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertEqual(json.loads(result.stdout)["Properties"]["RuntimeIdentifiers"], "android-arm64")

    def test_invalid_selection_fails_before_restore_and_build(self):
        for target in ("CollectPackageReferences", "PrepareForBuild"):
            for store in ("UnknownStore", "Galaxy+Huawei"):
                with self.subTest(target=target, store=store):
                    result = self.msbuild(target=target, AndroidStore=store)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn("TFSTORE001", result.stdout + result.stderr)

    def test_legacy_flags_fail_instead_of_silently_building_play(self):
        for properties in ({"HuaweiStoreBuild": "true"}, {"GalaxyStoreBuild": "true"},
                           {"HuaweiStoreBuild": "true", "GalaxyStoreBuild": "true"}):
            with self.subTest(properties=properties):
                result = self.msbuild(target="CollectPackageReferences", **properties)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("TFSTORE002", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
