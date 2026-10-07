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

Usage:
    python tools/package_release.py [--templates DIR] [--out DIR] [--no-build]
"""

import argparse
import os
import re
import subprocess
import sys
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSPROJ = os.path.join(REPO, "source", "HarmonyIntegration.csproj")
BUILT_DLL = os.path.join(REPO, "source", "bin", "Release", "net46", "HarmonyIntegration.dll")
PLUGIN_ENTRY = "BepInEx/plugins/HarmonyIntegration.dll"
DEFAULT_TEMPLATES = os.path.join(os.path.dirname(REPO), "release-templates", "IM-HarmonyIntegration")

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


def main():
    parser = argparse.ArgumentParser(description=(__doc__ or "").partition("\n")[0])
    parser.add_argument("--templates", default=DEFAULT_TEMPLATES)
    parser.add_argument("--out", default=None, help="default: dist/<version>")
    parser.add_argument("--no-build", action="store_true", help="package the existing Release build")
    args = parser.parse_args()

    version = read_version()
    if not args.no_build:
        subprocess.run(["dotnet", "build", CSPROJ, "-c", "Release", "-nologo", "--no-incremental"], check=True)
    if not os.path.isfile(BUILT_DLL):
        sys.exit("Built DLL not found: " + BUILT_DLL)

    out_dir = args.out or os.path.join(REPO, "dist", version)
    os.makedirs(out_dir, exist_ok=True)
    print("Packaging HarmonyIntegration {} into {}".format(version, out_dir))
    for platform, unix in PLATFORMS.items():
        package(platform, unix, os.path.join(args.templates, platform), BUILT_DLL,
                os.path.join(out_dir, "IM-HarmonyIntegration.{}.zip".format(platform)))


if __name__ == "__main__":
    main()
