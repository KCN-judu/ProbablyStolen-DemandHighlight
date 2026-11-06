#!/usr/bin/env bash
# Compiles and runs every headless test: each tests/<Name>Test.cs with the src/*.Pure.cs files, no game assemblies.
#   CSC=/path/to/csc.exe tests/run.sh
set -euo pipefail
cd "$(dirname "$0")/.."
CSC="${CSC:-/c/Program Files (x86)/Microsoft Visual Studio/18/BuildTools/MSBuild/Current/Bin/Roslyn/csc.exe}"

# As in build.sh: compile in a short folder, with backslashes in paths.
WORK="${TEMP:-/tmp}/dh_tests"
rm -rf "$WORK" && mkdir -p "$WORK/src" "$WORK/tests"
cp src/*.Pure.cs "$WORK/src/"
cp tests/*Test.cs "$WORK/tests/"
# A test may name more files on a "// extra: path ..." line (QueryApiTest compiles the API client file).
mkdir -p "$WORK/api" && cp api/*.cs "$WORK/api/"
pure=()
for f in src/*.Pure.cs; do pure+=("src\\$(basename "$f")"); done

failed=0
for t in tests/*Test.cs; do
  name=$(basename "$t" .cs)
  [ "$name" = ScanTest ] && continue
  echo "== $name"
  extra=()
  for e in $(sed -n 's#^// extra: ##p' "$t" | tr -d '\r'); do extra+=("$(echo "$e" | tr '/' '\\')"); done
  ( cd "$WORK" && "$CSC" -nologo -optimize+ -langversion:latest "-out:$name.exe" "tests\\$name.cs" ${extra[@]+"${extra[@]}"} "${pure[@]}" >/dev/null ) || { echo "FAIL  $name does not compile"; failed=1; continue; }
  "$WORK/$name.exe" || failed=1
done
[ "$failed" = 0 ] && echo "every test passed" || echo "some tests failed"
exit "$failed"
