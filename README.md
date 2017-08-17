# ElementsExplorer

A minimalist block explorer for Elements

## How to run?

* Install [.NET Core](https://www.microsoft.com/net/core)
* Have an Elements instance running in regtest

```
git clone https://github.com/dgarage/ElementsExplorer
cd ElementsExplorer
git submodule init
git submodule update
dotnet restore
cd ElementsExplorer
dotnet run -regtest
```

Adapt the configuration file if elements is not using default settings