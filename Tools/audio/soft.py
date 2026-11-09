"""Soft, child-friendly sounds for Farm World (0.7.5), made from code: no samples, no licence questions.
Every sound is built from a few sine partials (marimba / music-box / bell) with a smooth attack and fade-out,
so there are no sharp edges or buzzing. Writes WAV files into the folder given as argument; convert to OGG with ffmpeg."""
import numpy as np, wave, sys, os
SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else '.'

def write(name, x, peak):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    with wave.open(os.path.join(OUT, name + '.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes((x * 32767).astype(np.int16).tobytes())

def n2f(n): return 440.0 * 2 ** ((n - 69) / 12)   # MIDI note -> Hz

# instrument: (partial ratio, amplitude, decay seconds)
MARIMBA = [(1.0, 1.0, 0.45), (3.93, 0.10, 0.08), (9.2, 0.015, 0.03)]
MUSICBOX = [(1.0, 1.0, 0.9), (2.0, 0.18, 0.4), (5.4, 0.03, 0.12)]
BELL = [(1.0, 1.0, 1.2), (2.76, 0.12, 0.5), (5.4, 0.03, 0.2)]

def note(midi, dur, inst=MARIMBA, attack=0.004, bend=0.0):
    t = np.arange(int(dur * SR)) / SR
    f = n2f(midi) * (1 + bend * np.exp(-t / 0.03))   # optional little pitch drop at the start ("pop")
    ph = 2 * np.pi * np.cumsum(f) / SR
    x = sum(a * np.exp(-t / d) * np.sin(ph * r) for r, a, d in inst)
    env = np.minimum(1, t / attack) ** 2
    fade = np.minimum(1, (dur - t) / 0.02)            # no click at the end
    return x * env * np.clip(fade, 0, 1)

def seq(notes, total, inst=MARIMBA, gap=0.0):
    """notes: list of (start seconds, midi, length, gain)."""
    out = np.zeros(int(total * SR))
    for s, m, l, g in notes:
        x = note(m, l, inst) * g; i = int(s * SR)
        out[i:i + len(x)] += x[:len(out) - i]
    return out

# ------------------------------------------------------------------ UI
write('tap', note(79, 0.12, MARIMBA, bend=0.06), 0.32)                         # soft wooden "tok" (G5)
write('open', seq([(0, 76, 0.25, 0.8), (0.07, 81, 0.3, 1.0)], 0.4), 0.45)      # E5 -> A5
write('close', seq([(0, 81, 0.25, 0.8), (0.07, 76, 0.3, 1.0)], 0.4), 0.4)      # A5 -> E5
write('confirm', seq([(0, 72, 0.4, 0.8), (0.08, 76, 0.4, 0.8), (0.16, 79, 0.6, 1.0)], 0.8, MUSICBOX), 0.5)
write('error', seq([(0, 69, 0.3, 1.0), (0.14, 65, 0.4, 1.0)], 0.6), 0.4)       # gentle "uh-oh" A4 -> F4, no buzz
# ------------------------------------------------------------------ farm
write('plant', note(64, 0.22, MARIMBA, bend=0.25), 0.45)                        # low soft "plop"
write('harvest', seq([(0, 79, 0.4, 0.8), (0.06, 83, 0.4, 0.8), (0.12, 86, 0.6, 1.0)], 0.8, MUSICBOX), 0.45)
write('collect', seq([(0, 74, 0.2, 0.8), (0.07, 81, 0.4, 1.0)], 0.5, MARIMBA), 0.45)
write('coins', seq([(0, 88, 0.6, 0.7), (0.09, 95, 0.8, 1.0)], 0.9, BELL), 0.35)  # two little bells
write('levelup', seq([(0, 72, 0.5, 0.7), (0.12, 76, 0.5, 0.7), (0.24, 79, 0.5, 0.8), (0.36, 84, 1.2, 1.0),
                      (0.36, 76, 1.2, 0.4), (0.36, 79, 1.2, 0.4)], 1.6, MUSICBOX), 0.5)
# animals: little tunes instead of imitated calls (low = cow, middle = sheep, high = chicken)
write('cow', seq([(0, 67, 0.5, 1.0), (0.18, 62, 0.6, 0.9)], 0.8, MARIMBA), 0.45)
write('sheep', seq([(0, 71, 0.2, 0.9), (0.1, 71, 0.2, 0.8), (0.2, 69, 0.4, 0.9)], 0.6, MARIMBA), 0.4)
write('chicken', seq([(0, 86, 0.15, 0.8), (0.08, 88, 0.15, 0.8), (0.16, 86, 0.25, 0.9)], 0.45, MARIMBA), 0.32)

# ------------------------------------------------------------------ music: calm 64-bar loop at 84 BPM
BPM = 84; beat = 60 / BPM; bars = 32; total = bars * 4 * beat
C, D, E, G, A = 0, 2, 4, 7, 9
def p(deg, octv=5): return 12 * (octv + 1) + deg   # pentatonic note in octave
phraseA = [(0, E), (1, G), (2, A), (3, G), (4, E), (5, D), (6, C), (7.5, D)]
phraseB = [(0, G), (1, A), (2, 12 + C), (3, A), (4, G), (5, E), (6, D), (7.5, E)]
phraseC = [(0, A), (1, G), (2, E), (3, D), (4, C), (5, D), (6, E), (7, C)]
form = [phraseA, phraseB, phraseA, phraseC]          # 4 phrases x 2 bars = 8 bars, repeated 4 times
mel = []
for rep in range(4):
    for i, ph in enumerate(form):
        base = (rep * 8 + i * 2) * 4
        for b, d in ph:
            mel.append(((base + b) * beat, p(d, 5), 1.2, 0.55 if rep % 2 == 0 else 0.45))
music = seq(mel, total + 2, MUSICBOX)
# soft accompaniment: chord tones on beats 1 and 3, marimba, low octave
acc = []
prog = [(C, E, G), (A, C + 12, E + 12), (D, G, A), (G, C + 12, D + 12)]   # C, Am, (sus) , G-ish: all pentatonic
for bar in range(bars):
    ch = prog[(bar // 2) % 4]
    for beat_i in (0, 2):
        for k, deg in enumerate(ch):
            acc.append(((bar * 4 + beat_i) * beat + k * 0.02, p(deg, 3), 1.4, 0.18))
    acc.append((bar * 4 * beat, p(ch[0], 2), 2.5, 0.25))                     # soft bass note
music += seq(acc, total + 2, MARIMBA)
# make it loop seamlessly: fold the tail that rings past the end back onto the start
n = int(total * SR); tail = music[n:]; music = music[:n]; music[:len(tail)] += tail
write('music_farm', music, 0.5)
print('ok', round(total, 1), 's music')
