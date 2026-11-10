"""Procedural crop models (original, CC0 by this project) for every growth stage.

Writes Assets/Resources/Models/Crops/<Model>_<stage>.obj/.mtl (stage 1..4), with y = 0 at soil level.
Leaves are two-sided ribbons with a folded midrib so they catch light like real leaves.
usage: python3 Tools/models/crops.py [out_dir]
"""
import math, os, random, sys
import numpy as np

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '../../Assets/Resources/Models/Crops')

# Linear-space Kd colours (ModelLibrary applies .gamma to OBJ colours).
def lin(hexs):
    h = hexs.lstrip('#')
    return tuple(round((int(h[i:i + 2], 16) / 255.0) ** 2.2, 5) for i in (0, 2, 4))

PAL = {
    'leaf_dark': lin('#285c18'), 'leaf': lin('#367a1f'), 'leaf_light': lin('#4d9126'), 'leaf_old': lin('#7d8a2c'),
    'stalk': lin('#4a7f25'), 'husk': lin('#6a9433'), 'kernel': lin('#f5b81c'), 'silk': lin('#9a5a24'),
    'tassel': lin('#b08a3c'), 'wheat_green': lin('#668f28'), 'wheat_gold': lin('#d39a2c'), 'wheat_stem': lin('#be8c34'),
    'carrot': lin('#e8640e'), 'fern': lin('#2a7019'), 'fern_light': lin('#3f8a22'),
    'tomato': lin('#cc1a0e'), 'tomato_green': lin('#78a82c'), 'flower_y': lin('#f4d23a'), 'stake': lin('#7d5532'),
    'pumpkin': lin('#e86a0c'), 'pumpkin_green': lin('#55822a'), 'vine': lin('#43731f'), 'pstem': lin('#5f4f25'),
    'berry': lin('#d01426'), 'berry_white': lin('#e9e2c8'), 'petal': lin('#fbfaf2'), 'seed': lin('#f3d55a'),
    'strawleaf': lin('#2c6e1f'),
}


class Mesh:
    def __init__(self):
        self.v = []; self.n = []; self.f = {}  # material -> list of (vi, ni) triples

    def tri(self, mat, a, b, c, na, nb, nc):
        base = len(self.v)
        self.v += [a, b, c]; self.n += [na, nb, nc]
        self.f.setdefault(mat, []).append((base, base + 1, base + 2))

    def add_grid(self, mat, P, N, two_sided=True, close_u=False):
        """P, N: arrays [rows][cols][3]. Rows along length, cols across."""
        P = np.asarray(P, float); N = np.asarray(N, float)
        R, C = P.shape[:2]
        cols = range(C if close_u else C - 1)
        for i in range(R - 1):
            for j in cols:
                j2 = (j + 1) % C
                a, b, c, d = P[i, j], P[i, j2], P[i + 1, j2], P[i + 1, j]
                na, nb, nc, nd = N[i, j], N[i, j2], N[i + 1, j2], N[i + 1, j]
                self.tri(mat, a, b, c, na, nb, nc); self.tri(mat, a, c, d, na, nc, nd)
                if two_sided:
                    self.tri(mat, a, c, b, -na, -nc, -nb); self.tri(mat, a, d, c, -na, -nd, -nc)

    def write(self, name):
        os.makedirs(OUT, exist_ok=True)
        with open(os.path.join(OUT, name + '.mtl'), 'w') as m:
            m.write('# Farm World procedural crop (CC0)\n')
            for mat in self.f:
                kd = PAL[mat]
                m.write('\nnewmtl %s\nNs 20.0\nKa 1 1 1\nKd %.5f %.5f %.5f\nKs 0.05 0.05 0.05\nd 1.0\nillum 2\n' % ((mat,) + kd))
        with open(os.path.join(OUT, name + '.obj'), 'w') as o:
            o.write('# Farm World procedural crop (CC0)\nmtllib %s.mtl\no %s\n' % (name, name))
            for p in self.v: o.write('v %.4f %.4f %.4f\n' % tuple(p))
            for n in self.n:
                l = np.linalg.norm(n) or 1.0
                o.write('vn %.4f %.4f %.4f\n' % tuple(np.asarray(n) / l))
            for mat, tris in self.f.items():
                o.write('usemtl %s\n' % mat)
                for t in tris:
                    o.write('f %d//%d %d//%d %d//%d\n' % tuple(x + 1 for i in t for x in (i, i)))
        return sum(len(t) for t in self.f.values())


