"""Reproduce the original, non-sampled FORGELINE interface cues (Python standard library)."""
from pathlib import Path
import math
import struct
import wave

root = Path(__file__).resolve().parent.parent / "assets/source/audio/ui"
root.mkdir(parents=True, exist_ok=True)
for name, duration, start, end in [
    ("navigation", .035, 880, 720),
    ("intent", .055, 460, 460),
    ("accepted", .095, 640, 960),
    ("rejected", .110, 330, 220),
]:
    frames = bytearray()
    phase = 0.0
    count = round(duration * 48000)
    for index in range(count):
        t = index / (count - 1)
        phase += 2 * math.pi * (start + (end - start) * t) / 48000
        envelope = math.sin(math.pi * t) ** 2
        sample = round(32767 * .16 * envelope * (math.sin(phase) + .12 * math.sin(2 * phase)) / 1.12)
        frames.extend(struct.pack("<hh", sample, sample))
    with wave.open(str(root / (name + ".wav")), "wb") as output:
        output.setnchannels(2)
        output.setsampwidth(2)
        output.setframerate(48000)
        output.writeframes(frames)
