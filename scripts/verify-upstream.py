"""Verify the initialized upstream submodule against its gitlink and package provenance."""
import json
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parents[1]
source = root / "native/upstream"
manifest = json.loads((root / "native/upstream.json").read_text())


def git(*arguments, cwd=root):
    return subprocess.check_output(["git", *arguments], cwd=cwd, text=True).strip()


if not (source / ".git").exists():
    raise SystemExit("Upstream submodule is not initialized. Run: git submodule update --init --recursive")

entry = git("ls-files", "--stage", "--", "native/upstream").split()
if len(entry) != 4 or entry[0] != "160000" or entry[2] != "0":
    raise SystemExit("native/upstream must be a Git submodule with no merge conflicts.")
if entry[1] != manifest["commit"]:
    raise SystemExit("The upstream gitlink differs from native/upstream.json. Update them together.")
if git("config", "--file", ".gitmodules", "--get", "submodule.native/upstream.url") != manifest["repository"]:
    raise SystemExit("The upstream submodule URL differs from native/upstream.json.")
if git("rev-parse", "HEAD", cwd=source) != manifest["commit"]:
    raise SystemExit("Upstream is at the wrong revision. Run: git submodule update --init --recursive")
if git("status", "--porcelain", "--untracked-files=normal", "--ignore-submodules=none", cwd=source):
    raise SystemExit("Upstream has local changes. Build from the unmodified pinned submodule.")
print(f"Verified upstream submodule at {manifest['commit']}.")
