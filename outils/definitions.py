import json
A = 'assets/curveostockage/'
# La façade des formes est au nord ; le jeu donne au bloc la direction où regarde le joueur :
# on tourne d'un demi-tour pour que la façade soit face à lui.
ROT = {'north': 180, 'east': 90, 'south': 0, 'west': 270}
SONS_METAL = {'place': 'game:block/anvil', 'break': 'game:block/anvil', 'hit': 'game:block/anvil', 'walk': 'game:walk/stone'}
SONS_BOIS = {'place': 'game:block/planks', 'break': 'game:block/planks', 'hit': 'game:block/planks', 'walk': 'game:walk/wood'}

def bloc(code, forme, materiau='Metal', sons=SONS_METAL, lumiere=None, autres=None, formeParType=None):
    b = {
        'code': code, 'class': 'Block',
        'variantgroups': [*(autres or []), {'code': 'side', 'loadFromProperties': 'abstract/horizontalorientation'}],
        'behaviors': [{'name': 'HorizontalOrientable', 'properties': {'dropBlockFace': 'north'}}],
        'creativeinventory': {'general': ['*-north'], 'stasisvault': ['*-north']},
        'shapeByType': formeParType or {f'*-{s}': {'base': forme, 'rotateY': r} for s, r in ROT.items()},
        'drawtype': 'json', 'blockmaterial': materiau, 'resistance': 3.5, 'lightAbsorption': 0,
        'sidesolid': {'all': False}, 'sideopaque': {'all': False}, 'sounds': sons,
    }
    if lumiere: b['lightHsv'] = lumiere
    json.dump(b, open(f'{A}blocktypes/{code}.json', 'w'), indent=1)

import itertools
def ajuster(code, classe, entite=None, drops_nord=True):
    j = json.load(open(f'{A}blocktypes/{code}.json'))
    j['class'] = classe
    if entite: j['entityClass'] = entite
    json.dump(j, open(f'{A}blocktypes/{code}.json', 'w'), indent=1)

bloc('coeur', 'block/coeur', lumiere=[23, 5, 9])
bloc('terminal', 'block/terminal', sons=SONS_BOIS, lumiere=[23, 4, 6])
bloc('stabilisateur', 'block/stabilisateur', lumiere=[23, 5, 7])
bloc('baie', None, autres=[{'code': 'etat', 'states': ['vide', 'pleine']}],
     formeParType={f'*-{e}-{s}': {'base': 'block/baie' + ('-pleine' if e == 'pleine' else ''), 'rotateY': r}
                   for e in ('vide', 'pleine') for s, r in ROT.items()})
j = json.load(open(f'{A}blocktypes/baie.json')); j['creativeinventory'] = {'general': ['*-vide-north'], 'stasisvault': ['*-vide-north']}
json.dump(j, open(f'{A}blocktypes/baie.json', 'w'), indent=1)
ajuster('coeur', 'curveostockage.BlockReseau', 'curveostockage.Coeur')
ajuster('baie', 'curveostockage.BlockReseau', 'curveostockage.Baie')
ajuster('terminal', 'curveostockage.BlockReseau', 'curveostockage.Terminal')
ajuster('stabilisateur', 'curveostockage.BlockReseau', 'curveostockage.Stabilisateur')
bloc('emetteur', 'block/emetteur', lumiere=[23, 6, 10])
bloc('ancre', 'block/ancre', lumiere=[23, 5, 9])
ajuster('emetteur', 'curveostockage.BlockReseau')
ajuster('ancre', 'curveostockage.BlockReseau', 'curveostockage.Ancre')
# Atelier : ajoute la grille d'artisanat aux terminaux et tablettes du réseau
bloc('atelier', 'block/terminal-artisanat', sons=SONS_BOIS, lumiere=[23, 4, 6])
ajuster('atelier', 'curveostockage.BlockReseau')
import os
for vieux in ('terminal-artisanat',):
    if os.path.exists(f'{A}blocktypes/{vieux}.json'): os.remove(f'{A}blocktypes/{vieux}.json')

# Conduit : 64 variantes de raccordement, nœud + bras en surcouche
lettres = 'neswud'
etats = ['aucune'] + [''.join(c) for n in range(1, 7) for c in itertools.combinations(lettres, n)]
noeud = 5 / 16

def boites_conduit(etat):
    """Boîtes (en blocs) du nœud central et des bras présents, section 6/16 comme les bagues."""
    m, M = 5 / 16, 11 / 16
    boites = [(m, m, m, M, M, M)]
    bras = {'n': (m, m, 0, M, M, m), 's': (m, m, M, M, M, 1), 'w': (0, m, m, m, M, M),
            'e': (M, m, m, 1, M, M), 'd': (m, 0, m, M, m, M), 'u': (m, M, m, M, 1, M)}
    if etat != 'aucune': boites += [bras[d] for d in etat]
    return [dict(zip(('x1', 'y1', 'z1', 'x2', 'y2', 'z2'), (round(v, 4) for v in b))) for b in boites]
