# -*- mode: python ; coding: utf-8 -*-
"""
PyInstaller spec file for the Photobooth Python service.

Strategy: use minimal collect_all() only for packages that need it (uvicorn),
and use explicit hiddenimports for the rest. This avoids the Python 3.10.0
bytecode scan IndexError that occurs when collect_all() recursively scans
packages with incompatible bytecode (e.g. Pillow, mediapipe).
"""

import os as _os
from PyInstaller.utils.hooks import collect_all, collect_data_files, collect_dynamic_libs

block_cipher = None

_script_dir = _os.path.dirname(_os.path.abspath(SPECPATH))

# Only collect data/binaries for packages that have non-Python assets.
# Do NOT use collect_all() on packages that cause bytecode scan crashes.
uvicorn_datas,  uvicorn_bins,  uvicorn_hidden  = collect_all("uvicorn")
mp_datas    = collect_data_files("mediapipe")
mp_bins     = collect_dynamic_libs("mediapipe")
cv2_datas,  cv2_bins,  _  = collect_all("cv2")

all_datas    = uvicorn_datas + mp_datas + cv2_datas
all_binaries = uvicorn_bins  + mp_bins  + cv2_bins

# ---------------------------------------------------------------------------
# Analysis
# ---------------------------------------------------------------------------
a = Analysis(
    ["_entry.py"],
    pathex=[_script_dir],
    binaries=all_binaries,
    datas=all_datas,
    hiddenimports=uvicorn_hidden + [
        # fastapi / starlette
        "fastapi",
        "fastapi.middleware",
        "fastapi.middleware.cors",
        "starlette",
        "starlette.routing",
        "starlette.middleware",
        "starlette.middleware.base",
        "starlette.responses",
        "starlette.requests",
        "starlette.datastructures",
        # anyio
        "anyio",
        "anyio._backends._asyncio",
        # multipart
        "python_multipart",
        "multipart",
        # local app package
        "app",
        "app.main",
        "app.processing",
        "app.schemas",
        # image libs
        "PIL",
        "PIL.Image",
        "PIL.ImageEnhance",
        "PIL.ImageFilter",
        "numpy",
        "cv2",
        # mediapipe (loaded dynamically at runtime, excluding genai)
        "mediapipe",
        "mediapipe.python",
        "mediapipe.python.solutions",
        "mediapipe.python.solutions.selfie_segmentation",
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        "tkinter", "matplotlib", "PyQt5", "wx",
        "mediapipe.tasks.python.genai",
        "mediapipe.tasks.python.genai.converter",
        "jax", "torch", "sentencepiece",
    ],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

# ---------------------------------------------------------------------------
# PYZ archive
# ---------------------------------------------------------------------------
pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

# ---------------------------------------------------------------------------
# EXE - single file, no console window
# ---------------------------------------------------------------------------
exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name="python_service",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