def norm(v):
    v = np.asarray(v, float); l = np.linalg.norm(v)
    return v / l if l > 1e-9 else v


def frame_from(tangent):
    t = norm(tangent)
    up = np.array([0, 1, 0.]) if abs(t[1]) < 0.95 else np.array([1, 0, 0.])
    s = norm(np.cross(t, up)); u = np.cross(s, t)
    return s, u


def tube(mesh, mat, path, radii, sides=6, cap=True):
    """Generalised cylinder along a list of points."""
    path = [np.asarray(p, float) for p in path]
    P = []; N = []
    for i, p in enumerate(path):
        t = path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]
        s, u = frame_from(t)
        row = []; nrow = []
        for k in range(sides):
            a = 2 * math.pi * k / sides
            d = s * math.cos(a) + u * math.sin(a)
            row.append(p + d * radii[i]); nrow.append(d)
        P.append(row); N.append(nrow)
    mesh.add_grid(mat, P, N, two_sided=False, close_u=True)
    if cap and radii[-1] > 1e-4:
        c = path[-1]; t = norm(path[-1] - path[-2])
        for k in range(sides):
            mesh.tri(mat, P[-1][k], P[-1][(k + 1) % sides], c, t, t, t)


def leaf(mesh, mat, base, yaw, length, width, rise=0.9, droop=1.6, fold=0.25, segs=7, shape='blade', twist=0.0, lobes=0):
    """Ribbon leaf. rise = start angle above horizontal (rad); droop = how much it bends down over its length."""
    yaw_v = np.array([math.cos(yaw), 0, math.sin(yaw)])
    side_v = np.array([-math.sin(yaw), 0, math.cos(yaw)])
    P = []; N = []
    pos = np.asarray(base, float); ds = length / segs
    for i in range(segs + 1):
        t = i / segs
        ang = rise - droop * t * t
        tan = yaw_v * math.cos(ang) + np.array([0, 1, 0.]) * math.sin(ang)
        if shape == 'blade':
            w = width * (min(1.0, t * 6 + 0.25)) * (1 - t) ** 0.8
        elif shape == 'oval':
            w = width * math.sin(math.pi * min(t * 0.92 + 0.04, 1)) ** 0.75
        elif shape == 'round':
            w = width * math.sin(math.pi * t) ** 0.45
        else:  # spear
            w = width * math.sin(math.pi * t) ** 1.2
        if lobes:
            w *= 1 + 0.18 * math.sin(lobes * math.pi * t) ** 2
        tw = twist * t
        sd = side_v * math.cos(tw) + np.cross(tan, side_v) * math.sin(tw)
        nrm = norm(np.cross(sd, tan))
        if nrm[1] < 0: nrm = -nrm
        mid = pos + nrm * w * fold
        row = [pos - sd * w, mid, pos + sd * w]
        nl = norm(nrm + sd * 0.45); nr = norm(nrm - sd * 0.45)
        P.append(row); N.append([nl, nrm, nr])
        pos = pos + tan * ds
    mesh.add_grid(mat, P, N, two_sided=True)
    return pos


def ellipsoid(mesh, mat, c, r, rings=6, sides=8, ribs=0, rib_depth=0.0, squash_top=0.0):
    c = np.asarray(c, float); r = np.asarray(r, float)
    P = []; N = []
    for i in range(rings + 1):
        th = math.pi * i / rings
        row = []; nrow = []
        for k in range(sides):
            ph = 2 * math.pi * k / sides
            k_r = 1 - rib_depth * (0.5 - 0.5 * math.cos(ribs * ph)) if ribs else 1
            y = math.cos(th)
            if squash_top and y > 0: y *= 1 - squash_top
            d = np.array([math.sin(th) * math.cos(ph) * k_r, y, math.sin(th) * math.sin(ph) * k_r])
            row.append(c + d * r); nrow.append(norm(d / np.where(r > 0, r, 1)))
        P.append(row); N.append(nrow)
    mesh.add_grid(mat, P, N, two_sided=False, close_u=True)


