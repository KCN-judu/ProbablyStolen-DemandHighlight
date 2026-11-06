#!/usr/bin/env bash
# Builds the mod with the Roslyn compiler from Visual Studio Build Tools (the game uses newer C# features), against the game's own Mono assemblies.
#   GAME=/d/SteamLibrary/steamapps/common/"Probably Stolen" CSC=/path/to/csc.exe ./build.sh [--install]
set -euo pipefail
cd "$(dirname "$0")"
GAME="${GAME:-/d/SteamLibrary/steamapps/common/Probably Stolen}"
M="$GAME/Probably Stolen_Data/Managed"
CSC="${CSC:-/c/Program Files (x86)/Microsoft Visual Studio/18/BuildTools/MSBuild/Current/Bin/Roslyn/csc.exe}"
mkdir -p out/demand_highlight

refs=()
for r in mscorlib netstandard System System.Core Assembly-CSharp Assembly-CSharp-firstpass 0Harmony \
         UnityEngine UnityEngine.CoreModule UnityEngine.UI UnityEngine.UIModule UnityEngine.InputLegacyModule UnityEngine.TextRenderingModule Unity.TextMeshPro; do
  [ -f "$M/$r.dll" ] && refs+=("-r:$M/$r.dll")
done

# The .NET Framework csc cannot create its temp files under long paths, so compile in a short folder.
# It also reads "/" as an option prefix, so sources and output use backslashes.
WORK="${TEMP:-/tmp}/dh_build"
rm -rf "$WORK" && mkdir -p "$WORK/src"
cp src/*.cs "$WORK/src/"
srcs=()
for f in src/*.cs; do srcs+=("src\\$(basename "$f")"); done
( cd "$WORK" && "$CSC" -nologo -nostdlib -noconfig -target:library -optimize+ -langversion:latest -warn:4 -nowarn:1701,1702 \
  "${refs[@]}" '-out:DemandHighlight.dll' "${srcs[@]}" )
cp "$WORK/DemandHighlight.dll" out/demand_highlight/

cp manifest.xml out/demand_highlight/
cp preview.png out/demand_highlight/
# The pictures texture packs can replace (doc/manual/item-filter.md), for pack makers; the game never reads it.
[ -f textures.schema.tsv ] && cp textures.schema.tsv out/demand_highlight/
mkdir -p out/demand_highlight/Localization
cp Localization/*.csv out/demand_highlight/Localization/
echo "built out/demand_highlight"

if [ "${1:-}" = "--install" ]; then
  rm -rf "$GAME/Mods/demand_highlight"
  cp -r out/demand_highlight "$GAME/Mods/"
  echo "installed to $GAME/Mods/demand_highlight"
fi
