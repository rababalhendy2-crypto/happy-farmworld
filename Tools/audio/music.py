"""Original cheerful farm music for Farm World (no samples, no copied melodies).
Plucked strings use Karplus-Strong (banjo / guitar / ukulele-like), bass is a soft plucked low string.
Usage: python3 music.py OUTDIR  -> writes music_country.wav and music_bouncy.wav (loopable)."""
import numpy as np, wave, sys, os
SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else '.'
rng = np.random.default_rng(3)

def write(name, x, peak=0.45):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    with wave.open(os.path.join(OUT, name + '.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes((x * 32767).astype(np.int16).tobytes())

def n2f(n): return 440.0 * 2 ** ((n - 69) / 12)

def pluck(midi, dur, bright=0.5, decay=0.996):
    """Karplus-Strong string, computed block by block (each block depends only on samples one period back)."""
    N = max(2, int(round(SR / n2f(midi)))); L = int(dur * SR)
    y = np.zeros(L + N + 1)
    burst = rng.uniform(-1, 1, N)
    k = max(1, int(1 + (1 - bright) * 6))                      # smoother burst = darker tone
    burst = np.convolve(burst, np.ones(k) / k, mode='same')
    burst -= burst.mean()                                       # no DC offset (it thumps)
    y[:N] = burst
    i = N
    while i < L + N:
        j = min(i + N, L + N)
        y[i:j] = decay * 0.5 * (y[i - N:j - N] + y[i - N - 1:j - N - 1] if i - N - 1 >= 0 else y[i - N:j - N] * 2)
        i = j
    x = y[:L]
    fade = np.clip((dur - np.arange(L) / SR) / 0.03, 0, 1)
    return x * fade

def lowpass(x, a=0.5):
    out = np.empty_like(x); acc = 0.0
    # vectorised one-pole via cumulative filter in blocks would be complex; signals are short enough
    for i in range(len(x)): acc += (x[i] - acc) * a; out[i] = acc
    return out

def place(buf, x, t, g):
    i = int(t * SR); n = min(len(x), len(buf) - i)
    if n > 0: buf[i:i + n] += x[:n] * g

# major scale degrees -> semitones
S = [0, 2, 4, 5, 7, 9, 11]
def deg(d, root=60):
    o, r = divmod(d, 7); return root + 12 * o + S[r]

def song(bpm, melody, chords, bars_per_phrase, mel_oct, inst_bright, name, swing=0.0):
    beat = 60 / bpm; total_bars = len(chords); total = total_bars * 4 * beat
    buf = np.zeros(int((total + 3) * SR))
    for bar, ch in enumerate(chords):
        t0 = bar * 4 * beat
        root = ch[0]
        # oom-pah: bass on 1 and 3, chord strums on 2 and 4
        for b, kind in ((0, 'bass'), (1, 'chord'), (2, 'bass2'), (3, 'chord')):
            t = t0 + b * beat
            if kind == 'bass': place(buf, pluck(deg(root, 36), beat * 1.6, 0.2, 0.995), t, 0.55)
            elif kind == 'bass2': place(buf, pluck(deg(root + 4, 36), beat * 1.6, 0.2, 0.995), t, 0.45)
            else:
                for k, d in enumerate(ch):
                    place(buf, pluck(deg(d, 55), beat * 0.9, 0.45, 0.993), t + k * 0.012, 0.16)
    for (b, d, l) in melody:
        t = b * beat + (swing * beat if (b * 2) % 2 == 1 else 0)
        place(buf, pluck(deg(d, mel_oct), l * beat + 0.3, inst_bright, 0.997), t, 0.42)
    n = int(total * SR); tail = buf[n:]; out = buf[:n].copy(); out[:len(tail)] += tail
    out = lowpass(out, 0.35)                                    # take the edge off
    hp = np.empty_like(out); prev_x = prev_y = 0.0             # DC blocker
    for i in range(len(out)): prev_y = out[i] - prev_x + 0.995 * prev_y; prev_x = out[i]; hp[i] = prev_y
    out = hp
    write(name, out)
    return total

# ---------------------------------------------------------------- song 1: "Sunny Fields" (country, 112 BPM)
I, IV, V, vi = (0, 2, 4), (3, 5, 7), (4, 6, 8), (5, 7, 9)
chords1 = [I, I, IV, I, I, V, V, I,  IV, IV, I, vi, IV, V, I, I] * 2
m = []
phr = [  # (beat within 8-bar phrase, scale degree, length in beats)
  (0, 4, 1), (1, 5, 0.5), (1.5, 4, 0.5), (2, 2, 1), (3, 0, 1), (4, 2, 1), (5, 4, 1), (6, 4, 2),
  (8, 5, 1), (9, 4, 0.5), (9.5, 2, 0.5), (10, 1, 2), (12, 2, 1), (13, 1, 1), (14, 0, 2),
  (16, 4, 1), (17, 5, 0.5), (17.5, 4, 0.5), (18, 2, 1), (19, 4, 1), (20, 7, 1.5), (21.5, 6, 0.5), (22, 5, 2),
  (24, 4, 1), (25, 2, 1), (26, 1, 1), (27, 2, 1), (28, 0, 4),
]
phr2 = [
  (0, 3, 1), (1, 5, 1), (2, 7, 1), (3, 5, 1), (4, 4, 1), (5, 2, 1), (6, 0, 2),
  (8, 3, 1), (9, 5, 1), (10, 7, 1), (11, 8, 1), (12, 7, 2), (14, 5, 2),
  (16, 3, 1), (17, 5, 1), (18, 4, 1), (19, 2, 1), (20, 5, 1.5), (21.5, 4, 0.5), (22, 2, 2),
  (24, 1, 1), (25, 2, 1), (26, 4, 1), (27, 1, 1), (28, 0, 4),
]
for rep in range(2):
    base = rep * 64
    m += [(base + b, d, l) for b, d, l in phr] + [(base + 32 + b, d, l) for b, d, l in phr2]
t1 = song(112, m, chords1, 8, 72, 0.6, 'music_country', swing=0.08)

# ---------------------------------------------------------------- song 2: "Little Hen Hop" (bouncy, 100 BPM, ukulele-like)
chords2 = [I, vi, IV, V] * 8
m2 = []
hop = [(0, 0, 0.5), (0.5, 2, 0.5), (1, 4, 1), (2, 4, 0.5), (2.5, 5, 0.5), (3, 4, 1),
       (4, 3, 0.5), (4.5, 5, 0.5), (5, 7, 1), (6, 5, 0.5), (6.5, 4, 0.5), (7, 2, 1),
       (8, 3, 0.5), (8.5, 4, 0.5), (9, 5, 1), (10, 4, 0.5), (10.5, 2, 0.5), (11, 1, 1),
       (12, 4, 0.5), (12.5, 3, 0.5), (13, 2, 1), (14, 1, 1), (15, 0, 1)]
for rep in range(8):
    m2 += [(rep * 16 + b, d, l) for b, d, l in hop]
t2 = song(100, m2, chords2, 4, 72, 0.5, 'music_bouncy')
print('ok', round(t1, 1), round(t2, 1))
