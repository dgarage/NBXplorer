$version = [regex]::Match((Get-Content .\NBXplorer.csproj), '<Version>([^<]+)<').Groups[1].Value
dotnet restore
dotnet publish -c Release
docker build -t nicolasdorier/nbxplorer:$version .
docker push nicolasdorier/nbxplorer:$version