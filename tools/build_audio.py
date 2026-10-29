"""Compose Billu's rooftop soundtrack and foley from scratch, using only Python.

Original music, synthesized instruments, no recordings or external samples.
Run normally to export everything, or with --music-only for the soundtrack.
"""
from array import array
from pathlib import Path
import json
import math
import random
import sys
import wave

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "BilluKiBalti/Assets/Audio"
RATE = 22050
BPM = 104
BEAT = 60 / BPM
TAU = math.tau
rng = random.Random(20261029)
report = {}


def silence(seconds):
    return array("f", [0]) * round(seconds * RATE)


def hz(note):
    return 440 * 2 ** ((note - 69) / 12)


def envelope(t, duration, attack=.004, release=.025):
    return max(0, min(1, t / attack, (duration - t) / release))


def mix(destination, source, at=0, gain=1, wrap=False):
    start = round(at * RATE)
    for i, sample in enumerate(source):
        index = start + i
        if wrap:
            index %= len(destination)
        if 0 <= index < len(destination):
            destination[index] += sample * gain


def pluck(note, duration=.65, muted=False):
    """Noise-excited string with a dark wooden body, inspired by a rubab."""
    frequency = hz(note)
    size = max(4, round(RATE / frequency - .5))
    ring = [rng.uniform(-1, 1) for _ in range(size)]
    # Remove DC from the excitation and soften the initial pick transient.
    average = sum(ring) / size
    ring = [(x - average) * .45 for x in ring]
    result = silence(duration)
    damping = .976 if muted else .994
    for i in range(len(result)):
        t = i / RATE
        slot = i % size
        sample = ring[slot]
        ring[slot] = damping * (sample + ring[(slot + 1) % size]) * .5
        body = .18 * math.sin(TAU * frequency * t) * math.exp(-t * 7)
        result[i] = (sample + body) * envelope(t, duration)
    return result


def flute(note, duration=.6, scoop=False):
    """Breathy, gently ornamented flute; rounded attack leaves room for drums."""
    result = silence(duration)
    phase = 0
    breath = 0
    for i in range(len(result)):
        t = i / RATE
        bend = (-.8 * math.exp(-t * 26) if scoop else 0)
        bend += .075 * math.sin(TAU * 5.1 * t) * min(1, t * 4)
        phase += TAU * hz(note + bend) / RATE
        breath = breath * .72 + rng.uniform(-1, 1) * .28
        body = math.sin(phase) + .16 * math.sin(phase * 2) + .045 * math.sin(phase * 3)
        shape = envelope(t, duration, .035, .10) * (.84 + .16 * math.exp(-t * 4))
        result[i] = (.23 * body + .014 * breath) * shape
    return result


def reed_chord(notes, duration=4.8):
    """Very soft harmonium-like bed under the melody."""
    result = silence(duration)
    for i in range(len(result)):
        t = i / RATE
        value = 0
        for note in notes:
            phase = TAU * hz(note) * t
            value += math.sin(phase) + .19 * math.sin(phase * 2) + .08 * math.sin(phase * 3)
        result[i] = value * .035 * envelope(t, duration, .3, .45)
    return result


def soft_bass(note, duration=.8):
    """Rounded air-driven bass, with no string excitation or pick transient."""
    result = silence(duration)
    for i in range(len(result)):
        t = i / RATE
        phase = TAU * hz(note) * t
        body = math.sin(phase) + .08 * math.sin(phase * 2)
        result[i] = .24 * body * envelope(t, duration, .055, .16)
    return result


