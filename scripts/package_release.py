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
    allowed_dirs = {'plugins'}

    # Clean any extra files or subdirectories
    for item in os.listdir(target_dir):
        full_path = os.path.join(target_dir, item)
        if os.path.isdir(full_path):
            if item not in allowed_dirs:
                shutil.rmtree(full_path)
            else:
                for sub in os.listdir(full_path):
                    if sub.endswith('.pdb'):
                        os.remove(os.path.join(full_path, sub))
        elif item not in allowed_files:
            os.remove(full_path)

    # Ensure plugins folder has native FFmpeg binaries
    plugins_target = os.path.join(target_dir, "plugins")
    repo_plugins = os.path.join(REPO_ROOT, "plugins")
    if os.path.exists(repo_plugins):
        os.makedirs(plugins_target, exist_ok=True)
        for f in os.listdir(repo_plugins):
            if f.endswith('.dll'):
                dst_file = os.path.join(plugins_target, f)
                if not os.path.exists(dst_file):
                    shutil.copy2(os.path.join(repo_plugins, f), dst_file)

    # Create ZIP archive
    if os.path.exists(zip_path):
        os.remove(zip_path)

    with zipfile.ZipFile(zip_path, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zipf:
        for file in sorted(allowed_files):
            full_path = os.path.join(target_dir, file)
            if os.path.exists(full_path):
                zipf.write(full_path, file)
        if os.path.exists(plugins_target):
            for file in sorted(os.listdir(plugins_target)):
                full_path = os.path.join(plugins_target, file)
                if os.path.isfile(full_path):
                    zipf.write(full_path, os.path.join("plugins", file))

    print("\n=== Release Contents ===")
    for f in sorted(os.listdir(target_dir)):
        p = os.path.join(target_dir, f)
        if os.path.isdir(p):
            print(f"  [{f}/]")
            for sub in sorted(os.listdir(p)):
                sub_p = os.path.join(p, sub)
                print(f"    {sub} ({os.path.getsize(sub_p):,} bytes)")
        else:
            print(f"  {f} ({os.path.getsize(p):,} bytes)")

    print(f"\nCreated ZIP: {zip_path} ({os.path.getsize(zip_path):,} bytes)")

if __name__ == "__main__":
    main()
