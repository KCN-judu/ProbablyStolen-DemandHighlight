#!/usr/bin/env bash
# Builds the query sample against the game's assemblies; --install copies it to the game's Mods folder.
# It compiles api/DemandHighlightQuery.cs into the mod, as another mod using the API does: no reference to
# Demand Highlight's assembly.
#   GAME=/d/SteamLibrary/steamapps/common/"Probably Stolen" CSC=/path/to/csc.exe tools/QuerySample/build.sh [--install]
set -euo pipefail
cd "$(dirname "$0")"
GAME="${GAME:-/d/SteamLibrary/steamapps/common/Probably Stolen}"
M="$GAME/Probably Stolen_Data/Managed"
CSC="${CSC:-/c/Program Files (x86)/Microsoft Visual Studio/18/BuildTools/MSBuild/Current/Bin/Roslyn/csc.exe}"
OUT=../../out/query_sample
mkdir -p "$OUT"
refs=()
for r in mscorlib netstandard System System.Core Assembly-CSharp UnityEngine UnityEngine.CoreModule; do
  [ -f "$M/$r.dll" ] && refs+=("-r:$M/$r.dll")
done
WORK="${TEMP:-/tmp}/qsample_build"
rm -rf "$WORK" && mkdir -p "$WORK"
cp QuerySample.cs ../../api/DemandHighlightQuery.cs "$WORK/"
( cd "$WORK" && "$CSC" -nologo -nostdlib -noconfig -target:library -optimize+ -warn:4 -nowarn:1701,1702 \
  "${refs[@]}" -out:QuerySample.dll QuerySample.cs DemandHighlightQuery.cs )
cp "$WORK/QuerySample.dll" manifest.xml "$OUT/"
echo "built out/query_sample"
if [ "${1:-}" = "--install" ]; then
  rm -rf "$GAME/Mods/query_sample"
  cp -r "$OUT" "$GAME/Mods/"
  echo "installed to $GAME/Mods/query_sample"
fi