def curve(p0, p1, p2, n):
    p0, p1, p2 = (np.asarray(p, float) for p in (p0, p1, p2))
    return [(1 - t) ** 2 * p0 + 2 * (1 - t) * t * p1 + t * t * p2 for t in np.linspace(0, 1, n)]


# ---------------------------------------------------------------- corn
def corn(stage):
    m = Mesh(); rnd = random.Random(10 + stage)
    H = [0, 0.32, 0.9, 1.7, 2.0][stage]
    stalk_h = [0, 0.05, 0.55, 1.45, 1.75][stage]
    r0 = [0, 0.012, 0.022, 0.03, 0.032][stage]
    pts = [np.array([0, -0.04, 0.])] + [np.array([0.01 * math.sin(i), stalk_h * i / 6, 0.]) for i in range(1, 7)]
    tube(m, 'stalk', pts, [r0 * (1.05 - 0.45 * i / 6) for i in range(7)], sides=6)
    n_leaves = [0, 4, 7, 11, 12][stage]
    for i in range(n_leaves):
        f = (i + 0.6) / n_leaves
        y = stalk_h * (f ** 1.1) * 0.92 if stage > 1 else 0.0
        L = H * (0.6 if stage == 1 else 0.42) * (0.7 + 0.5 * math.sin(math.pi * min(f * 1.1, 1)))
        mat = 'leaf_old' if stage == 4 and i < 2 else ('leaf' if i % 3 else 'leaf_dark')
        if stage >= 3 and i == n_leaves - 1: mat = 'leaf_light'
        yaw = i * math.pi + rnd.uniform(-0.45, 0.45)
        leaf(m, mat, (0, y, 0), yaw, L, L * 0.11, rise=rnd.uniform(1.15, 1.4), droop=rnd.uniform(1.5, 2.1),
             fold=0.35, segs=7, twist=rnd.uniform(-0.6, 0.6))
    if stage >= 3:  # tassel
        top = np.array([0, stalk_h, 0.])
        tube(m, 'tassel' if stage == 4 else 'leaf_light', [top, top + (0, 0.22, 0)], [0.012, 0.004], sides=5)
        nb = 7 if stage == 4 else 4
        for k in range(nb):
            a = 2 * math.pi * k / nb + rnd.uniform(-0.2, 0.2)
            d = np.array([math.cos(a), 0, math.sin(a)])
            b0 = top + (0, 0.04 + 0.02 * k / nb, 0)
            tube(m, 'tassel' if stage == 4 else 'leaf_light', curve(b0, b0 + d * 0.08 + (0, 0.1, 0), b0 + d * 0.17 + (0, 0.02, 0), 4),
                 [0.006, 0.005, 0.004, 0.002], sides=4, cap=False)
    if stage == 4:  # ears with husk, kernels and silk
        for k, (yy, a) in enumerate([(0.62, 0.4), (0.47, 0.4 + math.pi)]):
            if k == 1 and rnd.random() < 0.0: continue
            d = np.array([math.cos(a), 0, math.sin(a)])
            base = np.array([0, stalk_h * yy, 0]) + d * 0.025
            axis = norm(d * 0.9 + np.array([0, 1, 0]))
            ln = 0.2 - 0.03 * k
            path = [base + axis * ln * t for t in np.linspace(0, 1, 5)]
            tube(m, 'husk', path, [0.03, 0.06, 0.065, 0.06, 0.05], sides=8)
            tip = base + axis * ln
            s, u = frame_from(axis)
            # golden cob with kernel rows (ribbed) showing out of the opened husk
            cob = [tip - axis * 0.02 + axis * 0.2 * t for t in np.linspace(0, 1, 6)]
            tube(m, 'kernel', cob, [0.055, 0.062, 0.06, 0.055, 0.042, 0.014], sides=10)
            for j in range(4):  # husk leaves peeled open
                ang = 2 * math.pi * j / 4 + 0.4
                dirv = s * math.cos(ang) + u * math.sin(ang)
                hb = tip - axis * 0.01 + dirv * 0.045
                hp = [hb + axis * 0.13 * t + dirv * 0.05 * t * t for t in np.linspace(0, 1, 4)]
                P = [[p - np.cross(axis, dirv) * 0.03 * (1 - t), p, p + np.cross(axis, dirv) * 0.03 * (1 - t)] for p, t in zip(hp, np.linspace(0, 1, 4))]
                Nn = [[dirv, dirv, dirv]] * 4
                m.add_grid('husk', P, Nn, two_sided=True)
            st = cob[-1]
            tube(m, 'silk', curve(st, st + axis * 0.05 + (0, 0.02, 0), st + d * 0.08 + (0, -0.03, 0), 4), [0.014, 0.012, 0.008, 0.002], sides=5, cap=False)
    return m


