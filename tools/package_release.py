"""Build HarmonyIntegration and package the x64/Linux/MacOS release zips.

The third-party release payload (BepInEx, doorstop, unstripped Unity/Mono
libraries) is not kept in this repo. It lives in a template folder per
platform, by default ..\\release-templates\\IM-HarmonyIntegration beside this
repo:

    <templates>/x64/...     -> IM-HarmonyIntegration.x64.zip
    <templates>/Linux/...   -> IM-HarmonyIntegration.Linux.zip
    <templates>/MacOS/...   -> IM-HarmonyIntegration.MacOS.zip

Each template is zipped as-is with the freshly built HarmonyIntegration.dll
placed at BepInEx/plugins/. Output goes to dist/<version>/ (gitignored).

With --upload (and --notes-file), also creates the GitHub release
"IM-HarmonyIntegration <version>" (tag <version> on HEAD) with the three zips,
using the gh CLI. HEAD must already be pushed and source/ must have no
uncommitted changes. Afterwards, mods that check for a minimum IM-HI version may
need updating to ask for the new one.

Usage:
    python tools/package_release.py [--templates DIR] [--out DIR] [--no-build]
                                    [--upload --notes-file FILE]
"""

import argparse
import os
import re
import shutil
import subprocess
import sys
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSPROJ = os.path.join(REPO, "source", "HarmonyIntegration.csproj")
BUILT_DLL = os.path.join(REPO, "source", "bin", "Release", "net46", "HarmonyIntegration.dll")
PLUGIN_ENTRY = "BepInEx/plugins/HarmonyIntegration.dll"
DEFAULT_TEMPLATES = os.path.join(os.path.dirname(REPO), "release-templates", "IM-HarmonyIntegration")
GH_FALLBACK = r"C:\Program Files\GitHub CLI\gh.exe"

# Linux/MacOS zips carry Unix permissions so run_bepinex.sh is executable after extraction.
PLATFORMS = {"x64": False, "Linux": True, "MacOS": True}


def read_version():
    with open(CSPROJ, encoding="utf-8-sig") as f:
        match = re.search(r"<Version>([^<]+)</Version>", f.read())
    if not match:
        sys.exit("No <Version> found in " + CSPROJ)
    return match.group(1).strip()


def add_entry(zf, arcname, src, unix):
    is_dir = src is None or os.path.isdir(src)
    info = zipfile.ZipInfo(arcname + ("/" if is_dir else ""))
    if not is_dir:
        info.compress_type = zipfile.ZIP_DEFLATED
    if unix:
        info.create_system = 3
        mode = 0o755 if is_dir or arcname.endswith(".sh") else 0o644
        info.external_attr = ((0o040000 if is_dir else 0o100000) | mode) << 16
    if is_dir:
        info.external_attr |= 0x10
        zf.writestr(info, b"")
    else:
        assert src is not None
        with open(src, "rb") as f:
            zf.writestr(info, f.read())


def package(platform, unix, template_dir, dll, out_path):
    if not os.path.isdir(template_dir):
        sys.exit("Missing template folder: " + template_dir)
    with zipfile.ZipFile(out_path, "w") as zf:
        for root, dirs, files in os.walk(template_dir):
            dirs.sort()
            rel_root = os.path.relpath(root, template_dir).replace(os.sep, "/")
            if rel_root != ".":
                add_entry(zf, rel_root, None, unix)
            for name in sorted(files):
                arcname = name if rel_root == "." else rel_root + "/" + name
                if arcname == PLUGIN_ENTRY:
                    continue  # never ship a stale plugin from the template
                add_entry(zf, arcname, os.path.join(root, name), unix)
        add_entry(zf, PLUGIN_ENTRY, dll, unix)
    print("  {:<38} {:>10,} bytes".format(os.path.basename(out_path), os.path.getsize(out_path)))


def git(*args):
    return subprocess.run(["git", "-C", REPO] + list(args), check=True,
                          capture_output=True, text=True).stdout.strip()


def find_gh():
    gh = shutil.which("gh") or (GH_FALLBACK if os.path.isfile(GH_FALLBACK) else None)
    if not gh:
        sys.exit("gh CLI not found; install it or put it on PATH.")
    return gh


def check_upload(gh, version):
    """Everything that could stop the upload, checked before building."""
    if subprocess.run([gh, "release", "view", version], cwd=REPO, capture_output=True).returncode == 0:
        sys.exit("A release tagged {} already exists; bump <Version> first.".format(version))
    if git("status", "--porcelain", "--", "source"):
        sys.exit("source/ has uncommitted changes; commit and push them before uploading.")
    git("fetch", "--quiet", "origin")
    if not git("branch", "-r", "--contains", "HEAD"):
        sys.exit("HEAD is not pushed to origin; push it before uploading.")
    return git("rev-parse", "HEAD")


def main():
    parser = argparse.ArgumentParser(description=(__doc__ or "").partition("\n")[0])
    parser.add_argument("--templates", default=DEFAULT_TEMPLATES)
    parser.add_argument("--out", default=None, help="default: dist/<version>")
    parser.add_argument("--no-build", action="store_true", help="package the existing Release build")
    parser.add_argument("--upload", action="store_true", help="create the GitHub release with the zips")
    parser.add_argument("--notes-file", help="release notes (Markdown) for --upload")
    args = parser.parse_args()

    version = read_version()
    if args.upload:
        if not args.notes_file or not os.path.isfile(args.notes_file):
            sys.exit("--upload needs --notes-file with the release notes.")
        gh = find_gh()
        commit = check_upload(gh, version)
    if not args.no_build:
        subprocess.run(["dotnet", "build", CSPROJ, "-c", "Release", "-nologo", "--no-incremental"], check=True)
    if not os.path.isfile(BUILT_DLL):
        sys.exit("Built DLL not found: " + BUILT_DLL)

    out_dir = args.out or os.path.join(REPO, "dist", version)
    os.makedirs(out_dir, exist_ok=True)
    print("Packaging HarmonyIntegration {} into {}".format(version, out_dir))
    zips = []
    for platform, unix in PLATFORMS.items():
        zips.append(os.path.join(out_dir, "IM-HarmonyIntegration.{}.zip".format(platform)))
        package(platform, unix, os.path.join(args.templates, platform), BUILT_DLL, zips[-1])

    if not args.upload:
        return
    print("Creating GitHub release {} on {}".format(version, commit[:7]))
    subprocess.run([gh, "release", "create", version, *zips, "--target", commit,
                    "--title", "IM-HarmonyIntegration " + version, "--notes-file", args.notes_file],
                   cwd=REPO, check=True)
    print("Released. Mods that check for a minimum IM-HI version may need updating to ask for {}.".format(version))


if __name__ == "__main__":
    main()
