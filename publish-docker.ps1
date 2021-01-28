$ver = [regex]::Match((Get-Content NBXplorer\NBXplorer.csproj), '<Version>([^<]+)<').Groups[1].Value
git tag -a "v$ver" -m "$ver"
git push origin "v$ver"