# ---------------------------------------------------------------- wheat (one clump per model)
def wheat(stage):
    m = Mesh(); rnd = random.Random(20 + stage)
    n = [0, 9, 12, 14, 15][stage]
    H = [0, 0.3, 0.6, 0.95, 1.0][stage]
    for i in range(n):
        a = rnd.uniform(0, 2 * math.pi); rr = rnd.uniform(0, 0.06)
        base = np.array([math.cos(a) * rr, -0.03, math.sin(a) * rr])
        lean = np.array([math.cos(a), 0, math.sin(a)]) * rnd.uniform(0.04, 0.16) * H
        h = H * rnd.uniform(0.82, 1.08)
        if stage <= 2:
            mat = 'wheat_green' if i % 3 else 'leaf'
            leaf(m, mat, base, a + rnd.uniform(-0.5, 0.5), h * 1.05, 0.016 + 0.004 * stage, rise=rnd.uniform(1.2, 1.45),
                 droop=rnd.uniform(0.6, 1.4), fold=0.3, segs=5)
            continue
        ripe = stage == 4
        top = base + lean + np.array([0, h, 0])
        nod = np.array([math.cos(a), 0, math.sin(a)]) * (0.06 if ripe else 0.02)
        path = curve(base, base + np.array([0, h * 0.55, 0]) + lean * 0.2, top, 5)
        tube(m, 'wheat_stem' if ripe else 'wheat_green', path, [0.007, 0.006, 0.0055, 0.005, 0.0045], sides=3, cap=False)
        # ear: segmented spindle (spikelets) bending slightly
        ear_dir = norm(np.array([0, 1, 0]) * 0.9 + nod * 4 + lean)
        ln = 0.14 * (1 if ripe else 0.9)
        e_pts = [top + ear_dir * ln * t + nod * (t ** 2) for t in np.linspace(0, 1, 7)]
        rads = [0.014, 0.025, 0.028, 0.028, 0.025, 0.018, 0.005]
        tube(m, 'wheat_gold' if ripe else 'wheat_green', e_pts, rads, sides=5)
        for j in (3, 6):  # awns
            p = e_pts[j]; s, u = frame_from(ear_dir)
            for sgn in (-1, 1):
                aw = norm(ear_dir * 1.0 + s * 0.35 * sgn)
                tube(m, 'wheat_gold' if ripe else 'wheat_green', [p, p + aw * 0.09], [0.0025, 0.0008], sides=3, cap=False)
        if i % 2 == 0:  # a blade leaf on the stem
            leaf(m, 'wheat_stem' if ripe else 'wheat_green', base + np.array([0, h * 0.3, 0]), a + 1.4, h * 0.45, 0.012,
                 rise=0.9, droop=1.6, fold=0.3, segs=4)
    return m


