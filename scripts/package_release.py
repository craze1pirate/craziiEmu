# Copyright (C) 2026 CraziiEmu Project
# SPDX-License-Identifier: GPL-2.0-or-later

#!/usr/bin/env python3
import os
import sys
import shutil
import argparse
import subprocess
import zipfile

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DEFAULT_TARGET_DIR = r"D:\downloads\craziimerge"
DEFAULT_ZIP_PATH = r"D:\downloads\craziimerge.zip"
SRC_UI_CSPROJ = os.path.join(REPO_ROOT, "src", "CraziiEmu.UI", "CraziiEmu.UI.csproj")

def run_cmd(cmd, cwd=REPO_ROOT):
    print(f"Running: {' '.join(cmd) if isinstance(cmd, list) else cmd}")
    res = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True)
    if res.returncode != 0:
        print(f"STDOUT: {res.stdout}")
        print(f"STDERR: {res.stderr}")
        raise RuntimeError(f"Command failed with code {res.returncode}")
    print(res.stdout)

def main():
    parser = argparse.ArgumentParser(description="Package clean local release for CraziiEmu")
    parser.add_argument("--out-dir", default=DEFAULT_TARGET_DIR, help="Destination directory for release files")
    parser.add_argument("--zip-path", default=DEFAULT_ZIP_PATH, help="Path for release zip archive")
    args = parser.parse_args()

    target_dir = os.path.abspath(args.out_dir)
    zip_path = os.path.abspath(args.zip_path)

    print(f"=== Creating clean release at {target_dir} ===")

    if os.path.exists(target_dir):
        shutil.rmtree(target_dir)
    os.makedirs(target_dir, exist_ok=True)

    # Publish CraziiEmu.UI directly into target_dir
    run_cmd(f'dotnet publish "{SRC_UI_CSPROJ}" -c Release -r win-x64 --self-contained true -p:PublishDir="{target_dir}"')

    # Allowed exact release files
    allowed_files = {
        'av_libglesv2.dll',
        'CraziiEmu.exe',
        'glfw3.dll',
        'libHarfBuzzSharp.dll',
        'libSkiaSharp.dll'
    }

    # Clean any extra files or subdirectories
    for item in os.listdir(target_dir):
        full_path = os.path.join(target_dir, item)
        if os.path.isdir(full_path):
            shutil.rmtree(full_path)
        elif item not in allowed_files:
            os.remove(full_path)

    # Create ZIP archive
    if os.path.exists(zip_path):
        os.remove(zip_path)

    with zipfile.ZipFile(zip_path, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zipf:
        for file in sorted(allowed_files):
            full_path = os.path.join(target_dir, file)
            zipf.write(full_path, file)

    print("\n=== Release Contents ===")
    for f in sorted(os.listdir(target_dir)):
        p = os.path.join(target_dir, f)
        print(f"  {f} ({os.path.getsize(p):,} bytes)")

    print(f"\nCreated ZIP: {zip_path} ({os.path.getsize(zip_path):,} bytes)")

if __name__ == "__main__":
    main()
