# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""A small test scene for the modern renderer's features (ours).

Writes a .glb, all of it generated here: a cobbled ground and a brick wall whose materials carry height
maps in their extras (the shared contract's `"extras": {"height": {"index", "scale"}}`), a smooth puddle
and a box (for screen-space reflections and shadows), and clumps of a plant made of crossed cards with an
alpha leaf texture (`MASK`, two-sided, `"extras": {"foliage": {"translucency", "wind"}}`, COLOR_0's alpha
the sway weight: 0 at the root, 1 at the tips). Usage: python3 testscene.py <out.glb>
"""
import io
import json
import struct
import sys

import numpy as np
from PIL import Image

RNG = np.random.default_rng(141)
N = 512


def png(a, mode):
    b = io.BytesIO()
    Image.fromarray(a, mode).save(b, 'PNG')
    return b.getvalue()


def normal_from_height(h, span_px):
    """A tangent-space normal map (glTF: +Y up the texture) from a height map, span_px texels per unit height."""
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * span_px
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * span_px
    n = np.dstack([-gx, gy, np.ones_like(h)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)


def blur(a, r):
    k = np.exp(-np.arange(-2 * r, 2 * r + 1) ** 2 / (2.0 * r * r)); k /= k.sum()
    for ax in (0, 1):
        a = sum(np.roll(a, i - 2 * r, ax) * w for i, w in enumerate(k))
    return a


def cobbles():
    """Voronoi stones, domed, with dark sunken joints. Returns height 0..1, albedo RGB, roughness."""
    pts = RNG.random((90, 2)) * N
    yy, xx = np.mgrid[0:N, 0:N].astype(float)
    d1 = np.full((N, N), 1e9); d2 = np.full((N, N), 1e9); idx = np.zeros((N, N), int)
    for i, (px, py) in enumerate(pts):
        for ox in (-N, 0, N):
            for oy in (-N, 0, N):
                d = np.hypot(xx - px - ox, yy - py - oy)
                closer = d < d1
                d2 = np.where(closer, d1, np.minimum(d2, d))
                idx = np.where(closer, i, idx); d1 = np.where(closer, d, d1)
    edge = (d2 - d1)
    h = np.clip(edge / 14.0, 0, 1) ** 0.6
    h = blur(h, 2) + (RNG.random((N, N)) - 0.5) * 0.04
    h = (h - h.min()) / (h.max() - h.min())
    tint = RNG.random((len(pts), 3)) * 0.15
    stone = np.array([0.42, 0.39, 0.35]) + tint[idx] - 0.07
    joint = np.array([0.16, 0.14, 0.12])
    w = np.clip(h * 3.0, 0, 1)[..., None]
    alb = stone * w + joint * (1 - w)
    alb *= (0.9 + 0.2 * blur(RNG.random((N, N)), 3))[..., None]
    rough = 0.95 - 0.25 * w[..., 0]
    return h, alb, rough


def bricks():
    h = np.zeros((N, N)); alb = np.zeros((N, N, 3))
    rows, cols = 8, 4
    bh, bw = N // rows, N // cols
    for r in range(rows):
        off = (bw // 2) * (r % 2)
        for c in range(-1, cols + 1):
            x0, y0 = c * bw + off, r * bh
            col = np.array([0.55, 0.30, 0.22]) + (RNG.random(3) - 0.5) * 0.12
            for y in range(y0 + 4, y0 + bh - 4):
                xs = np.arange(x0 + 4, x0 + bw - 4) % N
                fy = min(y - y0 - 4, y0 + bh - 5 - y) / 6.0
                fx = np.minimum(np.arange(len(xs)), np.arange(len(xs))[::-1]) / 6.0
                h[y, xs] = np.clip(np.minimum(fx, fy), 0, 1) * 0.85 + 0.15
                alb[y, xs] = col
    mortar = h <= 0.0
    alb[mortar] = (0.62, 0.60, 0.55)
    h = blur(h, 1) + (RNG.random((N, N)) - 0.5) * 0.05
    h = (h - h.min()) / (h.max() - h.min())
    alb *= (0.9 + 0.2 * blur(RNG.random((N, N)), 2))[..., None]
    return h, alb, np.where(mortar, 0.95, 0.8)


def leaves():
    """Blades of grass and a few broad leaves on a transparent card; alpha 1 inside them."""
    img = np.zeros((N, N, 4), np.uint8)
    yy, xx = np.mgrid[0:N, 0:N].astype(float)
    for _ in range(26):
        x0 = RNG.uniform(40, N - 40); lean = RNG.uniform(-0.35, 0.35); top = RNG.uniform(0.35, 0.95) * N; width = RNG.uniform(5, 11)
        t = np.clip((N - yy) / top, 0, 1)
        cx = x0 + lean * (N - yy) * (N - yy) / top
        inside = (np.abs(xx - cx) < width * (1 - t) + 0.5) & (N - yy < top)
        g = np.array([0.20, 0.36, 0.10]) * RNG.uniform(0.75, 1.25) * (0.7 + 0.5 * t[..., None])
        img[inside, :3] = (np.clip(g[inside], 0, 1) * 255).astype(np.uint8)
        img[inside, 3] = 255
    return img


class Glb:
    def __init__(self):
        self.j = dict(asset=dict(version='2.0', generator='Vitomir tools/render/testscene.py'), scene=0, scenes=[dict(nodes=[])],
                      nodes=[], meshes=[], materials=[], textures=[], images=[], samplers=[dict(wrapS=10497, wrapT=10497)],
                      accessors=[], bufferViews=[], buffers=[])
        self.bin = bytearray()

    def view(self, data, target=None):
        while len(self.bin) % 4: self.bin.append(0)
        v = dict(buffer=0, byteOffset=len(self.bin), byteLength=len(data))
        if target: v['target'] = target
        self.bin += data
        self.j['bufferViews'].append(v)
        return len(self.j['bufferViews']) - 1

    def acc(self, a, kind, comp=5126, target=34962, norm=False):
        a = np.ascontiguousarray(a)
        v = self.view(a.tobytes(), target)
        d = dict(bufferView=v, componentType=comp, count=int(a.shape[0]), type=kind)
        if norm: d['normalized'] = True
        if kind == 'VEC3' and comp == 5126: d['min'] = a.min(0).tolist(); d['max'] = a.max(0).tolist()
        self.j['accessors'].append(d)
        return len(self.j['accessors']) - 1

    def image(self, data):
        v = self.view(data)
        self.j['images'].append(dict(bufferView=v, mimeType='image/png'))
        self.j['textures'].append(dict(source=len(self.j['images']) - 1, sampler=0))
        return len(self.j['textures']) - 1

    def material(self, **m):
        self.j['materials'].append(m)
        return len(self.j['materials']) - 1

    def mesh(self, name, P, N_, UV, I, mat, C=None):
        """P, N in metres, Z up (converted to glTF's Y up here)."""
        g = lambda v: np.stack([v[:, 0], v[:, 2], -v[:, 1]], 1).astype(np.float32)
        attrs = dict(POSITION=self.acc(g(P), 'VEC3'), NORMAL=self.acc(g(N_), 'VEC3'), TEXCOORD_0=self.acc(UV.astype(np.float32), 'VEC2'))
        if C is not None: attrs['COLOR_0'] = self.acc(C.astype(np.float32), 'VEC4')
        prim = dict(attributes=attrs, indices=self.acc(I.astype(np.uint32).reshape(-1), 'SCALAR', 5125, 34963), material=mat)
        self.j['meshes'].append(dict(name=name, primitives=[prim]))
        self.j['nodes'].append(dict(name=name, mesh=len(self.j['meshes']) - 1))
        self.j['scenes'][0]['nodes'].append(len(self.j['nodes']) - 1)

    def write(self, path):
        while len(self.bin) % 4: self.bin.append(0)
        self.j['buffers'] = [dict(byteLength=len(self.bin))]
        js = json.dumps(self.j).encode()
        while len(js) % 4: js += b' '
        with open(path, 'wb') as f:
            f.write(struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(self.bin)))
            f.write(struct.pack('<II', len(js), 0x4E4F534A)); f.write(js)
            f.write(struct.pack('<II', len(self.bin), 0x004E4942)); f.write(self.bin)


def quad(o, u, v, n, uvscale):
    o, u, v, n = map(np.asarray, (o, u, v, n))
    P = np.array([o, o + u, o + u + v, o + v], float)
    N_ = np.tile(n, (4, 1)).astype(float)
    UV = np.array([[0, 0], [1, 0], [1, 1], [0, 1]], float) * uvscale
    UV[:, 1] = uvscale[1] - UV[:, 1]
    return P, N_, UV, np.array([[0, 1, 2], [0, 2, 3]])


def box(c, s):
    Ps, Ns, UVs, Is = [], [], [], []
    c = np.asarray(c, float); s = np.asarray(s, float)
    for axis in range(3):
        for sign in (-1, 1):
            n = np.zeros(3); n[axis] = sign
            a, b = [(1, 2), (2, 0), (0, 1)][axis]
            u = np.zeros(3); u[a] = s[a]; v = np.zeros(3); v[b] = s[b]
            if sign < 0: u, v = v, u
            o = c + n * s / 2 - u / 2 - v / 2
            P, N_, UV, I = quad(o, u, v, n, (1, 1))
            Is.append(I + 4 * len(Ps)); Ps.append(P); Ns.append(N_); UVs.append(UV)
    return np.vstack(Ps), np.vstack(Ns), np.vstack(UVs), np.vstack(Is)


def main(out):
    g = Glb()
    h, alb, rough = cobbles()
    mr = np.dstack([np.full((N, N), 255), (rough * 255), np.zeros((N, N))]).astype(np.uint8)
    cob = g.material(name='cobble_test', pbrMetallicRoughness=dict(baseColorTexture=dict(index=g.image(png((np.clip(alb, 0, 1) ** (1 / 2.2) * 255).astype(np.uint8), 'RGB'))),
                                                                    metallicRoughnessTexture=dict(index=g.image(png(mr, 'RGB'))), metallicFactor=0.0),
                     normalTexture=dict(index=g.image(png(normal_from_height(h, 8.0), 'RGB'))),
                     extras=dict(height=dict(index=g.image(png((h * 255).astype(np.uint8), 'L')), scale=0.045)))
    h, alb, rough = bricks()
    mr = np.dstack([np.full((N, N), 255), (rough * 255), np.zeros((N, N))]).astype(np.uint8)
    brk = g.material(name='brick_test', pbrMetallicRoughness=dict(baseColorTexture=dict(index=g.image(png((np.clip(alb, 0, 1) ** (1 / 2.2) * 255).astype(np.uint8), 'RGB'))),
                                                                   metallicRoughnessTexture=dict(index=g.image(png(mr, 'RGB'))), metallicFactor=0.0),
                     normalTexture=dict(index=g.image(png(normal_from_height(h, 6.0), 'RGB'))),
                     extras=dict(height=dict(index=g.image(png((h * 255).astype(np.uint8), 'L')), scale=0.03)))
    puddle = g.material(name='puddle_test', pbrMetallicRoughness=dict(baseColorFactor=[0.05, 0.05, 0.05, 1], metallicFactor=0.0, roughnessFactor=0.04))
    plain = g.material(name='plaster_test', pbrMetallicRoughness=dict(baseColorFactor=[0.75, 0.70, 0.62, 1], metallicFactor=0.0, roughnessFactor=0.9))
    leaf = g.material(name='leaves_test', alphaMode='MASK', alphaCutoff=0.5, doubleSided=True,
                      pbrMetallicRoughness=dict(baseColorTexture=dict(index=g.image(png(leaves(), 'RGBA'))), metallicFactor=0.0, roughnessFactor=0.7),
                      extras=dict(foliage=dict(translucency=0.6, wind=1.0)))
    # The ground: 16 m square, the cobbles 1.5 m a tile; the wall 10 m by 4 m, the bricks 1 m a tile.
    g.mesh('ground', *quad((-8, -8, 0), (16, 0, 0), (0, 16, 0), (0, 0, 1), np.array([16 / 1.5, 16 / 1.5])), cob)
    P, N_, UV, I = quad((-5, 5, 0), (10, 0, 0), (0, 0, 4), (0, -1, 0), np.array([10.0, 4.0]))
    g.mesh('wall', P, N_, UV, I, brk)
    g.mesh('puddle', *quad((-1.5, -1.0, 0.012), (3.0, 0, 0), (0, 2.2, 0), (0, 0, 1), np.array([1.0, 1.0])), puddle)
    g.mesh('box', *box((2.8, 2.5, 1.0), (1.0, 1.0, 2.0)), plain)
    # Plants: clumps of three crossed cards at the wall's foot and in the open.
    Ps, Ns, UVs, Is, Cs = [], [], [], [], []
    for cx, cy, s in [(-3.5, 4.6, 1.0), (-2.6, 4.7, 0.8), (0.5, 4.6, 1.1), (3.6, 4.65, 0.9), (-4.0, 0.5, 1.0), (4.5, -1.5, 0.9)]:
        for k in range(3):
            a = k * np.pi / 3 + RNG.uniform(0, 0.5)
            u = np.array([np.cos(a), np.sin(a), 0]) * 0.8 * s
            o = np.array([cx, cy, 0]) - u / 2
            P, N_, UV, I = quad(o, u, (0, 0, 1.0 * s), np.cross(u, (0, 0, 1)) / np.linalg.norm(u), (1, 1))
            C = np.ones((4, 4)); C[:, 3] = [0, 0, 1, 1]
            Is.append(I + 4 * len(Ps)); Ps.append(P); Ns.append(N_); UVs.append(UV); Cs.append(C)
    g.mesh('plants', np.vstack(Ps), np.vstack(Ns), np.vstack(UVs), np.vstack(Is), leaf, np.vstack(Cs))
    g.write(out)


if __name__ == '__main__':
    main(sys.argv[1])
