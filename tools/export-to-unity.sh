#!/usr/bin/env bash
# Copies the engine-free C# (sim, client logic, tests) and the game data into a
# Unity project's Assets folder, laid out as in the tech spec.
#
#   tools/export-to-unity.sh /path/to/UnityProject
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
unity="${1:?usage: export-to-unity.sh /path/to/UnityProject}"
assets="$unity/Assets"
[ -d "$assets" ] || { echo "No Assets/ folder in $unity" >&2; exit 1; }

copy_cs() { # src dst
  mkdir -p "$2"
  (cd "$1" && find . -name '*.cs' -not -path './bin/*' -not -path './obj/*' -print0 | \
    xargs -0 -I{} sh -c 'mkdir -p "$0/$(dirname "{}")" && cp "{}" "$0/{}"' "$2")
}

copy_cs "$repo/src/RR.Sim" "$assets/Scripts/Sim"
cp "$repo/src/RR.Sim/RR.Sim.asmdef" "$assets/Scripts/Sim/"

copy_cs "$repo/src/RR.Client" "$assets/Scripts/Game/Client"
cp "$repo/src/RR.Client/RR.Client.asmdef" "$assets/Scripts/Game/Client/"

mkdir -p "$assets/Data"
cp "$repo"/data/*.json "$assets/Data/"

mkdir -p "$assets/Tests/EditMode"
cp "$repo"/tests/RR.Sim.Tests/*.cs "$assets/Tests/EditMode/"
cp "$repo/tools/RR.SimRunner/RotationBot.cs" "$assets/Tests/EditMode/"
cat > "$assets/Tests/EditMode/RR.Sim.Tests.asmdef" <<'JSON'
{
    "name": "RR.Sim.Tests",
    "references": ["RR.Sim", "RR.Client"],
    "optionalUnityReferences": ["TestAssemblies"],
    "includePlatforms": ["Editor"],
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "noEngineReferences": true
}
JSON

echo "Exported to $assets. See docs/UNITY_PORT.md for the remaining steps."
