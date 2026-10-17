"""Offline entry point for the industrial sound library, including the four interface cues."""
import runpy
from pathlib import Path
runpy.run_path(str(Path(__file__).resolve().parent / "audio/RenderIndustrialAudio.py"), run_name="__main__")