json.dump({
    'code': 'conduit', 'class': 'curveostockage.BlockConduit',
    'variantgroups': [{'code': 'connexions', 'states': etats}],
    'creativeinventory': {'general': ['*-aucune'], 'stasisvault': ['*-aucune']},
    'shapeByType': {f'*-{e}': {'base': 'block/conduit-noeud',
                               **({'overlays': [{'base': f'block/conduit-bras-{d}'} for d in e]} if e != 'aucune' else {})}
                    for e in etats},
    'drawtype': 'json', 'blockmaterial': 'Metal', 'resistance': 1.5, 'lightAbsorption': 0,
    'sidesolid': {'all': False}, 'sideopaque': {'all': False}, 'sounds': SONS_METAL,
    # Une boîte pour le nœud et une par bras : les liaisons se visent et bloquent comme le modèle
    'collisionSelectionBoxesByType': {f'*-{e}': boites_conduit(e) for e in etats},
}, open(f'{A}blocktypes/conduit.json', 'w'), indent=1)

json.dump({
    'code': 'cylindre', 'class': 'curveostockage.ItemCylindre', 'maxstacksize': 1,
    'attributesByType': {'*-cuivre': {'capacite': {'objets': 2000, 'types': 25}}, '*-bronze': {'capacite': {'objets': 8000, 'types': 50}},
        '*-fer': {'capacite': {'objets': 16000, 'types': 75}},
        '*-acier': {'capacite': {'objets': 32000, 'types': 100}}, '*-refrigere': {'capacite': {'objets': 4000, 'types': 30}},
        '*-stase': {'capacite': {'objets': 2000, 'types': 25}}},
    'variantgroups': [{'code': 'materiau', 'states': ['cuivre', 'bronze', 'fer', 'acier', 'refrigere', 'stase']}],
    'shapeByType': {f'*-{m}': {'base': f'item/cylindre-{m}'} for m in ('cuivre', 'bronze', 'fer', 'acier', 'refrigere', 'stase')},
    'creativeinventory': {'general': ['*'], 'stasisvault': ['*']},
    'guiTransform': {'rotation': {'x': -22, 'y': -45, 'z': 0}, 'origin': {'x': 0.5, 'y': 0.5, 'z': 0.5}, 'scale': 1.9},
    'groundTransform': {'translation': {'x': 0, 'y': -0.25, 'z': 0}, 'origin': {'x': 0.5, 'y': 0.5, 'z': 0.5}, 'scale': 2.5},
    'tpHandTransform': {'translation': {'x': -0.9, 'y': -0.2, 'z': -0.6}, 'rotation': {'x': 0, 'y': 0, 'z': -90}, 'scale': 0.6},
    'fpHandTransform': {'translation': {'x': 0, 'y': 0.1, 'z': 0}, 'rotation': {'x': 20, 'y': 40, 'z': 0}, 'scale': 1.8},
}, open(f'{A}itemtypes/cylindre.json', 'w'), indent=1)

json.dump({
    'code': 'tablette', 'class': 'curveostockage.ItemTablette', 'maxstacksize': 1,
    'shape': {'base': 'item/tablette'},
    'creativeinventory': {'general': ['*'], 'stasisvault': ['*']},
    'guiTransform': {'rotation': {'x': 75, 'y': 0, 'z': 0}, 'origin': {'x': 0.5, 'y': 0.1, 'z': 0.5}, 'scale': 1.7},
    'groundTransform': {'origin': {'x': 0.5, 'y': 0, 'z': 0.5}, 'scale': 2.2},
    # tenue à deux mains, réglée en jeu par curveo (sert aussi à la 1ʳᵉ personne)
    'heldTpIdleAnimation': 'holdbothhands', 'heldRightReadyAnimation': 'holdbothhands',
    'tpHandTransform': {'translation': {'x': -2.66, 'y': -0.24, 'z': -1.55}, 'rotation': {'x': 53.2, 'y': -81.7, 'z': -49.1},
                        'origin': {'x': 1.4, 'y': 0, 'z': 0.5}, 'scale': 0.6},
}, open(f'{A}itemtypes/tablette.json', 'w'), indent=1)
# Bus (v1.9) : 6 orientations (la tête vise le bloc voisin), boîtes de sélection qui suivent le modèle
import sys, contextlib, io
sys.path.insert(0, 'outils')
with contextlib.redirect_stdout(io.StringIO()):
    from formes import boites_bus, TOURNER
for code, entite in (('busimport', 'BusImport'), ('busexport', 'BusExport'), ('busstockage', 'BusStockage')):
    json.dump({
        'code': code, 'class': 'curveostockage.BlockBus', 'entityClass': f'curveostockage.{entite}',
        'variantgroups': [{'code': 'face', 'states': list(TOURNER)}],
        'creativeinventory': {'general': ['*-north'], 'stasisvault': ['*-north']},
        'shapeByType': {f'*-{d}': {'base': f'block/{code}-{d}'} for d in TOURNER},
        'drawtype': 'json', 'blockmaterial': 'Metal', 'resistance': 3.5, 'lightAbsorption': 0, 'lightHsv': [23, 4, 5],
        'sidesolid': {'all': False}, 'sideopaque': {'all': False}, 'sounds': SONS_METAL,
        'collisionSelectionBoxesByType': {f'*-{d}': boites_bus(d) for d in TOURNER},
        'guiTransform': {'rotation': {'x': -22.6, 'y': -135, 'z': 0}, 'origin': {'x': 0.5, 'y': 0.5, 'z': 0.5}, 'scale': 1.3},
    }, open(f'{A}blocktypes/{code}.json', 'w'), indent=1)

