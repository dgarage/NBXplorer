#!/bin/bash
set -euo pipefail
rm -rf "bin/Release/"
dotnet pack --configuration Release --include-symbols -p:SymbolPackageFormat=snupkg
package=$(find ./bin/Release -name "*.nupkg" -type f | head -n 1) 
dotnet nuget push "${package[0]}" --source "https://api.nuget.org/v3/index.json" --api-key "$NUGET_API_KEY"
ver=$(basename "${package[0]}" | sed -E 's/NBXplorer\.Client\.([0-9]+(\.[0-9]+){1,3}).*/\1/')
git tag -a "Client/v$ver" -m "Client/$ver"
git push origin "Client/v$ver"
