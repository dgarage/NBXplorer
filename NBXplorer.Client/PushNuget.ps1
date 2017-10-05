rm "bin\release\" -Recurse -Force
dotnet pack --configuration Release
dotnet nuget push "bin\Release\" --source "https://api.nuget.org/v3/index.json"
$ver = ((ls .\bin\release\*.nupkg)[0].Name -replace 'NBXplorer\.Client\.(\d+(\.\d+){1,3}).*', '$1')
git tag -a "Client/v$ver" -m "Client/$ver"
git push --tags