# ---------------------------------------------------------------- carrot
def frond(m, mat, base, yaw, L, rnd):
    """Feathery carrot leaf: thin stem with paired leaflets."""
    d = np.array([math.cos(yaw), 0, math.sin(yaw)])
    rise = rnd.uniform(1.05, 1.35)
    pts = []
    pos = np.asarray(base, float)
    for i in range(6):
        t = i / 5; ang = rise - 1.1 * t * t
        pts.append(pos.copy()); pos = pos + (d * math.cos(ang) + np.array([0, math.sin(ang), 0])) * L / 5
    tube(m, mat, pts, [0.006, 0.005, 0.0045, 0.004, 0.003, 0.0015], sides=4, cap=False)
    for i in range(2, 6):
        for sgn in (-1, 1):
            side = yaw + sgn * 1.1
            leaf(m, 'fern_light' if (i + sgn) % 3 == 0 else 'fern', pts[i], side, L * 0.36 * (1.25 - i / 7), L * 0.09,
                 rise=0.5, droop=0.9, fold=0.25, segs=3, shape='spear', lobes=3)


def carrot(stage):
    m = Mesh(); rnd = random.Random(30 + stage)
    n = [0, 4, 6, 7, 8][stage]; L = [0, 0.16, 0.28, 0.38, 0.42][stage]
    for i in range(n):
        frond(m, 'fern', (0, 0, 0), 2 * math.pi * i / n + rnd.uniform(-0.3, 0.3), L * rnd.uniform(0.8, 1.1), rnd)
    if stage >= 2:  # orange shoulder showing above the soil, root below
        rr = [0, 0, 0.022, 0.036, 0.045][stage]
        pts = [np.array([0, 0.03 + 0.012 * stage, 0]) - np.array([0, 1, 0]) * t for t in np.linspace(0, 0.22 + 0.04 * stage, 6)]
        tube(m, 'carrot', pts, [rr * 0.8, rr, rr * 0.95, rr * 0.75, rr * 0.45, 0.002], sides=8)
        ellipsoid(m, 'carrot', pts[0], (rr * 0.8, rr * 0.25, rr * 0.8), rings=3, sides=8)
    return m


# ---------------------------------------------------------------- tomato
def compound_leaf(m, base, yaw, L, rnd, mat='leaf'):
    d = np.array([math.cos(yaw), 0, math.sin(yaw)])
    rise = rnd.uniform(0.4, 0.8)
    end = np.asarray(base, float) + (d * math.cos(rise) + np.array([0, math.sin(rise), 0])) * L * 0.5
    leaf(m, mat, end, yaw, L * 0.5, L * 0.17, rise=rise, droop=1.2, fold=0.3, segs=4, shape='oval', lobes=4)
    for sgn in (-1, 1):
        leaf(m, 'leaf_dark' if sgn < 0 else mat, np.asarray(base) + (end - base) * 0.45, yaw + sgn * 1.0, L * 0.38, L * 0.13,
             rise=rise * 0.6, droop=1.1, fold=0.3, segs=4, shape='oval', lobes=3)
    tube(m, 'stalk', [np.asarray(base, float), end], [0.008, 0.005], sides=4, cap=False)


