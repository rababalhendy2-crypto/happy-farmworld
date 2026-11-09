"""Original sounds for Farm World, made from code (no samples, so no licence questions).
music_farm: a 32-bar cozy loop (marimba-like plucks, soft pad, bass) in C major pentatonic.
cow / sheep / chicken: cartoon animal calls built from filtered oscillators.
Writes 44.1 kHz mono WAV files; convert with ffmpeg to OGG."""
import numpy as np, wave, sys, os
SR = 44100
rng = np.random.default_rng(7)

def write(name, x, peak=0.8):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    d = (x * 32767).astype(np.int16)
    with wave.open(name, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(d.tobytes())

def t(sec): return np.arange(int(sec * SR)) / SR

def onepole(x, a):  # simple low-pass, a in (0,1): higher = darker
    y = np.empty_like(x); acc = 0.0
    for i, v in enumerate(x): acc = acc * a + v * (1 - a); y[i] = acc
    return y

def formant(x, freqs, q=8.0):
    """Band-pass resonators summed (2-pole IIR), gives vowel colour."""
    out = np.zeros_like(x)
    for f, g in freqs:
        r = np.exp(-np.pi * f / q / SR); th = 2 * np.pi * f / SR
        a1, a2 = -2 * r * np.cos(th), r * r
        y = np.zeros_like(x); y1 = y2 = 0.0
        for i, v in enumerate(x):
            y0 = (1 - r) * v - a1 * y1 - a2 * y2; y[i] = y0; y2, y1 = y1, y0
        out += g * y
    return out

def saw(phase): return 2 * (phase % 1.0) - 1

# ------------------------------------------------------------------ animals
def cow():
    d = 1.4; tt = t(d)
    f0 = 118 - 22 * tt / d + 3 * np.sin(2 * np.pi * 5 * tt) * (tt > 0.3)
    ph = np.cumsum(f0) / SR
    src = saw(ph) + 0.3 * saw(ph * 2)
    env = np.minimum(1, tt / 0.18) * np.minimum(1, (d - tt) / 0.35)
    vow = np.clip(tt / d * 1.6, 0, 1)  # "mmm" -> "ooo"
    x = formant(src, [(300, 1.0), (700, 0.35 + 0.3 * 0), (2400, 0.05)], q=6)
    x = x * env * (0.55 + 0.45 * vow)
    return onepole(x, 0.55)

def sheep():
    d = 0.9; tt = t(d)
    f0 = 330 + 25 * np.sin(2 * np.pi * 2 * tt)
    ph = np.cumsum(f0) / SR
    src = saw(ph)
    trem = 0.55 + 0.45 * np.sin(2 * np.pi * 22 * tt)   # the bleat flutter
    env = np.minimum(1, tt / 0.05) * np.minimum(1, (d - tt) / 0.25)
    x = formant(src, [(750, 1.0), (1250, 0.6), (2600, 0.15)], q=7) * trem * env
    return onepole(x, 0.3)

def chicken():
    parts = []
    for i, (gap, f) in enumerate([(0.0, 760), (0.13, 820), (0.11, 700), (0.2, 980)]):
        d = 0.09 if i < 3 else 0.22
        tt = t(d)
        ff = f * (1 + 0.25 * (1 - tt / d)) if i == 3 else f * (1 - 0.15 * tt / d)
        ph = np.cumsum(ff) / SR
        src = saw(ph) * 0.6 + rng.normal(0, 0.25, len(tt))
        env = np.minimum(1, tt / 0.008) * np.exp(-tt / (d * 0.5))
        parts.append(np.zeros(int(gap * SR))); parts.append(formant(src, [(1100, 1.0), (2500, 0.4)], q=5) * env)
    return np.concatenate(parts)

# ------------------------------------------------------------------ music
def note_hz(n): return 440.0 * 2 ** ((n - 69) / 12)

def pluck(freq, dur, vel=1.0):
    tt = t(dur)
    env = np.exp(-tt * 5.5) * np.minimum(1, tt / 0.004)
    x = (np.sin(2 * np.pi * freq * tt) + 0.35 * np.sin(2 * np.pi * freq * 4 * tt) * np.exp(-tt * 18)
         + 0.15 * np.sin(2 * np.pi * freq * 2 * tt))
    return x * env * vel

def pad(freqs, dur):
    tt = t(dur)
    x = sum(np.sin(2 * np.pi * f * tt) + 0.3 * np.sin(2 * np.pi * f * 1.003 * tt) for f in freqs)
    env = np.minimum(1, tt / 0.6) * np.minimum(1, (dur - tt) / 0.6)
    return x * env

def music():
    bpm = 92; beat = 60 / bpm; bar = 4 * beat
    chords = [  # C  Am  F  G  (each two bars), roots in MIDI
        (48, [60, 64, 67]), (45, [57, 60, 64]), (41, [57, 60, 65]), (43, [59, 62, 67])]
    scale = [60, 62, 64, 67, 69, 72, 74, 76]   # C major pentatonic, two octaves
    bars = 32
    total = int(bars * bar * SR) + SR
    mix = np.zeros(total)
    mel_rng = np.random.default_rng(11)
    motif = None
    for b in range(bars):
        root, triad = chords[(b // 2) % 4]
        start = int(b * bar * SR)
        if b % 2 == 0:
            p = pad([note_hz(n) for n in triad], 2 * bar) * 0.05
            mix[start:start + len(p)] += p
        for k in range(4):   # soft bass on beats 1 and 3, a lighter note on 2 and 4
            s = start + int(k * beat * SR)
            n = root if k % 2 == 0 else root + 7
            x = pluck(note_hz(n), beat * 0.95, 0.35 if k % 2 == 0 else 0.18)
            x = onepole(x, 0.6)
            mix[s:s + len(x)] += x
        # melody: an 8-step motif repeated with small variations, rests make room to breathe
        if b % 8 == 0 or motif is None:
            motif = [mel_rng.choice(scale) if mel_rng.random() > 0.3 else None for _ in range(8)]
        for k in range(8):
            n = motif[k]
            if n is None: continue
            if b % 4 == 3 and k >= 6: continue           # phrase ending
            if n not in [m for m in triad] + [m + 12 for m in triad] and k % 2 == 0:
                n = min(triad + [m + 12 for m in triad], key=lambda m: abs(m - n))   # chord tones on strong steps
            s = start + int(k * beat / 2 * SR)
            x = pluck(note_hz(n), beat * 1.5, 0.5 + 0.15 * (k % 2 == 0))
            mix[s:s + len(x)] += x
    loop_len = int(bars * bar * SR)
    tail = mix[loop_len:]; mix = mix[:loop_len]; mix[:len(tail)] += tail   # wrap the release into the start: seamless loop
    # gentle stereo-free reverb: a few feedback delays
    out = mix.copy()
    for dly, g in [(0.113, 0.25), (0.171, 0.2), (0.229, 0.15)]:
        n = int(dly * SR); y = out.copy(); y[n:] += out[:-n] * g; y[:n] += out[-n:] * g; out = y
    return onepole(out, 0.35)

if __name__ == '__main__':
    d = sys.argv[1]
    write(os.path.join(d, 'cow.wav'), cow(), 0.7)
    write(os.path.join(d, 'sheep.wav'), sheep(), 0.6)
    write(os.path.join(d, 'chicken.wav'), chicken(), 0.6)
    write(os.path.join(d, 'music_farm.wav'), music(), 0.5)
