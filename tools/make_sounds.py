#!/usr/bin/env python3
"""Synthesises AVAPinballConstructionSet's sound effects and tunes into ../src/AVAPinballConstructionSet/Sounds as 16-bit mono WAV.

Nothing is sampled from any game or machine: every effect is built here from sine, square, triangle and noise
voices: solenoid thwacks for the flippers and kickers, bells for the rollovers and targets (the chimes of the
electro-mechanical tables), ticks for the spinner, buzzers for the tilt, and blips for the construction set. The
title plays The Entertainer (Scott Joplin, 1902; public domain) on a honky-tonk piano voice. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAPinballConstructionSet", "Sounds")
RNG = random.Random(1983)


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
    return math.sin(2 * math.pi * p)


def tone(seconds, f0, f1=None, kind="square", attack=0.003, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
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
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds duller.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def bell(seconds, f, gain=1.0, decay=6.0, partials=((1, 1.0), (2.76, 0.45), (5.4, 0.25), (8.93, 0.12))):
    """A struck bell or chime: a few inharmonic partials, each dying away, the high ones first."""
    n = int(RATE * seconds)
    out = []
    for i in range(n):
        t = i / RATE
        s = 0.0
        for k, (ratio, amp) in enumerate(partials):
            s += amp * math.sin(2 * math.pi * f * ratio * t) * math.exp(-decay * (1 + k * 0.8) * t)
        out.append(s * gain * min(1.0, t / 0.002))
    return out


def thump(seconds, f0, f1, gain=1.0):
    """A solenoid's thump: a low sine falling in pitch, gone quickly."""
    n = int(RATE * seconds)
    out, phase = [], 0.0
    for i in range(n):
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        phase += f / RATE
        out.append(math.sin(2 * math.pi * phase) * math.exp(-i / n * 5) * gain)
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


def at(seconds, part):
    """A sound starting a little way in."""
    return silence(seconds) + part


def lowpass(samples, k):
    out, y = [], 0.0
    for s in samples:
        y += k * (s - y)
        out.append(y)
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def piano(seconds, note, gain=0.6):
    """A honky-tonk upright: two strings a little out of tune with each other, struck and dying away."""
    f = hz(note)
    n = int(RATE * seconds)
    tail = int(RATE * 0.05)
    out = []
    for i in range(n + tail):
        t = i / RATE
        s = 0.0
        for detune in (1.0, 1.006):
            for h, amp in ((1, 1.0), (2, 0.45), (3, 0.22), (4, 0.1)):
                s += amp * math.sin(2 * math.pi * f * detune * h * t) * math.exp(-t * (2.2 + h * 1.4))
        env = min(1.0, t / 0.003) * (1.0 if i < n else 1 - (i - n) / tail)
        out.append(s * env * gain * 0.5)
    return out


def play(unit, notes, gain=0.6):
    """Notes as (name, units) one after another; a list of names is a chord; '-' is a rest. Each note rings into the next."""
    total = int(sum(u for _, u in notes) * unit * RATE) + RATE
    out = [0.0] * total
    pos = 0.0
    for note, units in notes:
        d = units * unit
        if note != "-":
            for name in note if isinstance(note, list) else [note]:
                s = piano(d + 0.04, name, gain)
                start = int(pos * RATE)
                for i, v in enumerate(s):
                    if start + i < total:
                        out[start + i] += v
        pos += d
    return out[: int(pos * RATE) + int(0.2 * RATE)]


