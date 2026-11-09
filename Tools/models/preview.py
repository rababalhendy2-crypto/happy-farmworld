"""Render OBJ files (with MTL colours) to a PNG grid, iso-ish view, flat shading. usage: preview.py out.png a.obj b.obj ..."""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw

def read(path):
    mtl = {}; V = []; F = []; cur = None
    d = os.path.dirname(path)
    for line in open(path):
        p = line.split()
        if not p: continue
        if p[0] == 'mtllib':
            for l2 in open(os.path.join(d, p[1])):
                q = l2.split()
                if not q: continue
                if q[0] == 'newmtl': name = q[1]
                if q[0] == 'Kd': mtl[name] = tuple(float(x) for x in q[1:4])
        elif p[0] == 'v': V.append([float(x) for x in p[1:4]])
        elif p[0] == 'usemtl': cur = p[1]
        elif p[0] == 'f': F.append(([int(x.split('/')[0]) - 1 for x in p[1:4]], mtl.get(cur, (0.7, 0.7, 0.7))))
    return np.array(V), F

def render(path, size=300, yaw=35, pitch=25):
    V, F = read(path)
    V = V - (V.min(0) + V.max(0)) / 2
    y = math.radians(yaw); x = math.radians(pitch)
    R1 = np.array([[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]])
    R2 = np.array([[1, 0, 0], [0, math.cos(x), -math.sin(x)], [0, math.sin(x), math.cos(x)]])
    P = V @ R1.T @ R2.T
    s = size * 0.42 / max(np.abs(P[:, :2]).max(), 1e-6)
    img = Image.new('RGB', (size, size), (235, 240, 230)); dr = ImageDraw.Draw(img)
    light = np.array([0.4, 0.8, -0.5]); light /= np.linalg.norm(light)
    order = sorted(F, key=lambda f: -P[f[0]][:, 2].mean())
    for idx, col in order:
        a, b, c = P[idx]
        n = np.cross(b - a, c - a); ln = np.linalg.norm(n)
        if ln == 0: continue
        n /= ln
        sh = 0.45 + 0.55 * abs(np.dot(n, light))
        pts = [(size / 2 + p[0] * s, size / 2 - p[1] * s) for p in (a, b, c)]
        dr.polygon(pts, fill=tuple(int(min(1, ch * sh) * 255) for ch in col))
    return img

if __name__ == '__main__':
    out = sys.argv[1]; files = sys.argv[2:]
    ims = [render(f) for f in files]
    W = Image.new('RGB', (300 * len(ims), 300), 'white')
    for i, im in enumerate(ims): W.paste(im, (300 * i, 0))
    W.save(out)
