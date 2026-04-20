"""
PyInstaller entry point for the Photobooth Python service.

Windowless executables (console=False) have sys.stdout = None.
Uvicorn's DefaultFormatter calls sys.stdout.isatty() on startup,
which raises AttributeError. We redirect streams to devnull first.

We import the FastAPI app object directly (not via a string path)
so that PyInstaller can statically trace and bundle the app package.
"""

import multiprocessing
import os
import sys

import uvicorn

# Patch None streams before any uvicorn code runs.
if sys.stdout is None:
    sys.stdout = open(os.devnull, "w")
if sys.stderr is None:
    sys.stderr = open(os.devnull, "w")

# Import the FastAPI app directly for static traceability by PyInstaller.
from app.main import app as fastapi_app
from app import processing  # noqa: F401 - ensure bundled
from app import schemas     # noqa: F401 - ensure bundled

LOG_CONFIG = {
    "version": 1,
    "disable_existing_loggers": False,
    "formatters": {
        "default": {
            "()": "uvicorn.logging.DefaultFormatter",
            "fmt": "%(levelprefix)s %(message)s",
            "use_colors": False,
        },
    },
    "handlers": {
        "default": {
            "formatter": "default",
            "class": "logging.StreamHandler",
            "stream": "ext://sys.stderr",
        },
    },
    "loggers": {
        "uvicorn": {"handlers": ["default"], "level": "WARNING"},
        "uvicorn.error": {"level": "WARNING"},
        "uvicorn.access": {
            "handlers": ["default"],
            "level": "WARNING",
            "propagate": False,
        },
    },
}


def main():
    uvicorn.run(
        fastapi_app,
        host="127.0.0.1",
        port=8000,
        log_config=LOG_CONFIG,
        reload=False,
    )


if __name__ == "__main__":
    multiprocessing.freeze_support()
    main()
