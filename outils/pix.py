"""Petite boîte à outils pixel art sans dépendance : images RGBA 32x32, bruit, PNG."""
import struct, zlib, random, math

class Img:
    def __init__(self, w=32, h=32, fond=(0, 0, 0, 0)):
        self.w, self.h = w, h
        self.p = [[tuple(fond) for _ in range(w)] for _ in range(h)]

    def set(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            if len(c) == 3: c = (*c, 255)
            self.p[y][x] = tuple(max(0, min(255, int(v))) for v in c)

    def get(self, x, y): return self.p[y % self.h][x % self.w]

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1): self.set(x, y, c)

    def shade(self, x, y, f):
        r, g, b, a = self.get(x, y); self.set(x, y, (r * f, g * f, b * f, a))

    def tint(self, x, y, c, t):
        r, g, b, a = self.get(x, y)
        self.set(x, y, (r + (c[0] - r) * t, g + (c[1] - g) * t, b + (c[2] - b) * t, a))

    def save(self, chemin):
        brut = b''.join(b'\x00' + bytes(v for px in ligne for v in px) for ligne in self.p)
        def bloc(t, d): return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
        with open(chemin, 'wb') as f:
            f.write(b'\x89PNG\r\n\x1a\n' + bloc(b'IHDR', struct.pack('>IIBBBBB', self.w, self.h, 8, 6, 0, 0, 0))
                    + bloc(b'IDAT', zlib.compress(brut, 9)) + bloc(b'IEND', b''))

def bruit(w, h, graine, echelle=4):
    """Bruit de valeur lissé, périodique (texture sans raccord), valeurs 0..1."""
    rnd = random.Random(graine)
    gw, gh = max(1, w // echelle), max(1, h // echelle)
    grille = [[rnd.random() for _ in range(gw)] for _ in range(gh)]
    def v(x, y):
        fx, fy = x / echelle, y / echelle
        x0, y0 = int(fx) % gw, int(fy) % gh
        x1, y1 = (x0 + 1) % gw, (y0 + 1) % gh
        tx, ty = fx - int(fx), fy - int(fy)
        tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
        a = grille[y0][x0] + (grille[y0][x1] - grille[y0][x0]) * tx
        b = grille[y1][x0] + (grille[y1][x1] - grille[y1][x0]) * tx
        return a + (b - a) * ty
    return [[v(x, y) for x in range(w)] for y in range(h)]

def degrade(palette, t):
    """palette : liste de couleurs sombre -> clair ; t 0..1."""
    t = max(0.0, min(0.999, t)) * (len(palette) - 1)
    i = int(t); f = t - i
    a, b = palette[i], palette[i + 1]
    return tuple(a[k] + (b[k] - a[k]) * f for k in range(3))
