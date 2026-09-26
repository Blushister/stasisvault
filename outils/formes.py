"""Génère les formes (format JSON de Vintage Story). Unités : 0..16 par bloc ; UV en unités de texture 16."""
import json
S = 'assets/curveostockage/shapes/'
FACES = ('north', 'east', 'south', 'west', 'up', 'down')

def uv_auto(f, t, face):
    (x0, y0, z0), (x1, y1, z1) = f, t
    if face in ('north', 'south'): return [x0, 16 - y1, x1, 16 - y0]
    if face in ('east', 'west'):   return [z0, 16 - y1, z1, 16 - y0]
    return [x0, z0, x1, z1]

def el(nom, f, t, tex, faces=None, full=(), glow=None, rot=None, origine=None, sans=()):
    """tex : texture par défaut ; faces : {face: texture} ; full : faces avec la texture entière."""
    d = {'name': nom, 'from': list(f), 'to': list(t), 'faces': {}}
    for fa in FACES:
        if fa in sans: continue
        tx = (faces or {}).get(fa, tex)
        face = {'texture': '#' + tx, 'uv': [0, 0, 16, 16] if fa in full else uv_auto(f, t, fa)}
        if glow and fa in glow: face['glow'] = glow[fa]
        d['faces'][fa] = face
    if rot:
        d['rotationOrigin'] = origine or [8, 8, 8]
        for axe, ang in rot.items(): d['rotation' + axe] = ang
    return d

def forme(nom, textures, elements, dossier='block'):
    j = {'editor': {'allAngles': False}, 'textureWidth': 16, 'textureHeight': 16,
         'textures': {k: 'curveostockage:' + v for k, v in textures.items()}, 'elements': elements}
    json.dump(j, open(f'{S}{dossier}/{nom}.json', 'w'), indent=1)

TX = {'laiton': 'block/laiton', 'lisse': 'block/laiton-lisse', 'fer': 'block/fer', 'cuivre': 'block/cuivre',
      'noyer': 'block/noyer', 'verre': 'block/verre-temporel'}

# Cœur temporel : caisson de fer, montants et plateaux de laiton, engrenage lumineux en façade, verre au sommet
forme('coeur', {**TX, 'facade': 'block/coeur-facade'}, [
    el('caisson', (1, 1, 1), (15, 15, 15), 'fer', {'north': 'facade'}, full=('north',), glow={'north': 60}),
    el('plateau-bas', (0, 0, 0), (16, 1, 16), 'laiton'),
    el('plateau-haut', (0, 15, 0), (16, 16, 16), 'laiton', sans=('up',)),
    el('verre-haut', (4, 15.01, 4), (12, 16.5, 12), 'verre', glow={f: 180 for f in FACES}),
    *[el(f'montant{i}', (x, 1, z), (x + 2, 15, z + 2), 'lisse') for i, (x, z) in enumerate(((0, 0), (14, 0), (0, 14), (14, 14)))],
])

# Baie à cylindres : façade à 8 alvéoles
for suffixe, facade in (('', 'block/baie-facade'), ('-pleine', 'block/baie-facade-pleine')):
    forme('baie' + suffixe, {**TX, 'facade': facade}, [
        el('corps', (0, 0, 1), (16, 16, 16), 'lisse', {'north': 'facade', 'up': 'fer', 'down': 'fer'}, full=('north',),
           glow={'north': 25} if suffixe else None),
        el('cadre-haut', (0, 15, 0), (16, 16, 1), 'laiton'),
        el('cadre-bas', (0, 0, 0), (16, 1, 1), 'laiton'),
    ])

# Terminal : lutrin en noyer, pupitre incliné en laiton avec lentille-écran
forme('terminal', {**TX, 'ecran': 'block/terminal-ecran'}, [
    el('socle', (3, 0, 3), (13, 2, 13), 'noyer'),
    el('pied', (6, 2, 6), (10, 9, 10), 'noyer'),
    el('pupitre', (1, 9, 2), (15, 11, 14), 'laiton', {'up': 'ecran'}, full=('up',), glow={'up': 140},
       rot={'X': -22.5}, origine=[8, 10, 8]),
])

# Terminal d'artisanat : table en noyer, grille 3x3 incrustée, petit écran au fond
forme('terminal-artisanat', {**TX, 'grille': 'block/artisanat-dessus', 'ecran': 'block/terminal-ecran'}, [
    el('plateau', (0, 10, 0), (16, 12, 16), 'noyer', {'up': 'grille'}, full=('up',)),
    *[el(f'pied{i}', (x, 0, z), (x + 2, 10, z + 2), 'noyer') for i, (x, z) in enumerate(((1, 1), (13, 1), (1, 13), (13, 13)))],
    el('traverse', (2, 3, 2), (14, 4, 14), 'noyer'),
    el('ecran', (2, 12, 13), (14, 16, 15), 'laiton', {'north': 'ecran'}, full=('north',), glow={'north': 140}),
])

# Stabilisateur temporel : bloc de cuivre, logement d'engrenage, bandes de laiton
forme('stabilisateur', {**TX, 'facade': 'block/stabilisateur-facade'}, [
    el('corps', (1, 0, 1), (15, 14, 15), 'cuivre', {'north': 'facade'}, full=('north',), glow={'north': 50}),
    el('bande-bas', (0.5, 1, 0.5), (15.5, 3, 15.5), 'laiton'),
    el('bande-haut', (0.5, 11, 0.5), (15.5, 13, 15.5), 'laiton'),
    el('couvercle', (3, 14, 3), (13, 15, 13), 'fer'),
    el('lentille', (6, 15, 6), (10, 16, 10), 'verre', glow={f: 200 for f in FACES}),
])

