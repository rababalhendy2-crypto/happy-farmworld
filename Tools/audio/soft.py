"""Soft, child-friendly sounds for Farm World (0.7.5), made from code: no samples, no licence questions.
0.7.6: warm, low and short (felt piano and wooden pops), so nothing rings in the ears.
so there are no sharp edges or buzzing. Writes WAV files into the folder given as argument; convert to OGG with ffmpeg."""
import numpy as np, wave, sys, os
SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else '.'

def write(name, x, peak):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    with wave.open(os.path.join(OUT, name + '.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes((x * 32767).astype(np.int16).tobytes())

def n2f(n): return 440.0 * 2 ** ((n - 69) / 12)   # MIDI note -> Hz

# instrument: (partial ratio, amplitude, decay seconds). Warm and short: harmonic partials only, nothing rings on.
WOOD = [(1.0, 1.0, 0.09), (2.0, 0.22, 0.05), (3.0, 0.05, 0.03)]       # soft wooden "pop"
FELT = [(1.0, 1.0, 0.22), (2.0, 0.20, 0.12), (3.0, 0.05, 0.06)]       # felt piano, short
FELT_LONG = [(1.0, 1.0, 0.55), (2.0, 0.18, 0.25), (3.0, 0.04, 0.1)]   # for the music

def note(midi, dur, inst=FELT, attack=0.006, bend=0.0):
    t = np.arange(int(dur * SR)) / SR
    f = n2f(midi) * (1 + bend * np.exp(-t / 0.02))
    ph = 2 * np.pi * np.cumsum(f) / SR
    x = sum(a * np.exp(-t / d) * np.sin(ph * r) for r, a, d in inst)
    env = np.minimum(1, t / attack) ** 2
    fade = np.clip((dur - t) / 0.03, 0, 1)
    return x * env * fade

def seq(notes, total, inst=FELT):
    out = np.zeros(int(total * SR))
    for s, m, l, g in notes:
        x = note(m, l, inst) * g; i = int(s * SR)
        out[i:i + len(x)] += x[:len(out) - i]
    return out

# ------------------------------------------------------------------ UI (low and short: done in a quarter second)
write('tap', note(67, 0.07, WOOD, bend=0.08), 0.22)                             # tiny "pop", G4
write('open', note(69, 0.12, WOOD), 0.25)
write('close', note(64, 0.12, WOOD), 0.22)
write('confirm', seq([(0, 60, 0.25, 0.8), (0.07, 64, 0.25, 0.8), (0.14, 67, 0.3, 1.0)], 0.45), 0.4)   # C4 E4 G4
write('error', seq([(0, 57, 0.2, 1.0), (0.12, 53, 0.25, 1.0)], 0.4), 0.3)
# ------------------------------------------------------------------ farm
write('plant', note(60, 0.12, WOOD, bend=0.2), 0.35)
write('harvest', seq([(0, 64, 0.2, 0.8), (0.06, 67, 0.2, 0.8), (0.12, 72, 0.3, 1.0)], 0.45), 0.4)
write('collect', seq([(0, 62, 0.15, 0.8), (0.06, 67, 0.25, 1.0)], 0.35), 0.38)
write('coins', seq([(0, 72, 0.2, 0.8), (0.07, 76, 0.3, 1.0)], 0.4), 0.35)
write('levelup', seq([(0, 60, 0.3, 0.7), (0.12, 64, 0.3, 0.7), (0.24, 67, 0.3, 0.8), (0.36, 72, 0.7, 1.0),
                      (0.36, 64, 0.7, 0.35), (0.36, 67, 0.7, 0.35)], 1.1, FELT_LONG), 0.45)
write('cow', seq([(0, 55, 0.25, 1.0), (0.14, 50, 0.3, 0.9)], 0.45), 0.4)
write('sheep', seq([(0, 64, 0.12, 0.9), (0.09, 64, 0.12, 0.8), (0.18, 62, 0.2, 0.9)], 0.4), 0.35)
write('chicken', seq([(0, 72, 0.08, 0.8), (0.06, 74, 0.08, 0.8), (0.12, 72, 0.12, 0.9)], 0.26, WOOD), 0.28)

# ------------------------------------------------------------------ music: calm, low, slow loop at 72 BPM
BPM = 72; beat = 60 / BPM; bars = 32; total = bars * 4 * beat
C, D, E, G, A = 0, 2, 4, 7, 9
def p(deg, octv): return 12 * (octv + 1) + deg
phraseA = [(0, E), (1, G), (2, A), (3, G), (4, E), (6, D)]
phraseB = [(0, G), (1, A), (2, 12 + C), (3, A), (4, G), (6, E)]
phraseC = [(0, A), (1, G), (2, E), (3, D), (4, C)]
form = [phraseA, phraseB, phraseA, phraseC]
mel = []
for rep in range(4):
    for i, ph in enumerate(form):
        base = (rep * 8 + i * 2) * 4
        for b, d in ph:
            mel.append(((base + b) * beat, p(d, 4), 1.4, 0.5))
music = seq(mel, total + 2, FELT_LONG)
prog = [(C, E, G), (A - 12, C, E), (D - 12, G - 12, D), (G - 12, D, G - 12 + 12)]
acc = []
for bar in range(bars):
    ch = prog[(bar // 2) % 4]
    for k, deg in enumerate(ch):
        acc.append(((bar * 4) * beat + k * 0.04, p(deg, 3), 2.0, 0.16))
music += seq(acc, total + 2, FELT_LONG)
n = int(total * SR); tail = music[n:]; music = music[:n]; music[:len(tail)] += tail
write('music_farm', music, 0.4)
print('ok', round(total, 1), 's music')