def drum(kind):
    duration = {"dum": .32, "tak": .15, "rim": .09}[kind]
    result = silence(duration)
    noise_low = 0
    for i in range(len(result)):
        t = i / RATE
        noise = rng.uniform(-1, 1)
        noise_low = .65 * noise_low + .35 * noise
        if kind == "dum":
            phase = TAU * (88 * t + 1.65 * (1 - math.exp(-t * 24)))
            value = .52 * math.sin(phase) * math.exp(-t * 16)
            value += .12 * math.sin(phase * 1.93) * math.exp(-t * 29)
            value += noise_low * .13 * math.exp(-t * 70)
        elif kind == "tak":
            value = .18 * math.sin(TAU * 293.66 * t) * math.exp(-t * 32)
            value += .10 * math.sin(TAU * 455 * t) * math.exp(-t * 45)
            value += (noise - noise_low) * .22 * math.exp(-t * 58)
        else:
            value = .19 * math.sin(TAU * 820 * t) * math.exp(-t * 80)
            value += noise_low * .22 * math.exp(-t * 95)
        result[i] = value * envelope(t, duration, .0015, .015)
    return result


def metal(note=81, duration=.24):
    """Small bucket-rim tap with a quickly damped metallic shimmer."""
    result = silence(duration)
    frequency = hz(note)
    for i in range(len(result)):
        t = i / RATE
        value = sum(amp * math.sin(TAU * frequency * ratio * t) * math.exp(-decay * t)
                    for ratio, amp, decay in [(1, .30, 18), (2.32, .12, 35), (3.87, .04, 52)])
        result[i] = value * envelope(t, duration, .002, .02)
    return result


def paper(duration=.08):
    result = silence(duration)
    low = 0
    for i in range(len(result)):
        t = i / RATE
        noise = rng.uniform(-1, 1)
        low = low * .8 + noise * .2
        result[i] = (noise - low) * .10 * math.sin(math.pi * t / duration) ** 2
    return result


def export(name, samples, peak=.78, loop=False):
    # A tiny DC correction and gentle saturation keep the mix comfortable.
    mean = sum(samples) / len(samples)
    shaped = [math.tanh((x - mean) * 1.25) for x in samples]
    if not loop:
        edge = min(round(.006 * RATE), len(shaped) // 2)
        for i in range(edge):
            shaped[i] *= i / edge
            shaped[-1 - i] *= i / edge
    scale = peak / max(.001, max(abs(x) for x in shaped))
    pcm = array("h", [round(x * scale * 32767) for x in shaped])
    if sys.byteorder != "little":
        pcm.byteswap()
    OUT.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT / (name + ".wav")), "wb") as output:
        output.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        output.writeframes(pcm.tobytes())
    report[name] = {
        "seconds": round(len(samples) / RATE, 3),
        "peak": peak,
        "rms": round(math.sqrt(sum((x * scale) ** 2 for x in shaped) / len(shaped)), 4),
    }
    print(f"Wrote {name}: {report[name]['seconds']}s", flush=True)