def tomato(stage):
    m = Mesh(); rnd = random.Random(40 + stage)
    H = [0, 0.22, 0.55, 0.9, 0.95][stage]
    if stage >= 3:  # wooden stake the plant is tied to
        tube(m, 'stake', [np.array([0.05, -0.05, 0.03]), np.array([0.05, H * 1.02, 0.03])], [0.014, 0.012], sides=4)
    stems = [np.array([0, -0.03, 0.])]
    for i in range(1, 6):
        stems.append(np.array([0.02 * math.sin(i * 1.7), H * 0.92 * i / 5, 0.02 * math.cos(i * 1.3)]))
    tube(m, 'stalk', stems, [0.016, 0.014, 0.012, 0.01, 0.008, 0.005], sides=5)
    nl = [0, 3, 7, 11, 12][stage]
    for i in range(nl):
        f = (i + 0.5) / nl
        p = stems[0] + (stems[-1] - stems[0]) * f
        L = (0.16 if stage == 1 else 0.3 + 0.1 * (stage >= 3)) * (1.15 - 0.4 * f)
        compound_leaf(m, p, i * 2.4 + rnd.uniform(-0.3, 0.3), L, rnd, 'leaf_light' if i % 4 == 0 else 'leaf')
    if stage >= 3:
        nc = 4 if stage == 4 else 3
        for c in range(nc):
            a = c * 2.1 + 0.6; d = np.array([math.cos(a), 0, math.sin(a)])
            y = H * (0.3 + 0.17 * c)
            hub = np.array([0, y, 0]) + d * 0.1
            for k in range(3 if stage == 4 else 2):
                q = hub + d * 0.035 * k + np.array([0.03 * math.sin(k * 2.0), -0.04 * k, 0.03 * math.cos(k * 2.0)])
                if stage == 4:
                    r = rnd.uniform(0.036, 0.05)
                    ellipsoid(m, 'tomato' if (c + k) % 4 else 'tomato_green', q, (r, r * 0.85, r), rings=5, sides=8, ribs=5, rib_depth=0.08)
                    ellipsoid(m, 'leaf_dark', q + np.array([0, r * 0.82, 0]), (r * 0.5, 0.006, r * 0.5), rings=2, sides=5)
                else:
                    r = rnd.uniform(0.018, 0.026)
                    ellipsoid(m, 'tomato_green' if k else 'flower_y', q, (r, r * 0.9, r), rings=4, sides=6)
    return m


# ---------------------------------------------------------------- pumpkin
def pumpkin(stage):
    m = Mesh(); rnd = random.Random(50 + stage)
    nl = [0, 3, 6, 9, 9][stage]; L = [0, 0.13, 0.22, 0.3, 0.3][stage]
    for i in range(nl):
        a = 2 * math.pi * i / nl + rnd.uniform(-0.3, 0.3)
        d = np.array([math.cos(a), 0, math.sin(a)])
        spread = (0.0 if stage == 1 else rnd.uniform(0.08, 0.3))
        b = d * spread
        tube(m, 'vine', curve((0, 0.01, 0), b * 0.5 + (0, 0.04, 0), b + (0, 0.02, 0), 4), [0.012, 0.011, 0.01, 0.008], sides=4, cap=False)
        stem_top = b + np.array([0, L * 0.38, 0])
        tube(m, 'vine', [b + (0, 0.02, 0), stem_top], [0.007, 0.006], sides=4, cap=False)
        leaf(m, 'leaf' if i % 3 else 'leaf_dark', stem_top, a + rnd.uniform(-0.4, 0.4), L * 0.7, L * 0.42,
             rise=0.45, droop=1.0, fold=0.18, segs=6, shape='round', lobes=5)
    if stage >= 3:
        big = stage == 4
        r = 0.2 if big else 0.11
        c = np.array([0.26 if big else 0.3, r * 0.75, -0.05])
        ellipsoid(m, 'pumpkin' if big else 'pumpkin_green', c, (r, r * 0.78, r), rings=8, sides=20, ribs=10, rib_depth=0.12, squash_top=0.15)
        top = c + np.array([0, r * 0.62, 0])
        tube(m, 'pstem', curve(top - (0, 0.02, 0), top + (0, 0.05, 0), top + (0.03, 0.08, 0.01), 4), [0.022, 0.02, 0.017, 0.015], sides=6)
    return m


