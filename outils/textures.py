import sys, math, random
sys.path.insert(0, 'outils')
from pix import Img, bruit, degrade
B = 'assets/curveostockage/textures/block/'
I = 'assets/curveostockage/textures/item/'

LAITON = [(58, 40, 18), (112, 78, 32), (160, 118, 50), (196, 156, 78), (226, 196, 120)]
CUIVRE = [(62, 30, 18), (118, 58, 32), (164, 88, 50), (196, 120, 72), (222, 158, 108)]
BRONZE = [(52, 36, 22), (100, 70, 40), (140, 100, 58), (172, 132, 80), (204, 168, 110)]
ACIER  = [(34, 36, 40), (70, 74, 80), (110, 116, 124), (150, 156, 164), (196, 202, 210)]
FER    = [(20, 19, 20), (40, 38, 38), (62, 58, 56), (86, 80, 76), (112, 104, 98)]
NOYER  = [(34, 22, 14), (58, 38, 24), (82, 54, 34), (104, 72, 46), (128, 92, 60)]
TEMPO  = [(10, 40, 44), (20, 92, 96), (40, 160, 158), (110, 220, 206), (210, 255, 245)]
GIVRE  = [(60, 84, 110), (110, 140, 170), (160, 190, 214), (206, 228, 240), (240, 250, 255)]
PATINE = (70, 150, 120)

def metal(pal, graine, rivets=True, bord=True, patine=0.0):
    im = Img(); n = bruit(32, 32, graine, 4); n2 = bruit(32, 32, graine + 7, 2)
    for y in range(32):
        for x in range(32):
            t = 0.45 + (n[y][x] - 0.5) * 0.35 + (n2[y][x] - 0.5) * 0.12 + (y % 4 == 0) * -0.03
            im.set(x, y, degrade(pal, t))
            if patine and n2[y][x] * n[y][x] > 1 - patine: im.tint(x, y, PATINE, 0.55)
    if bord:
        for i in range(32):
            for (x, y, f) in ((i, 0, 1.35), (0, i, 1.3), (i, 31, 0.55), (31, i, 0.6), (i, 1, 1.12), (1, i, 1.1), (i, 30, 0.75), (30, i, 0.78)):
                im.shade(x, y, f)
    if rivets:
        for (cx, cy) in ((4, 4), (27, 4), (4, 27), (27, 27)):
            for (dx, dy, f) in ((0, 0, 1.25), (-1, 0, 1.15), (0, -1, 1.2), (1, 0, 0.8), (0, 1, 0.7), (1, 1, 0.55)):
                im.shade(cx + dx, cy + dy, f)
    return im

def bois(pal, graine):
    im = Img(); n = bruit(32, 32, graine, 2)
    for y in range(32):
        planche = y // 8
        for x in range(32):
            fil = math.sin((x + planche * 7) * 0.9 + n[y][x] * 3) * 0.08
            t = 0.45 + fil + (n[y][x] - 0.5) * 0.3 + (planche % 2) * 0.05
            im.set(x, y, degrade(pal, t))
        if y % 8 == 0:
            for x in range(32): im.shade(x, y, 0.55)
        if y % 8 == 1:
            for x in range(32): im.shade(x, y, 1.12)
    return im

def lueur(im, cx, cy, r, pal, force=1.0):
    for y in range(32):
        for x in range(32):
            d = math.hypot(x - cx, y - cy)
            if d < r:
                c = degrade(pal, (1 - d / r) * force)
                im.tint(x, y, c, min(1, (1 - d / r) * 1.4))

# --- Matériaux de base
metal(LAITON, 1).save(B + 'laiton.png')
metal(LAITON, 2, rivets=False).save(B + 'laiton-lisse.png')
metal(CUIVRE, 3, rivets=False, patine=0.28).save(B + 'cuivre.png')
metal(FER, 4).save(B + 'fer.png')
bois(NOYER, 5).save(B + 'noyer.png')

