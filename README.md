# Stasis Vault

Refined Storage-style virtual storage for **Vintage Story 1.22**: no more walls of chests. Items go into memory cylinders, reachable from one searchable terminal, and temporal gears can slow down or freeze food spoilage.

**Download:** [mods.vintagestory.at/stasisvault](https://mods.vintagestory.at/stasisvault) · Mod ID: `curveostockage` · Required on both server and client.

## Features

- **Temporal Core** (one per network), **Cylinder Bays** (8 cylinders each) and **Conduits** that shape themselves.
- **Memory cylinders**: copper, bronze, iron, steel, refrigerated and stasis. A cylinder keeps its contents when removed.
- **Storage Terminal**: search (`@mod`, `#type`), sort, quick filters, pinned items, activity log, deposit slot, shift-click in/out.
- **Temporal Stabilizer**: burns temporal gears to slow spoilage (×0.5, ×0.1) or freeze food in stasis cylinders.
- **Wireless Tablet** linked to a **Temporal Emitter**.
- **Temporal Anchor**: keeps the network's chunks loaded.
- **Workshop**: crafting grid with auto-refill and a JEI-style recipe browser.
- **Import Bus**: pulls items from the container it faces (a machine's output) into the network, and accepts anything a vanilla hopper or chute pushes into it. Whitelist/blacklist filter.
- **Export Bus**: keeps a chosen amount of each filtered item in the container it faces (chest, firepit fuel, machine input…), taken from the network.
- **Storage Bus**: adds a chest, crate or shelf (FoodShelves included) to the network; its contents show up in terminals. Whitelisted storage is filled before the cylinders; "extract only" mode available.
- Buses respect land claims and locks: they only act on containers the player who placed them can use.
- **Clockwork Automaton** (autocrafting): punch a recipe onto a card from the workshop's recipe browser, place the card in the automaton. Each card can keep a stock in the network ("always 64 planks"), and anything an automaton can make can be ordered from the terminal (Ctrl+Click), missing sub-ingredients being crafted first when another card makes them. A mechanical axle above or below speeds it up; its gear, pinned drum and press are animated with the axle.

## Building

Requirements: .NET 10 SDK, Python 3, a Vintage Story 1.22 installation.

```bash
VINTAGE_STORY=/path/to/Vintagestory outils/construire.sh
```

The script regenerates the block/item definitions and translations, builds `CurveoStockage.dll` and packs `stasisvault_<version>.zip`, ready to drop into the `Mods` folder. `VINTAGE_STORY` is the game folder that contains `VintagestoryAPI.dll`, `Mods/` and `Lib/`.

## Project layout

| Path | Content |
| --- | --- |
| `src/` | C# code of the mod (network, cylinders, stabilizer, anchor, tablet, workshop, interfaces) |
| `assets/curveostockage/` | Blocks, items, shapes, textures, recipes and translations |
| `outils/` | Python generators: `textures.py` (pixel-art textures), `formes.py` (3D shapes), `definitions.py` (block/item JSON, `modinfo.json`, translations from `lang.json`) |

Assets are generated: edit the generators, then run them (`python3 outils/textures.py`, `formes.py`, `definitions.py`) rather than editing the JSON/PNG files by hand. Translations live in `outils/lang.json` (French and English).

## Server configuration

`ModConfig/stasisvault.json` is created on first start (an older `curveostockage.json` is converted automatically and renamed to `.bak`). The server sends its settings to players when they join. Restart the server after editing.

Every part of the mod can be disabled with `Enabled: false`: its recipe is removed and blocks already placed stay inert (nothing is destroyed).

| Section | Settings |
| --- | --- |
| `Network` | max blocks per network, max item temperature, auto-sort between cylinders, activity log length, rot alert threshold |
| `Cylinders` | per material (`copper`, `bronze`, `iron`, `steel`, `refrigerated`, `stasis`): `Enabled`, `MaxItems`, `MaxItemTypes` (existing cylinders included); spoil rates of refrigerated and stasis cylinders |
| `Fuel` | fuel item (default temporal gear) and charges per item |
| `Stabilizer` | `Enabled`, `ConsumesFuel`, `MaxCharge`, each mode (`Enabled`, `SpoilRate`, `HoursPerCharge`), storm consumption multiplier |
| `Anchor` | `Enabled`, `ConsumesFuel`, `MaxCharge`, `HoursPerCharge`, storm multiplier, chunk columns per anchor, anchors per player |
| `Wireless` | tablet and emitter `Enabled`, `RangeBlocks` (0 = unlimited) |
| `Workshop` | `Enabled`, grid auto-refill |
| `Buses` | each bus `Enabled`, transfer interval and items per transfer, respect claims and locks |
| `Autocrafter` | `Enabled`, craft interval, axle speed-up and bonus, sub-craft depth, max queued orders |

For testing, `"AutoTestEnabled": true` enables the `/stockage autotest` command, which builds a network near spawn and checks routing, spoilage, buses, autocrafting and the config. Leave it off in production.

## AI disclaimer

AI (Claude, by Anthropic) was used in the development of this mod, including its textures and user interface. The textures are produced by AI-written scripts that generate pixel art, not by an image generator. Design, gameplay choices and in-game testing were done by curveo.

## License

[GNU Lesser General Public License v3.0](COPYING.LESSER) (LGPL-3.0), which extends the [GNU GPL v3](COPYING).

---

### Français

Stasis Vault apporte un stockage virtuel façon Refined Storage à Vintage Story 1.22 : cylindres-mémoire, terminal avec recherche, tablette sans fil, bus d'import, d'export et de stockage (trémies, coffres, caisses, étagères), automate horloger (autocraft par cartes perforées, accéléré par un axe), atelier avec recettes façon JEI, et engrenages temporels pour ralentir ou figer le pourrissement. Compilation : `VINTAGE_STORY=/chemin/vers/Vintagestory outils/construire.sh`. Licence LGPL-3.0.