# ---------------------------------------------------------------- strawberry (BushBerries)
def strawberry(stage):
    m = Mesh(); rnd = random.Random(60 + stage)
    n = [0, 3, 6, 9, 10][stage]; L = [0, 0.12, 0.2, 0.26, 0.27][stage]
    for i in range(n):
        a = 2 * math.pi * i / n + rnd.uniform(-0.3, 0.3)
        d = np.array([math.cos(a), 0, math.sin(a)])
        rise = rnd.uniform(0.9, 1.25)
        inner = i % 2 == 0
        top = d * L * (0.12 if inner else 0.32) + np.array([0, L * (1.0 if inner else 0.7) * math.sin(rise), 0])
        tube(m, 'vine', curve((0, -0.01, 0), d * L * 0.1 + (0, L * 0.6, 0), top, 4), [0.006, 0.005, 0.0045, 0.004], sides=4, cap=False)
        for k in (-1, 0, 1):  # trifoliate
            leaf(m, 'strawleaf' if (i + k) % 3 else 'leaf', top, a + k * 0.9, L * 0.4, L * 0.2, rise=0.55, droop=0.9, fold=0.3,
                 segs=4, shape='oval', lobes=6)
    if stage >= 3:
        for c in range(5 if stage == 4 else 4):
            a = c * 1.3 + 0.4; d = np.array([math.cos(a), 0, math.sin(a)])
            p = d * L * rnd.uniform(0.45, 0.6) + np.array([0, 0.04, 0])
            tube(m, 'vine', curve((0, 0.02, 0), d * L * 0.3 + (0, L * 0.5, 0), p + (0, 0.03, 0), 4), [0.004] * 4, sides=3, cap=False)
            if stage == 4 and c != 4:
                r = rnd.uniform(0.03, 0.038)
                ellipsoid(m, 'berry' if c % 3 else 'berry', p - (0, r * 0.4, 0), (r, r * 1.25, r), rings=6, sides=8)
                # cone tip: squash bottom by adding a small sphere lower
                ellipsoid(m, 'berry', p - (0, r * 1.25, 0), (r * 0.55, r * 0.6, r * 0.55), rings=4, sides=6)
                for k in range(5):  # sepals
                    aa = 2 * math.pi * k / 5
                    leaf(m, 'leaf_dark', p + (0, r * 0.75, 0), aa, r * 0.9, r * 0.35, rise=0.2, droop=0.6, fold=0.1, segs=2, shape='spear')
                for k in range(6):  # seeds
                    aa = 2 * math.pi * k / 6 + 0.3
                    q = p - (0, r * (0.3 + 0.35 * (k % 2)), 0) + np.array([math.cos(aa), 0, math.sin(aa)]) * r * 0.97
                    ellipsoid(m, 'seed', q, (0.003, 0.004, 0.003), rings=2, sides=4)
            else:  # white flower
                for k in range(5):
                    aa = 2 * math.pi * k / 5
                    ellipsoid(m, 'petal', p + np.array([math.cos(aa), 0, math.sin(aa)]) * 0.016, (0.013, 0.003, 0.013), rings=2, sides=6)
                ellipsoid(m, 'flower_y', p + (0, 0.003, 0), (0.008, 0.005, 0.008), rings=3, sides=6)
    return m


GEN = {'Corn': corn, 'Wheat': wheat, 'Carrot': carrot, 'Tomato': tomato, 'Pumpkin': pumpkin, 'BushBerries': strawberry}

if __name__ == '__main__':
    heights = []
    for name, g in GEN.items():
        for s in range(1, 5):
            mesh = g(s)
            tris = mesh.write('%s_%d' % (name, s))
            top = max(v[1] for v in mesh.v)
            heights.append('            ["%s_%d"] = %.4ff,' % (name, s, top))
            print('%s_%d: %d tris, %.2f m' % (name, s, tris, top))
    # Unity's import scale for OBJ is not something the game should depend on: FieldPlot sizes each model to this
    # authored height (Fit.Height), which keeps the real proportions between stages and crops.
    cs = os.path.join(os.path.dirname(__file__), '../../Assets/Scripts/Farming/CropHeights.cs')
    with open(cs, 'w') as f:
        f.write('// Generated by Tools/models/crops.py - do not edit.\nusing System.Collections.Generic;\n\n'
                'namespace FarmWorld.Farming\n{\n    public static class CropHeights\n    {\n'
                '        /// <summary>Height in metres (soil to top) of each crop model as authored.</summary>\n'
                '        public static readonly Dictionary<string, float> Metres = new()\n        {\n'
                + '\n'.join(heights) + '\n        };\n    }\n}\n')