# --- Verre temporel (lueur, verre facetté)
v = Img(); n = bruit(32, 32, 9, 4)
for y in range(32):
    for x in range(32):
        facette = ((x + y) % 11 < 1) * 0.25 + ((x - y) % 13 < 1) * 0.18
        v.set(x, y, degrade(TEMPO, 0.35 + n[y][x] * 0.35 + facette))
lueur(v, 16, 16, 14, TEMPO, 1.0)
v.save(B + 'verre-temporel.png')

# --- Façade du cœur : engrenage temporel serti dans le laiton
c = metal(FER, 11)
for y in range(32):
    for x in range(32):
        dx, dy = x - 15.5, y - 15.5; d = math.hypot(dx, dy); a = math.atan2(dy, dx)
        dent = 12.5 + (1.6 if math.cos(a * 10) > 0.2 else 0)
        if d < dent:
            t = 0.55 + math.cos(a * 3 + d * 0.4) * 0.12 - d / 60
            c.set(x, y, degrade(LAITON, t))
            if dent - d < 1.2: c.shade(x, y, 0.62)
        if 5.5 < d < 7 : c.shade(x, y, 0.6)
        if d < 5.5: c.set(x, y, degrade(TEMPO, 0.95 - d / 9))
        for k in range(6):
            aa = k * math.pi / 3
            if 7 < d < 10.5 and abs(math.sin(a - aa)) < 0.12: c.shade(x, y, 0.7)
lueur(c, 15.5, 15.5, 7, TEMPO, 1.0)
c.save(B + 'coeur-facade.png')

# --- Façade de la baie : 2 colonnes x 4 rangées d'alvéoles
b = metal(LAITON, 12, rivets=False)
for col in range(2):
    for row in range(4):
        x0, y0 = 4 + col * 13, 3 + row * 7
        for y in range(y0, y0 + 5):
            for x in range(x0, x0 + 11):
                b.set(x, y, degrade(FER, 0.2 + (y - y0) * 0.05))
        for x in range(x0 - 1, x0 + 12): b.shade(x, y0 - 1, 0.6); b.shade(x, y0 + 5, 1.3)
        for y in range(y0 - 1, y0 + 6): b.shade(x0 - 1, y, 0.65); b.shade(x0 + 11, y, 1.25)
b.save(B + 'baie-facade.png')

# --- Baie avec disques (pour l'aperçu : bouts de cylindres visibles)
bd = Img(); bd.p = [row[:] for row in b.p]
teintes = [CUIVRE, CUIVRE, BRONZE, ACIER, BRONZE, GIVRE, TEMPO, ACIER]
for i, pal in enumerate(teintes):
    col, row = i % 2, i // 2
    x0, y0 = 4 + col * 13, 3 + row * 7
    for y in range(y0, y0 + 5):
        for x in range(x0 + 1, x0 + 10):
            bd.set(x, y, degrade(pal, 0.35 + (2 - abs(y - (y0 + 2))) * 0.15))
    bd.set(x0 + 8, y0 + 2, degrade(TEMPO, 0.95))
bd.save(B + 'baie-facade-pleine.png')

# --- Terminal : lentille lumineuse avec lignes de glyphes
t = metal(LAITON, 13)
for y in range(5, 27):
    for x in range(5, 27):
        t.set(x, y, degrade(TEMPO, 0.12 + (y - 5) * 0.004))
rnd = random.Random(4)
for ligne in range(7, 25, 3):
    x = 7
    while x < 25:
        l = rnd.randint(1, 4)
        for k in range(l):
            if x + k < 25: t.set(x + k, ligne, degrade(TEMPO, 0.8 + rnd.random() * 0.2))
        x += l + rnd.randint(1, 2)
for i in range(4, 28):
    t.shade(i, 4, 0.55); t.shade(4, i, 0.6); t.shade(i, 27, 1.3); t.shade(27, i, 1.25)
t.save(B + 'terminal-ecran.png')