# Automate horloger (v1.9) : consommateur mécanique (axe au-dessus ou en dessous), façade vers le joueur.
# Toutes les textures sont déclarées ici : les pièces mobiles, dessinées à part, les retrouvent sur le bloc.
TEX_AUTOMATE = {'laiton': 'block/laiton', 'lisse': 'block/laiton-lisse', 'fer': 'block/fer', 'cuivre': 'block/cuivre',
                'noyer': 'block/noyer', 'verre': 'block/verre-temporel', 'flancpic': 'item/cylindre-bronze-flanc', 'carte': 'item/carte-perforee'}
json.dump({
    'code': 'automate', 'class': 'curveostockage.BlockAutomate', 'entityClass': 'curveostockage.Automate',
    'entityBehaviors': [{'name': 'MPConsumer', 'properties': {'mechPartShape': None, 'resistance': 0.05}}],
    'variantgroups': [{'code': 'orientation', 'states': ['north', 'east', 'south', 'west']}],
    'creativeinventory': {'general': ['*-north'], 'mechanics': ['*-north'], 'stasisvault': ['*-north']},
    'shapeByType': {f'*-{o}': {'base': 'block/automate', 'rotateY': r} for o, r in ROT.items()},
    'textures': {k: {'base': v} for k, v in TEX_AUTOMATE.items()},
    'drawtype': 'json', 'blockmaterial': 'Wood', 'resistance': 3, 'lightAbsorption': 0,
    'sidesolid': {'all': False, 'down': True}, 'sideopaque': {'all': False}, 'sounds': SONS_BOIS,
}, open(f'{A}blocktypes/automate.json', 'w'), indent=1)
# Bus : textures déclarées aussi (les bras de conduit dessinés au rendu les utilisent)
for code in ('busimport', 'busexport', 'busstockage'):
    j = json.load(open(f'{A}blocktypes/{code}.json'))
    j['textures'] = {k: {'base': v} for k, v in {**TEX_AUTOMATE, 'facade': f'block/{code}-facade'}.items() if k not in ('flancpic', 'carte')}
    json.dump(j, open(f'{A}blocktypes/{code}.json', 'w'), indent=1)
json.dump({
    'code': 'carte', 'class': 'curveostockage.ItemCarte',
    'variantgroups': [{'code': 'etat', 'states': ['vierge', 'perforee']}],
    'maxstacksizeByType': {'*-vierge': 64, '*': 1},
    'shapeByType': {f'*-{e}': {'base': f'item/carte-{e}'} for e in ('vierge', 'perforee')},
    'creativeinventory': {'general': ['*-vierge'], 'stasisvault': ['*-vierge']},
    'guiTransform': {'rotation': {'x': 75, 'y': 0, 'z': 0}, 'origin': {'x': 0.5, 'y': 0.1, 'z': 0.5}, 'scale': 2.2},
    'groundTransform': {'origin': {'x': 0.5, 'y': 0, 'z': 0.5}, 'scale': 2.4},
    'tpHandTransform': {'translation': {'x': -0.8, 'y': -0.1, 'z': -0.6}, 'rotation': {'x': 0, 'y': 0, 'z': -30}, 'scale': 0.7},
}, open(f'{A}itemtypes/carte.json', 'w'), indent=1)

noms = json.load(open('outils/lang.json', encoding='utf-8'))
for l, d in noms.items(): json.dump(d, open(f'{A}lang/{l}.json', 'w'), indent=1, ensure_ascii=False)
json.dump({'type': 'code', 'modid': 'curveostockage', 'name': 'Stasis Vault',
           'authors': ['curveo'], 'version': '1.9.0',
           'description': "Stasis Vault : stockage virtuel façon Refined Storage pour Vintage Story. Cœur temporel, baies à cylindres-mémoire (cuivre, bronze, fer, acier, réfrigéré, stase), terminal avec recherche, filtres, épingles et journal, tablette sans fil via un émetteur, bus d'import, d'export et de stockage (trémies, coffres, caisses, étagères), automate horloger (autocraft par cartes perforées, accéléré par un axe mécanique), atelier avec grille d'artisanat et recettes façon JEI, stabilisateur et ancre temporelle alimentés en engrenages temporels pour ralentir ou figer le pourrissement. — Refined Storage-like virtual storage: temporal core, memory cylinder bays, searchable terminal, wireless tablet, import/export/storage buses (hoppers, chests, crates, shelves), clockwork autocrafter (punch cards, sped up by mechanical power), crafting workshop with a JEI-style recipe browser, and temporal gear powered stabilizer and chunk anchor.",
           'side': 'Universal', 'requiredOnClient': True, 'dependencies': {'game': '1.22.0'}},
          open('modinfo.json', 'w'), indent=1, ensure_ascii=False)
print('types ok')
