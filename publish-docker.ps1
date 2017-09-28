$version = [regex]::Match((Get-Content NBXplorer\NBXplorer.csproj), '<Version>([^<]+)<').Groups[1].Value
docker build -t nicolasdorier/nbxplorer:$version .
docker push nicolasdorier/nbxplorer:$version