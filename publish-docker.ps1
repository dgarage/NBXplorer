$ver = [regex]::Match((Get-Content NBXplorer\NBXplorer.csproj), '<Version>([^<]+)<').Groups[1].Value
git tag -a "v$ver" -m "$ver"
git checkout latest
git merge master
git checkout master
git tag -d "stable"
git tag -a "stable" -m "stable"
git push origin "v$ver"
git push --force origin stable