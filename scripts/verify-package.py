"""Verify NuGet native paths, provenance, licenses and the absence of a runtime Npgsql dependency."""
import argparse
import subprocess
import sys
from pathlib import Path
from xml.etree import ElementTree
from zipfile import ZipFile

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("package", type=Path)
args = parser.parse_args()
subprocess.run([sys.executable, str(root / "scripts/verify-upstream.py")], check=True)
with ZipFile(args.package) as package:
    names = package.namelist()
    metadata = ElementTree.fromstring(package.read(next(name for name in names if name.endswith(".nuspec"))))
    dependencies = [node.attrib["id"] for node in metadata.iter() if node.tag.endswith("}dependency")]
    if "Npgsql" in dependencies:
        raise SystemExit("Npgsql must remain a test-only dependency.")
    assets = [name for name in names if name.startswith("runtimes/")]
    if not assets:
        raise SystemExit("The core package contains no native library.")
    for asset in assets:
        parts = asset.split("/")
        if len(parts) != 4 or parts[2] != "native":
            raise SystemExit(f"Invalid NuGet runtime asset path: {asset}")
        if package.read(asset) != (root / asset).read_bytes():
            raise SystemExit(f"Packaged native asset differs from build output: {asset}")
    expected = {
        "licenses/LICENSE": "native/upstream/LICENSE",
        "native/upstream.json": "native/upstream.json",
        "native/include/stampeded.h": "native/upstream/crates/stampeded-ffi/include/stampeded.h",
        "schema/schema.sql": "native/upstream/crates/stampeded/schema/schema.sql",
    }
    for destination, source in expected.items():
        if package.read(destination) != (root / source).read_bytes():
            raise SystemExit(f"Packaged upstream file differs: {destination}")
print(f"Verified {args.package.name}: {len(assets)} native asset(s), matching upstream files, no Npgsql dependency.")
