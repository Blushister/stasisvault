#!/bin/bash
# Régénère les définitions et traductions, compile et empaquette stasisvault_<version>.zip.
# Usage : VINTAGE_STORY=/chemin/vers/Vintagestory outils/construire.sh   (depuis n'importe où)
set -e
cd "$(dirname "$0")/.."
: "${VINTAGE_STORY:?Indique le dossier du jeu : VINTAGE_STORY=/chemin/vers/Vintagestory}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
python3 outils/definitions.py >/dev/null
(cd src && dotnet build -c Release -p:VintageStoryPath="$VINTAGE_STORY" 2>&1 | grep -E "warning CS|error CS|Build succeeded" | sort -u)
python3 - <<'PY'
import zipfile, os, glob, json
version = json.load(open('modinfo.json'))['version']
for f in glob.glob('stasisvault_*.zip'): os.remove(f)
nom = f'stasisvault_{version}.zip'
with zipfile.ZipFile(nom, 'w', zipfile.ZIP_DEFLATED) as z:
    z.write('modinfo.json'); z.write('modicon.png')
    z.write('src/bin/Release/net10.0/CurveoStockage.dll', 'CurveoStockage.dll')
    for r, _, fs in os.walk('assets'):
        for f in fs: z.write(os.path.join(r, f))
print(nom, os.path.getsize(nom) // 1024, 'Ko')
PY
