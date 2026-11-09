"""Bake skinned glTF animations into static OBJ frames (one OBJ per frame) with flat colour materials.

Unity imports OBJ natively in this project, so animated characters and pets become a short flipbook of
meshes. Textures (Kenney colour maps) are sampled per face into MTL colours.

usage: python3 -I bake.py in.glb outdir name anim:frames[:start-end] ...
   e.g. bake.py mini_male_a.dq.glb out Farmer walk:8 idle:4
"""
import json, struct, sys, os, math
import numpy as np
from PIL import Image
import io

def load(path):
    b = open(path, 'rb').read()
    jl = struct.unpack('<I', b[12:16])[0]
    j = json.loads(b[20:20 + jl])
    off = 20 + jl
    bl = struct.unpack('<I', b[off:off + 4])[0]
    binc = b[off + 8: off + 8 + bl]
    return j, binc

CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}

def acc(j, binc, i):
    a = j['accessors'][i]
    bv = j['bufferViews'][a['bufferView']]
    dt = np.dtype(CT[a['componentType']])
    n = NC[a['type']]
    start = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = bv.get('byteStride', 0) or dt.itemsize * n
    cnt = a['count']
    raw = np.frombuffer(binc, dtype=np.uint8, count=stride * (cnt - 1) + dt.itemsize * n, offset=start)
    out = np.zeros((cnt, n), dtype=np.float64)
    for k in range(cnt):
        out[k] = np.frombuffer(raw[k * stride: k * stride + dt.itemsize * n].tobytes(), dtype=dt)
    if a.get('normalized'):
        if dt == np.uint8: out /= 255.0
        elif dt == np.uint16: out /= 65535.0
        elif dt == np.int8: out = np.maximum(out / 127.0, -1)
        elif dt == np.int16: out = np.maximum(out / 32767.0, -1)
    return out

def quat_mat(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])

def trs(t, r, s):
    m = np.eye(4)
    m[:3, :3] = quat_mat(r) * np.array(s)[None, :]
    m[:3, 3] = t
    return m

def slerp(a, b, f):
    a = np.array(a); b = np.array(b)
    d = np.dot(a, b)
    if d < 0: b = -b; d = -d
    if d > 0.9995:
        r = a + f * (b - a); return r / np.linalg.norm(r)
    th = math.acos(d)
    return (math.sin((1 - f) * th) * a + math.sin(f * th) * b) / math.sin(th)