# Conduits : nœud de laiton (les bras sont des surcouches, plus bas)
forme('conduit-noeud', TX, [el('noeud', (5, 5, 5), (11, 11, 11), 'laiton'),
                            el('coeur-verre', (6.5, 6.5, 4.9), (9.5, 9.5, 11.1), 'verre', glow={f: 120 for f in FACES})])


# Cylindres-mémoire : prisme octogonal (deux pavés dont un tourné de 45°) + couvercles
for nom in ('cuivre', 'bronze', 'acier', 'refrigere', 'stase'):
    tx = {'flanc': f'item/cylindre-{nom}-flanc', 'couvercle': f'item/cylindre-{nom}-couvercle'}
    lum = {f: 90 for f in FACES} if nom == 'stase' else None
    faces = {'north': 'couvercle', 'south': 'couvercle', 'east': 'flanc', 'west': 'flanc', 'up': 'flanc', 'down': 'flanc'}
    forme(f'cylindre-{nom}', tx, [
        el('a', (5, 5, 2), (11, 11, 14), 'flanc', faces, full=FACES, glow=lum),
        el('b', (5, 5, 2.01), (11, 11, 13.99), 'flanc', faces, full=FACES, glow=lum, rot={'Z': 45}, origine=[8, 8, 8]),
    ], dossier='item')
print('formes ok')

# Bras de conduit par direction (le nœud est la forme de base, les bras s'ajoutent en surcouche)
BRAS = {
    'n': ((6, 6, 0), (10, 10, 5.5), (5, 5, 0.5), (11, 11, 2)),
    's': ((6, 6, 10.5), (10, 10, 16), (5, 5, 14), (11, 11, 15.5)),
    'w': ((0, 6, 6), (5.5, 10, 10), (0.5, 5, 5), (2, 11, 11)),
    'e': ((10.5, 6, 6), (16, 10, 10), (14, 5, 5), (15.5, 11, 11)),
    'd': ((6, 0, 6), (10, 5.5, 10), (5, 0.5, 5), (11, 2, 11)),
    'u': ((6, 10.5, 6), (10, 16, 10), (5, 14, 5), (11, 15.5, 11)),
}
for d, (f, t, bf, bt) in BRAS.items():
    forme(f'conduit-bras-{d}', TX, [el('tuyau', f, t, 'cuivre'), el('bague', bf, bt, 'laiton')])
print('bras ok')

# --- Sans-fil (v1.1) ---
G = {f: 200 for f in FACES}
# Émetteur : socle de fer, mât de laiton, anneaux, cristal temporel en losange
forme('emetteur', TX, [
    el('socle', (2, 0, 2), (14, 2, 14), 'fer'),
    el('socle-haut', (4, 2, 4), (12, 3, 12), 'laiton'),
    el('mat', (7, 3, 7), (9, 11, 9), 'lisse'),
    *[el(f'anneau{i}', f, t, 'laiton') for i, (f, t) in enumerate((
        ((4.5, 6, 7.5), (11.5, 6.8, 8.5)), ((7.5, 6, 4.5), (8.5, 6.8, 11.5)),
        ((5.5, 9, 7.5), (10.5, 9.8, 8.5)), ((7.5, 9, 5.5), (8.5, 9.8, 10.5))))],
    el('cristal', (6, 11, 6), (10, 15, 10), 'verre', glow=G, rot={'Y': 45}, origine=[8, 13, 8]),
    el('pointe', (7, 15, 7), (9, 16.5, 9), 'verre', glow=G, rot={'Y': 45}, origine=[8, 15.5, 8]),
])
# Ancre temporelle : pilier de fer, montants de laiton, sablier de sable temporel lumineux
forme('ancre', TX, [
    el('socle', (1, 0, 1), (15, 3, 15), 'fer'),
    el('chapeau', (1, 13, 1), (15, 16, 15), 'fer'),
    el('bande-bas', (0.5, 2.5, 0.5), (15.5, 3.5, 15.5), 'laiton'),
    el('bande-haut', (0.5, 12.5, 0.5), (15.5, 13.5, 15.5), 'laiton'),
    *[el(f'montant{i}', (x, 3, z), (x + 2, 13, z + 2), 'lisse') for i, (x, z) in enumerate(((1, 1), (13, 1), (1, 13), (13, 13)))],
    el('bulbe-bas', (4.5, 3, 4.5), (11.5, 7, 11.5), 'verre', glow=G),
    el('col', (7, 7, 7), (9, 9, 9), 'verre', glow=G),
    el('bulbe-haut', (4.5, 9, 4.5), (11.5, 13, 11.5), 'verre', glow={f: 120 for f in FACES}),
])
# Tablette : cadre de noyer, écran-lentille lumineux, charnière de laiton (objet tenu en main)
forme('tablette', {**TX, 'ecran': 'block/terminal-ecran'}, [
    el('cadre', (3, 0, 1), (13, 1.5, 15), 'noyer'),
    el('ecran', (4, 1.5, 2), (12, 1.8, 14), 'laiton', {'up': 'ecran'}, full=('up',), glow={'up': 150}),
    el('charniere', (6, 0.3, 0.4), (10, 1.2, 1), 'laiton'),
    el('lentille', (7.3, 1.5, 14.3), (8.7, 2.2, 14.9), 'verre', glow=G),
], dossier='item')
print('sans-fil ok')
