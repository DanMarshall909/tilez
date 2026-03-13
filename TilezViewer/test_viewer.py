#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Simple test to verify tiles are being generated and can be displayed.
This helps debug the Godot viewer setup.
"""

import os
import sys
import subprocess
import json
from pathlib import Path

# Fix encoding on Windows
if sys.platform == "win32":
    import io

    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

# Paths
TILEZ_PROJECT = r"C:\code\tilez\Tilez\Tilez.csproj"
GENERATED_DIR = r"C:\code\tilez\generated"
IMAGES_DIR = os.path.join(GENERATED_DIR, "images")
METADATA_DIR = os.path.join(GENERATED_DIR, "metadata")

# LMStudio config
BASE_URL = "http://localhost:1234"
AI_MODEL = "flux.1-schnell"


def test_lmstudio():
    """Check if LMStudio is running and has Flux"""
    print("Testing LMStudio connection...")
    try:
        import requests

        resp = requests.get(f"{BASE_URL}/v1/models", timeout=5)
        models = resp.json().get("data", [])
        model_ids = [m["id"] for m in models]

        print(f"✓ Connected to LMStudio")
        print(f"  Available models: {model_ids[:3]}...")

        if AI_MODEL in model_ids:
            print(f"✓ Found {AI_MODEL}")
            return True
        else:
            print(f"✗ {AI_MODEL} not found in models")
            return False
    except Exception as e:
        print(f"✗ Cannot connect to LMStudio: {e}")
        return False


def generate_test_tile():
    """Generate a single test tile"""
    print("\nGenerating test tile...")
    cmd = [
        "dotnet",
        "run",
        "--project",
        TILEZ_PROJECT,
        "--",
        "render",
        "--image-backend",
        "openai",
        "--ai-base-url",
        BASE_URL,
        "--ai-model",
        AI_MODEL,
        "--x",
        "0",
        "--y",
        "0",
        "--zoom",
        "3.0",
        "--seed",
        "42",
    ]

    try:
        result = subprocess.run(
            cmd, cwd=r"C:\code\tilez", capture_output=True, text=True, timeout=180
        )
        print(result.stdout)
        if result.returncode == 0:
            print("✓ Tile generated successfully")
            return True
        else:
            print(f"✗ Generation failed: {result.stderr}")
            return False
    except subprocess.TimeoutExpired:
        print("✗ Generation timed out (>3 minutes)")
        return False
    except Exception as e:
        print(f"✗ Generation error: {e}")
        return False


def check_generated_tiles():
    """List generated tiles"""
    print("\nChecking generated tiles...")
    if os.path.exists(IMAGES_DIR):
        files = list(Path(IMAGES_DIR).glob("*.png"))
        print(f"✓ Found {len(files)} PNG files")

        # Show latest few
        recent = sorted(files, key=lambda x: x.stat().st_mtime, reverse=True)[:5]
        for f in recent:
            size_kb = f.stat().st_size / 1024
            print(f"  - {f.name} ({size_kb:.1f} KB)")

        return len(files) > 0
    else:
        print(f"✗ Images directory not found: {IMAGES_DIR}")
        return False


def check_godot():
    """Check if Godot is available"""
    print("\nChecking Godot...")
    try:
        result = subprocess.run(
            ["godot", "--version"], capture_output=True, text=True, timeout=5
        )
        version = result.stdout.strip()
        print(f"✓ Godot {version}")
        return True
    except Exception as e:
        print(f"✗ Godot not found: {e}")
        return False


def main():
    print("=" * 60)
    print("TILEZ VIEWER - SYSTEM TEST")
    print("=" * 60)

    results = {
        "LMStudio": test_lmstudio(),
        "Godot": check_godot(),
        "Generated Tiles": check_generated_tiles(),
    }

    print("\n" + "=" * 60)
    print("RUNNING TEST GENERATION")
    print("=" * 60)

    if results["LMStudio"]:
        generate_test_tile()
        check_generated_tiles()

    print("\n" + "=" * 60)
    print("SUMMARY")
    print("=" * 60)
    for name, status in results.items():
        symbol = "✓" if status else "✗"
        print(f"{symbol} {name}")

    all_ok = all(results.values())
    if all_ok:
        print("\n✓ All systems ready! You can now launch the Godot viewer:")
        print(f"   cd C:\\code\\tilez\\TilezViewer\\project")
        print(f"   godot")
    else:
        print("\n✗ Some systems not ready. Check errors above.")

    return 0 if all_ok else 1


if __name__ == "__main__":
    exit(main())