def entertainer():
    """The Entertainer (Scott Joplin, 1902): its famous first strain, in C, over a stride bass, in sixteenths of 2/4."""
    pickup = [("D4", 1), ("D#4", 1)]
    head = [("E4", 1), ("C5", 2), ("E4", 1), ("C5", 2), ("E4", 1), ("C5", 5)]
    a = head + [("C5", 1), ("D5", 1), ("D#5", 1), ("E5", 1),
                ("C5", 1), ("D5", 1), ("E5", 2), ("B4", 1), ("D5", 2), ("C5", 7)] + pickup
    b = head + [("A4", 1), ("G4", 1), ("F#4", 1), ("A4", 1),
                ("C5", 1), ("E5", 2), ("D5", 1), ("C5", 1), ("A4", 1), ("D5", 8)] + pickup
    c = head + [("C5", 1), ("D5", 1), ("D#5", 1), ("E5", 1),
                ("C5", 1), ("D5", 1), ("E5", 2), ("B4", 1), ("D5", 2), ("C5", 9)]
    d = [("C5", 1), ("D5", 1), ("E5", 1), ("C5", 1), ("D5", 1), ("E5", 2), ("C5", 1),
         ("D5", 1), ("C5", 1), ("E5", 1), ("C5", 1), ("D5", 1), ("E5", 2), ("C5", 1),
         ("D5", 1), ("C5", 1), ("E5", 1), ("C5", 1), ("D5", 1), ("E5", 2), ("B4", 1),
         ("D5", 2), ("C5", 6)]
    lead = pickup + a + b + c + d
    chords = {
        "C": ("C3", "G2", ["E3", "G3", "C4"]),
        "G": ("G2", "D3", ["B3", "D4", "F4"]),
        "D7": ("D3", "A2", ["F#3", "A3", "C4"]),
    }
    bars = ["C", "C", "G", "C", "C", "C", "D7", "G", "C", "C", "G", "C", "C", "C", "G", "C"]
    low = [("-", 2)]
    for name in bars:
        root, fifth, chord = chords[name]
        low += [(root, 2), (chord, 2), (fifth, 2), (chord, 2)]
    return lead, low


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))

    # ── The table ──
    # A flipper: the solenoid's clack and the bat's thump.
    write("Flipper.wav", mix(lowpass(tone(0.05, 3000, 1200, "noise", attack=0.001, release=2.0), 0.5),
                             thump(0.09, 160, 70, 0.9)), 0.5)
    # A pop bumper: a hard thump under a bright chime.
    write("Bumper.wav", mix(thump(0.1, 190, 60, 1.0), lowpass(tone(0.04, 4000, 2000, "noise", attack=0.001, release=2), 0.6),
                            bell(0.35, 988, 0.55, decay=9)), 0.6)
    # A kicker: a snap of rubber and a clack.
    write("Sling.wav", mix(tone(0.06, 700, 260, "square", attack=0.001, release=2.0, gain=0.35),
                           lowpass(tone(0.04, 5000, 2500, "noise", attack=0.001, release=2.0), 0.7), thump(0.07, 140, 70, 0.7)), 0.5)
    # Rollovers and targets: the chimes, high, middle and low.
    write("Rollover.wav", bell(0.45, 1568, 1.0, decay=6), 0.45)
    write("Standup.wav", mix(lowpass(tone(0.025, 4000, 3000, "noise", attack=0.001, release=2), 0.6), at(0.005, bell(0.4, 1047, 0.9, decay=7))), 0.5)
    write("Drop.wav", mix(thump(0.12, 120, 50, 1.0), lowpass(tone(0.05, 1800, 600, "noise", attack=0.001, release=1.5), 0.4),
                          at(0.01, bell(0.35, 784, 0.6, decay=8))), 0.55)
    write("Reset.wav", cat(*[mix(thump(0.06, 160, 90, 0.8), lowpass(tone(0.03, 2000, 1000, "noise", attack=0.001, release=2), 0.5)) for _ in range(3)]), 0.4)
    # The spinner: one tick a turn.
    write("Spinner.wav", mix(tone(0.018, 2600, 1800, "square", attack=0.0005, release=2.0, gain=0.4), lowpass(tone(0.015, 6000, 4000, "noise", attack=0.0005, release=2), 0.8)), 0.35)
    write("Rubber.wav", tone(0.09, 420, 260, "tri", attack=0.001, release=2.0), 0.3)
    write("Thud.wav", thump(0.07, 110, 60, 1.0), 0.3)
    # The kick-out hole: the ball dropping in, and kicked out.
    write("Catch.wav", mix(thump(0.16, 140, 70, 1.0), lowpass(tone(0.08, 900, 300, "noise", attack=0.001, release=1.5), 0.3)), 0.5)
    write("Kickout.wav", mix(thump(0.12, 90, 200, 1.0), lowpass(tone(0.2, 600, 3000, "noise", attack=0.002, release=1.2, gain=0.6), 0.4)), 0.55)
    # The ball eater: a gulp.
    write("Eaten.wav", cat(tone(0.12, 500, 250, "square", attack=0.002, release=0.3, vibrato=0.06, vib_rate=30, gain=0.5),
                           tone(0.28, 250, 70, "square", attack=0.002, release=0.8, vibrato=0.08, vib_rate=22, gain=0.5)), 0.4)
    # The magnet: a hum coming on, with a crackle.
    write("Magnet.wav", mix(tone(0.6, 60, 60, "saw", attack=0.12, release=0.6, gain=0.6), tone(0.6, 120, 120, "square", attack=0.12, release=0.6, gain=0.25),
                            lowpass(tone(0.6, 8000, 8000, "noise", attack=0.2, release=0.5, gain=0.15), 0.9)), 0.4)
    # The plunger: the spring drawn back, and let go.
    write("Pull.wav", cat(*[cat(tone(0.012, 1800, 1200, "square", attack=0.001, release=2, gain=0.3), silence(0.05)) for _ in range(6)]), 0.25)
    write("Launch.wav", mix(thump(0.1, 120, 60, 1.0), lowpass(tone(0.18, 300, 2500, "noise", attack=0.002, release=1.0, gain=0.7), 0.35)), 0.55)
    # The ball lost.
    write("Drain.wav", cat(*[tone(0.16, hz(n), kind="square", attack=0.003, release=0.6, gain=0.4) for n in ["G4", "E4", "C4"]]), 0.35)
    # The nudge, the warning and the tilt buzzer.
    write("Nudge.wav", mix(thump(0.1, 80, 50, 1.0), lowpass(tone(0.12, 1500, 500, "noise", attack=0.002, release=1.0, gain=0.5), 0.3)), 0.5)
    write("Danger.wav", cat(tone(0.18, 140, kind="square", release=0.2, gain=0.5), silence(0.06), tone(0.18, 140, kind="square", release=0.2, gain=0.5)), 0.35)
    write("Tilt.wav", mix(tone(1.0, 90, 88, "saw", attack=0.01, release=0.3), tone(1.0, 135, 132, "square", attack=0.01, release=0.3, gain=0.4)), 0.45)
    # Good news: a part lit, a coin in, the bonus counted, an extra ball, multiball.
    write("Lit.wav", mix(bell(0.5, hz("E6"), 0.8, decay=6), at(0.08, bell(0.5, hz("A6"), 0.8, decay=6))), 0.4)
    write("Coin.wav", mix(bell(0.6, hz("B5"), 0.9, decay=5), at(0.09, bell(0.6, hz("E6"), 0.9, decay=5))), 0.45)
    write("BonusCount.wav", cat(*[bell(0.09, 900 + k * 70, 0.8, decay=18) for k in range(12)]), 0.4)
    write("ExtraBall.wav", mix(*[at(i * 0.1, bell(0.7, hz(n), 0.8, decay=4)) for i, n in enumerate(["C5", "E5", "G5", "C6", "E6"])]), 0.5)
    write("Multiball.wav", cat(*[tone(0.12, f, f * 1.5, "square", attack=0.003, release=0.3, gain=0.4) for f in (440, 554, 659, 880, 659, 880)],
                               tone(0.35, 1108, kind="square", attack=0.003, release=0.6, gain=0.4)), 0.4)

    # ── The construction set ──
    write("Click.wav", mix(tone(0.012, 2400, 2000, "square", attack=0.0005, release=2, gain=0.4), lowpass(tone(0.01, 6000, 4000, "noise", attack=0.0005, release=2), 0.8)), 0.25)
    write("Pick.wav", tone(0.07, 420, 900, "tri", attack=0.002, release=0.8), 0.3)
    write("Place.wav", mix(tone(0.07, 900, 380, "tri", attack=0.002, release=0.8), at(0.06, thump(0.05, 200, 120, 0.8))), 0.35)
    write("Delete.wav", mix(lowpass(tone(0.25, 3000, 300, "noise", attack=0.002, release=1.0), 0.4), tone(0.25, 600, 120, "square", attack=0.002, release=1.0, gain=0.25)), 0.35)
    write("Paint.wav", lowpass(tone(0.18, 1200, 400, "noise", attack=0.01, release=1.2, vibrato=0.3, vib_rate=25), 0.25), 0.35)
    write("Wire.wav", tone(0.08, 1200, 2600, "square", attack=0.001, release=0.6, gain=0.4), 0.22)
    write("Undo.wav", cat(tone(0.05, 700, 400, "tri", attack=0.002, release=0.5), tone(0.06, 400, 700, "tri", attack=0.002, release=0.8)), 0.3)
    write("Refused.wav", tone(0.18, 110, kind="square", release=0.6, gain=0.5), 0.2)

    # ── Music ──
    unit = 60.0 / 104 / 4
    lead, low = entertainer()
    write("TitleTune.wav", cat(mix(play(unit, lead, 0.62), play(unit, low, 0.34)), silence(1.5)), 0.42)
    write("Start.wav", mix(play(0.09, [("G4", 1), ("C5", 1), ("E5", 1), ("G5", 3)]), play(0.09, [(["C3", "G3"], 6)], 0.4)), 0.45)
    write("HighScore.wav", mix(play(0.1, [("C5", 1), ("E5", 1), ("G5", 1), ("C6", 2), ("A5", 1), ("C6", 4)]),
                               play(0.1, [("C3", 3), ("F3", 3), (["C3", "G3"], 4)], 0.4)), 0.45)
    write("GameOver.wav", mix(play(0.16, [("G4", 2), ("F#4", 2), ("F4", 2), ("E4", 2), ("C4", 6)]),
                              play(0.16, [("C3", 4), ("G2", 4), ("C3", 6)], 0.4)), 0.45)


if __name__ == "__main__":
    main()