def compose_music():
    # Sixteen eight-beat flute phrases: hook, quieter reply, lift, return.
    # D-major / Mixolydian colours keep the evening setting playful and warm.
    track = silence(128 * BEAT)
    hooks = [
        [(0, 62, .8), (1, 66, .55), (2, 69, .9), (3.5, 66, .4), (4, 64, 1), (6, 62, 1.1)],
        [(.5, 64, .5), (1.5, 66, .6), (3, 69, .7), (4, 71, 1.2), (6, 69, 1.1)],
        [(0, 74, 1), (1.5, 71, .5), (2.5, 69, .75), (4, 67, .8), (5.5, 66, .5), (6.5, 64, .8)],
        [(0, 66, .7), (1.5, 64, .55), (3, 62, 1), (5, 57, .6), (6, 62, 1.4)],
    ]
    harmony = [(50, 57, 62), (50, 57, 64), (43, 50, 59), (45, 52, 57)]
    for phrase in range(16):
        origin = phrase * 8 * BEAT
        section = phrase // 4
        hook = hooks[phrase % 4]
        mix(track, reed_chord(harmony[phrase % 4], 8 * BEAT + .3), origin, .55, True)
        for offset, note, length in hook:
            at = origin + offset * BEAT
            # Breaths between phrases and gentle pitch scoops keep the lead light.
            lead = flute(note, max(.28, length * BEAT), scoop=offset in (0, 4))
            gain = (.52, .43, .57, .48)[section]
            mix(track, lead, at, gain, True)
            mix(track, lead, at + BEAT * .75, .045, True)
        for offset in (0, 2.5, 4, 6.5):
            bass_note = harmony[phrase % 4][0] + (12 if offset in (2.5, 6.5) else 0)
            mix(track, soft_bass(bass_note, .8), origin + offset * BEAT, .22, True)
        # Eight-beat hand drum cycle, with restrained ghost strokes and fills.
        for offset, accent in [(0, .68), (1.5, .30), (4, .56), (6, .39)]:
            mix(track, drum("dum"), origin + offset * BEAT, accent, True)
        for offset, accent in [(1, .38), (2, .55), (3, .30), (5, .40), (6.5, .3), (7, .45)]:
            mix(track, drum("tak"), origin + (offset + .025) * BEAT, accent, True)
        for offset in (.5, 2.5, 4.5, 5.5, 7.5):
            mix(track, drum("rim"), origin + offset * BEAT, .15, True)
        if phrase % 4 == 3:
            mix(track, drum("tak"), origin + 7.75 * BEAT, .23, True)
        if section == 2:
            mix(track, flute(74, .38, scoop=True), origin + 7.5 * BEAT, .18, True)
    export("rooftop-loop", track, .76, loop=True)


def compose_effects():
    # Paper landing in the blue bucket: rustle followed by a small rim clink.
    pickup = silence(.24)
    mix(pickup, paper(), 0, .35)
    mix(pickup, metal(81, .18), .025, .7)
    mix(pickup, pluck(86, .13, True), .06, .3)
    export("catch", pickup, .66)

    cues = {
        "start": [(0, 62), (.085, 66), (.17, 69), (.29, 74)],
        "pause": [(0, 69), (.10, 66)],
        "resume": [(0, 66), (.09, 69), (.18, 74)],
    }
    for name, notes in cues.items():
        cue = silence(notes[-1][0] + .34)
        mix(cue, drum("rim"), 0, .18)
        for at, note in notes:
            mix(cue, pluck(note, .32, True), at, .8)
        export(name, cue, .68)

    toggle = silence(.10)
    mix(toggle, drum("rim"), 0, .6)
    mix(toggle, pluck(74, .09, True), 0, .24)
    export("toggle", toggle, .52)

    # Cartoon impact: low thump, soft debris, then the dented bucket ringing.
    explosion = silence(.82)
    low_noise = 0
    for i in range(len(explosion)):
        t = i / RATE
        low_noise = .88 * low_noise + .12 * rng.uniform(-1, 1)
        phase = TAU * (48 * t + 2.2 * (1 - math.exp(-t * 20)))
        thump = .65 * math.sin(phase) * math.exp(-t * 15)
        debris = low_noise * 2.1 * math.exp(-t * 8)
        explosion[i] = (thump + debris) * envelope(t, .82, .002, .08)
    mix(explosion, metal(50, .5), .09, .6)
    mix(explosion, drum("rim"), .27, .12)
    export("explode", explosion, .84)

    # A short drooping flute response accompanies Billu's burned expression.
    sad = silence(.86)
    phase = 0
    for i in range(round(.68 * RATE)):
        t = i / RATE
        note = 66 - 9 * (t / .68) ** .8 + .08 * math.sin(TAU * 6 * t)
        phase += TAU * hz(note) / RATE
        value = math.sin(phase) + .18 * math.sin(phase * 2)
        sad[i] = value * .3 * envelope(t, .68, .045, .12)
    mix(sad, metal(57, .16), .69, .32)
    export("sad", sad, .63)


if __name__ == "__main__":
    compose_music()
    if "--music-only" not in sys.argv:
        compose_effects()
    print(json.dumps(report, indent=2))
