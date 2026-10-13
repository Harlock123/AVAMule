#!/usr/bin/env python3
"""Synthesises AVAMule's sound effects and tunes into ../src/AVAMule/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAMule", "Sounds")
RNG = random.Random(1985)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def main():
    os.makedirs(OUT, exist_ok=True)
    write("Tick.wav", tone(0.03, 1400, kind="square", release=1.2), 0.18)
    write("Claim.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.4) for n in ["G5", "C6", "E6", "G6"]]), 0.4)
    write("Buy.wav", cat(tone(0.05, hz("E6"), kind="square", release=0.6), tone(0.12, hz("A6"), kind="square", release=0.7)), 0.3)
    write("Outfit.wav", cat(*[tone(0.04, 300 + i * 120, kind="square", release=0.5) for i in range(8)]), 0.3)
    write("Install.wav", mix(tone(0.25, 200, 400, "square", release=0.5), tone(0.25, 800, 1600, "tri", release=0.6, gain=0.5)), 0.38)
    # A mule's bray: up and down and up, roughened with noise.
    bray = cat(tone(0.22, 700, 1100, "saw", release=0.2, vibrato=0.05, vib_rate=30), tone(0.3, 500, 300, "saw", release=0.4, vibrato=0.08, vib_rate=24),
               tone(0.2, 650, 1000, "saw", release=0.3, vibrato=0.05, vib_rate=30), tone(0.35, 450, 250, "saw", release=0.6, vibrato=0.08, vib_rate=24))
    write("Bray.wav", mix(bray, tone(len(bray) / RATE, 900, 400, "noise", release=0.5, gain=0.25)), 0.45)
    write("Runaway.wav", cat(*[tone(0.07, f, f * 0.7, "square", release=0.6) for f in [900, 800, 700, 600, 500, 400]]), 0.38)
    write("Sample.wav", cat(tone(0.08, 300, 120, "noise", release=0.8), tone(0.06, 1200, kind="tri", release=0.6)), 0.3)
    write("Assay.wav", cat(*[tone(0.05, f, kind="sine", release=0.5) for f in [1200, 1500, 1800, 2400]]), 0.35)
    write("Pub.wav", cat(*[tone(0.05, hz(n), kind="tri", release=0.4) for n in ["C6", "G5", "E5", "C5", "E5", "G5", "C6"]]), 0.38)
    write("Coin.wav", cat(tone(0.05, hz("B5"), kind="square", release=0.5), tone(0.18, hz("E6"), kind="square", release=0.7)), 0.3)
    write("Produce.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["C5", "D5", "E5", "G5", "C6"]]), 0.38)
    write("Event.wav", cat(*[tone(0.12, f, kind="square", release=0.3) for f in [523, 392, 523, 392]]), 0.36)
    write("Trade.wav", tone(0.04, 1800, 1500, "square", release=1.0), 0.22)
    write("Gong.wav", mix(tone(1.0, 220, kind="sine", release=0.4), tone(1.0, 331, kind="sine", release=0.5, gain=0.5), tone(1.0, 554, kind="sine", release=0.6, gain=0.3)), 0.45)
    write("Step.wav", tone(0.04, 200, 120, "noise", release=1.5), 0.15)
    write("Menu.wav", tone(0.03, 1200, kind="tri", release=0.8), 0.2)

    # The colony's tune: a bouncy frontier walk in G over an oom-pah bass (new). Loops.
    lead = [("G4", 0.5), ("B4", 0.5), ("D5", 0.5), ("G5", 0.5), ("F#5", 0.5), ("E5", 0.5), ("D5", 1),
            ("E5", 0.5), ("C5", 0.5), ("A4", 0.5), ("C5", 0.5), ("B4", 0.5), ("G4", 0.5), ("A4", 1),
            ("G4", 0.5), ("B4", 0.5), ("D5", 0.5), ("G5", 0.5), ("A5", 0.5), ("G5", 0.5), ("E5", 0.5), ("C5", 0.5),
            ("D5", 0.5), ("B4", 0.5), ("A4", 0.5), ("F#4", 0.5), ("G4", 2)]
    bass = []
    for root, fifth in [("G2", "D3"), ("G2", "D3"), ("C3", "G3"), ("D3", "A2"), ("G2", "D3"), ("E2", "B2"), ("A2", "D3"), ("G2", "G2")]:
        bass += [(root, 1), (fifth, 1)]
    write("Music.wav", tune(132, lead, bass), 0.4)
    write("TitleTune.wav", tune(120,
        [("D5", 0.5), ("G5", 0.5), ("B5", 1), ("A5", 0.5), ("G5", 0.5), ("E5", 1), ("D5", 1), ("B4", 1),
         ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("F#5", 0.5), ("E5", 0.5), ("D5", 2),
         ("D5", 0.5), ("G5", 0.5), ("B5", 1), ("C6", 0.5), ("B5", 0.5), ("A5", 1), ("G5", 1), ("E5", 1),
         ("D5", 0.5), ("B4", 0.5), ("A4", 0.5), ("F#4", 0.5), ("G4", 2)],
        [(n, 1) for n in ["G2", "D3", "G2", "D3", "C3", "G3", "D3", "A2", "G2", "D3", "C3", "D3", "G2", "D3", "E2", "B2", "C3", "D3", "G2", "G2"]]), 0.55)
    write("Fanfare.wav", tune(150,
        [("G4", 0.5), ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("E5", 0.5), ("G5", 2.5)],
        [("C3", 1.5), ("G2", 1), ("C3", 3)]), 0.55)
    write("GameOver.wav", tune(90,
        [("G5", 1), ("D5", 0.5), ("B4", 0.5), ("C5", 1), ("A4", 1), ("G4", 3)],
        [("G2", 2), ("C3", 1), ("D3", 1), ("G2", 3)], kind="tri"), 0.55)


if __name__ == "__main__":
    main()