def main():
    args = sys.argv[1:]
    K = 8; HAT = None
    if '--k' in args: i = args.index('--k'); K = int(args[i + 1]); del args[i:i + 2]
    if '--hat' in args: i = args.index('--hat'); HAT = args[i + 1]; del args[i:i + 2]
    src, outdir, name = args[0], args[1], args[2]
    specs = args[3:]
    j, binc = load(src)
    os.makedirs(outdir, exist_ok=True)
    nodes = j['nodes']
    parent = {}
    for i, n in enumerate(nodes):
        for c in n.get('children', []): parent[c] = i
    base = []
    for n in nodes:
        if 'matrix' in n:
            m = np.array(n['matrix']).reshape(4, 4).T
            base.append(('m', m))
        else:
            base.append(('trs', list(n.get('translation', [0, 0, 0])), list(n.get('rotation', [0, 0, 0, 1])), list(n.get('scale', [1, 1, 1]))))

    # textures
    images = []
    for im in j.get('images', []):
        bv = j['bufferViews'][im['bufferView']]
        data = binc[bv.get('byteOffset', 0): bv.get('byteOffset', 0) + bv['byteLength']]
        images.append(np.array(Image.open(io.BytesIO(data)).convert('RGB')))
    def mat_color(mi, uv):
        m = j['materials'][mi] if mi is not None else {}
        pbr = m.get('pbrMetallicRoughness', {})
        f = np.array(pbr.get('baseColorFactor', [1, 1, 1, 1])[:3])
        bt = pbr.get('baseColorTexture')
        if bt is not None and uv is not None:
            img = images[j['textures'][bt['index']]['source']]
            u, v = uv
            tt = bt.get('extensions', {}).get('KHR_texture_transform')
            if tt:
                sc = tt.get('scale', [1, 1]); of = tt.get('offset', [0, 0])
                u = u * sc[0] + of[0]; v = v * sc[1] + of[1]
            h, w = img.shape[:2]
            x = int((u % 1.0) * w) % w; y = int((v % 1.0) * h) % h
            f = f * img[y, x] / 255.0
        return f

    anims = {a.get('name', str(i)): a for i, a in enumerate(j.get('animations', []))}
    def find_anim(key):
        for k, a in anims.items():
            if k.split('|')[-1].lower() == key.lower(): return a
        for k, a in anims.items():
            if key.lower() in k.lower(): return a
        raise SystemExit('no anim ' + key + ' in ' + ', '.join(anims))

    def pose(anim, t):
        local = []
        for b in base:
            local.append(list(b))
        if anim is not None:
            for ch in anim['channels']:
                tgt = ch['target']; node = tgt.get('node'); path = tgt['path']
                if node is None or path == 'weights': continue
                smp = anim['samplers'][ch['sampler']]
                ts = acc(j, binc, smp['input'])[:, 0]
                vs = acc(j, binc, smp['output'])
                cubic = smp.get('interpolation') == 'CUBICSPLINE'
                if cubic: vs = vs[1::3]
                if t <= ts[0]: v = vs[0]
                elif t >= ts[-1]: v = vs[-1]
                else:
                    k = np.searchsorted(ts, t) - 1
                    f = (t - ts[k]) / (ts[k + 1] - ts[k])
                    if smp.get('interpolation') == 'STEP': v = vs[k]
                    elif path == 'rotation': v = slerp(vs[k], vs[k + 1], f)
                    else: v = vs[k] * (1 - f) + vs[k + 1] * f
                if local[node][0] == 'm': continue
                idx = {'translation': 1, 'rotation': 2, 'scale': 3}[path]
                local[node][idx] = list(v)
        mats = [None] * len(nodes)
        def world(i):
            if mats[i] is not None: return mats[i]
            b = local[i]
            m = b[1] if b[0] == 'm' else trs(b[1], b[2], b[3])
            if i in parent: m = world(parent[i]) @ m
            mats[i] = m
            return m
        for i in range(len(nodes)): world(i)
        return mats

    # collect primitives once
    prims = []
    for ni, n in enumerate(nodes):
        if 'mesh' not in n: continue
        mesh = j['meshes'][n['mesh']]
        for p in mesh['primitives']:
            at = p['attributes']
            pos = acc(j, binc, at['POSITION'])
            uv = acc(j, binc, at['TEXCOORD_0']) if 'TEXCOORD_0' in at else None
            jo = acc(j, binc, at['JOINTS_0']).astype(int) if 'JOINTS_0' in at else None
            we = acc(j, binc, at['WEIGHTS_0']) if 'WEIGHTS_0' in at else None
            idx = acc(j, binc, p['indices'])[:, 0].astype(int) if 'indices' in p else np.arange(len(pos))
            tris = idx.reshape(-1, 3)
            cols = []
            for tri in tris:
                c = uv[tri].mean(axis=0) if uv is not None else None
                cols.append(mat_color(p.get('material'), c))
            prims.append(dict(node=ni, skin=n.get('skin'), pos=pos, jo=jo, we=we, tris=tris, cols=np.array(cols)))

    skins = []
    for s in j.get('skins', []):
        ibm = acc(j, binc, s['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1) if 'inverseBindMatrices' in s else np.tile(np.eye(4), (len(s['joints']), 1, 1))
        skins.append((s['joints'], ibm))

    if HAT is not None:
        # a straw hat, skinned 100 % to the head joint, sized from the head mesh in bind pose
        si = next(i for i, sk in enumerate(skins) if True)
        joints, _ = skins[si]
        hj = next(k for k, jn in enumerate(joints) if nodes[jn].get('name') == HAT)
        headp = [p for p in prims if nodes[p['node']].get('name', '').startswith(HAT)]
        bindm = pose(None, 0)
        _, ibm_h = skins[si]
        M0 = bindm[joints[hj]] @ ibm_h[hj]
        def skinned(p):
            joints2, ibm2 = skins[p['skin']]
            jm = np.array([bindm[jn] @ ibm2[k] for k, jn in enumerate(joints2)])
            pos = np.c_[p['pos'], np.ones(len(p['pos']))]
            out = np.zeros((len(pos), 4))
            for k in range(4): out += p['we'][:, k:k + 1] * np.einsum('nij,nj->ni', jm[p['jo'][:, k]], pos)
            return out[:, :3]
        hp = np.concatenate([skinned(p) for p in headp])
        lo, hi = hp.min(0), hp.max(0)
        cx, cz = (lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2
        w = max(hi[0] - lo[0], hi[2] - lo[2]); top = hi[1]; hh = hi[1] - lo[1]
        verts = []; tris = []; cols = []
        def cyl(r, y0, y1, col, seg=18):
            b = len(verts)
            for k in range(seg):
                a = 2 * math.pi * k / seg
                verts.append([cx + r * math.cos(a), y0, cz + r * math.sin(a)])
                verts.append([cx + r * math.cos(a), y1, cz + r * math.sin(a)])
            verts.append([cx, y0, cz]); verts.append([cx, y1, cz])
            c0, c1 = len(verts) - 2, len(verts) - 1
            for k in range(seg):
                a0, a1 = b + 2 * k, b + 2 * ((k + 1) % seg)
                for t in ([a0, a1, a0 + 1], [a1, a1 + 1, a0 + 1], [c0, a1, a0], [c1, a0 + 1, a1 + 1]):
                    tris.append(t); cols.append(col)
        straw = np.array([0.93, 0.78, 0.42]); band = np.array([0.62, 0.22, 0.15])
        yb = top - hh * 0.18
        cyl(w * 0.74, yb, yb + hh * 0.05, straw)
        cyl(w * 0.47, yb + hh * 0.05, yb + hh * 0.14, band)
        cyl(w * 0.45, yb + hh * 0.14, yb + hh * 0.36, straw)
        vw = np.c_[np.array(verts), np.ones(len(verts))]
        verts = (np.linalg.inv(M0) @ vw.T).T[:, :3]
        n = len(verts)
        jo = np.zeros((n, 4), dtype=int); jo[:, 0] = hj
        we = np.zeros((n, 4)); we[:, 0] = 1
        prims.append(dict(node=headp[0]['node'] if headp else prims[0]['node'], skin=si, pos=np.array(verts), jo=jo, we=we,
                          tris=np.array(tris), cols=np.array(cols)))

    # one shared palette over all frames (same materials in the same order), k-means to K colours
    allcols = np.concatenate([p['cols'] for p in prims])
    uniq = np.unique(np.round(allcols * 40) / 40, axis=0)
    if len(uniq) <= K:
        centers = uniq
    else:
        rng = np.random.default_rng(1)
        centers = uniq[rng.choice(len(uniq), K, replace=False)]
        for _ in range(30):
            lab = np.argmin(((allcols[:, None, :] - centers[None]) ** 2).sum(-1), 1)
            for k in range(K):
                if (lab == k).any(): centers[k] = allcols[lab == k].mean(0)
    palette = [tuple(np.round(c, 3)) for c in centers]
    def nearest(c):
        return int(np.argmin(((centers - np.array(c)) ** 2).sum(-1)))
    for p in prims: p['lab'] = [nearest(c) for c in p['cols']]

    def write(fname, mats):
        V = []; F = {i: [] for i in range(len(palette))}
        for p in prims:
            pos = np.c_[p['pos'], np.ones(len(p['pos']))]
            if p['skin'] is not None and p['jo'] is not None:
                joints, ibm = skins[p['skin']]
                jm = np.array([mats[jn] @ ibm[k] for k, jn in enumerate(joints)])
                out = np.zeros((len(pos), 4))
                for k in range(4):
                    out += p['we'][:, k:k + 1] * np.einsum('nij,nj->ni', jm[p['jo'][:, k]], pos)
            else:
                out = (mats[p['node']] @ pos.T).T
            basei = len(V)
            V.extend(out[:, :3].tolist())
            for ti, tri in enumerate(p['tris']):
                F[p['lab'][ti]].append([basei + tri[0], basei + tri[1], basei + tri[2]])
        with open(os.path.join(outdir, fname + '.obj'), 'w') as f:
            f.write('mtllib %s.mtl\no %s\n' % (name, name))
            for v in V: f.write('v %.4f %.4f %.4f\n' % (v[0], v[1], v[2]))   # OBJ and glTF are both right-handed; Unity's importer converts
            for mi in range(len(palette)):
                if not F[mi]: continue
                f.write('usemtl c%d\n' % mi)
                for t in F[mi]: f.write('f %d %d %d\n' % (t[0] + 1, t[1] + 1, t[2] + 1))
        return V

    with open(os.path.join(outdir, name + '.mtl'), 'w') as f:
        for i, c in enumerate(palette):
            f.write('newmtl c%d\nKd %.3f %.3f %.3f\nKa 0 0 0\nKs 0 0 0\nd 1\n\n' % (i, *[min(1, x) for x in c]))

    for spec in specs:
        parts = spec.split(':')
        aname, frames = parts[0], int(parts[1])
        if aname == 'bind':
            write(name, pose(None, 0)); continue
        a = find_anim(aname)
        tmax = max(acc(j, binc, s['input'])[-1, 0] for s in a['samplers'])
        for fi in range(frames):
            t = tmax * fi / frames
            write('%s_%s_%d' % (name, aname.lower(), fi), pose(a, t))
        print(spec, 'duration %.2fs' % tmax)

if __name__ == '__main__':
    main()