# --- Dessus du terminal d'artisanat : grille 3x3 incrustée
g = bois(NOYER, 14)
for i in range(3):
    for j in range(3):
        x0, y0 = 5 + i * 8, 5 + j * 8
        for y in range(y0, y0 + 6):
            for x in range(x0, x0 + 6): g.set(x, y, degrade(FER, 0.25))
        for k in range(-1, 7):
            g.set(x0 + k, y0 - 1, degrade(LAITON, 0.7)); g.set(x0 + k, y0 + 6, degrade(LAITON, 0.45))
            g.set(x0 - 1, y0 + k, degrade(LAITON, 0.65)); g.set(x0 + 6, y0 + k, degrade(LAITON, 0.45))
g.save(B + 'artisanat-dessus.png')

# --- Stabilisateur : logement d'engrenage + cadran
s = metal(CUIVRE, 15, patine=0.2)
for y in range(32):
    for x in range(32):
        d = math.hypot(x - 11, y - 16)
        if d < 8: s.set(x, y, degrade(FER, 0.18 + d / 40))
        if 8 <= d < 9.3: s.set(x, y, degrade(LAITON, 0.75 if y < 16 else 0.45))
lueur(s, 11, 16, 5, TEMPO, 0.9)
for y in range(7, 26):
    for x in range(22, 27): s.set(x, y, degrade(FER, 0.15))
for y in range(14, 25):
    for x in range(23, 26): s.set(x, y, degrade(TEMPO, 0.55 + (y - 14) * 0.03))
s.save(B + 'stabilisateur-facade.png')

# --- Cylindres-mémoire (disques) : flanc à picots façon boîte à musique + couvercle
def cylindre(nom, pal, graine, lumineux=False):
    f = Img(); n = bruit(32, 32, graine, 4); rnd = random.Random(graine)
    for y in range(32):
        for x in range(32):
            rond = math.cos((y - 15.5) / 16 * math.pi / 2)
            f.set(x, y, degrade(pal, 0.25 + rond * 0.55 + (n[y][x] - 0.5) * 0.12))
    for _ in range(38):
        x, y = rnd.randrange(2, 30), rnd.randrange(4, 28)
        f.set(x, y, degrade(TEMPO if lumineux else LAITON, 0.9)); f.shade(x + 1, y + 1, 0.6)
    for x in range(32):
        for y in (0, 1, 30, 31): f.set(x, y, degrade(LAITON, 0.55 if y < 2 else 0.35))
    f.save(I + f'cylindre-{nom}-flanc.png')
    c = Img()
    for y in range(32):
        for x in range(32):
            d = math.hypot(x - 15.5, y - 15.5)
            c.set(x, y, degrade(LAITON, 0.7 - d / 40) if d > 10 else degrade(pal, 0.6 - d / 30))
            if d < 3: c.set(x, y, degrade(TEMPO, 0.95 - d / 6))
    c.save(I + f'cylindre-{nom}-couvercle.png')

for nom, pal, gr, lum in (('cuivre', CUIVRE, 21, False), ('bronze', BRONZE, 22, False), ('acier', ACIER, 23, False),
                          ('refrigere', GIVRE, 24, False), ('stase', TEMPO, 25, True)):
    cylindre(nom, pal, gr, lum)
print('textures ok')

# --- Textures d'interface : continues et calmes (laiton brossé), pour être répétées derrière du texte
G = 'assets/curveostockage/textures/gui/'
def brosse(pal, graine, base=0.55, amplitude=0.1):
    im = Img(); n = bruit(32, 32, graine, 8); fil = bruit(32, 32, graine + 3, 2)
    for y in range(32):
        for x in range(32):
            # Stries horizontales douces (brossage) + légères variations
            strie = (fil[y][(x // 4) % 32] - 0.5) * 0.08
            im.set(x, y, degrade(pal, base + (n[y][x] - 0.5) * amplitude + strie))
    return im
brosse(LAITON, 41, 0.58).save(G + 'laiton.png')
print('textures gui ok')
