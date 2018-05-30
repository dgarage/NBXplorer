[![NuGet](https://img.shields.io/nuget/v/NBxplorer.Client.svg)](https://www.nuget.org/packages/NBxplorer.Client) [![Docker Automated buil](https://img.shields.io/docker/automated/jrottenberg/ffmpeg.svg)](https://hub.docker.com/r/nicolasdorier/nbxplorer/)

# NBXplorer

A minimalist UTXO tracker for HD Wallets, currently supporting btc and ltc.
The goal is to have a flexible, .NET based UTXO tracker for HD wallets.
The explorer supports only P2PKH derivation for now but will be able to support more complex generation in near future. (m-n, segwit, p2sh)

This explorer is not meant to be exposed on internet, but should be used as an internal tool for tracking the UTXOs of your own service.

It currently supports:

* Bitcoin
* Litecoin
* Dogecoin
* Dash
* Polis
* BCash (also known as Bitcoin Cash)
* BGold (also known as Bitcoin Gold)
* Monacoin
* Feathercoin
* Ufo

## Prerequisite

* Install [.NET Core 2.1](https://www.microsoft.com/net/download/dotnet-core/sdk-2.1.300-rc1)
* Bitcoin Core instance synched and running (at least 0.16.0).

## API Specification

Read our [API Specification](docs/API.md).

## How to build and run?

If you are using Bitcoin core default settings:

On Powershell:
```
.\build.ps1
```

On Linux:
```
./build.sh
```

Then to run:

On Powershell:
```
.\run.ps1 --help
```

On Linux:

```
./run.sh --help
```

Example, if you have ltc node and btc node on regtest (default configuration), and want to connect to them:

```
./run.sh --chains=btc,ltc --network=regtest
```

## With Docker

Use [our image](https://hub.docker.com/r/nicolasdorier/nbxplorer/).
You can check [the sample](docker-compose.regtest.yml) for configuring and composing it bitcoin core.

## How to develop on it?

If you are on Windows, I recommend Visual Studio 2017 update 3 (15.3).
If you are on other platform and want lightweight environment, use [Visual Studio Code](https://code.visualstudio.com/).
If you are hardcore, you can code on vim.

I like Visual Studio Code and Visual Studio 2017 as it allows me to debug in step by step.

## How to configure?

NBXplorer supports configuration through command line arguments, configuration file, or environment variables.

### Configuration file

If you are not using standard install for bitcoind, you will have to change the configuration file:
In Windows it is located on

```
C:\Users\<user>\AppData\Roaming\NBXplorer\<network>\settings.config
```

On linux or mac:
```
~/.nbxplorer/<network>/settings.config
```

The default configuration assume `mainnet` with only `btc` chain supported, and use default settings of bitcoind.

You can change the location of the configuration file with the `--conf=pathToConf` command line argument.

### Command line parameters

The same settings as above, for example: `dotnet run NBXplorer.dll -- --port=20300 --network=mainnet --nodeendpoint=127.0.0.1:32939`.

### Environment variables

The same settings as above, for example `export NBXPLORER_PORT=20300`. This is usefull for configuring docker.

## Important Note

This tool will only start scanning from the configured `startheight`. (By default, the height of the blockchain during your first run)
This mean that you might not see old payment from you HD key.

If you need to see old payments, you need to configure `startheight` to a specific height of your choice, then run with again with `-rescan`.

## How to query?

A better documentation is on the way, for now the only documentation is the client API in C# on [nuget](https://www.nuget.org/packages/NBxplorer.Client).
The `ExplorerClient` classes allows you to query unused address, and the UTXO of a HD PubKey.
You can take a look at [the tests](https://github.com/dgarage/NBXplorer/blob/master/NBXplorer.Tests/UnitTest1.cs) to see how it works.

There is a simple use case documented on [Blockchain Programming in C#](https://programmingblockchain.gitbooks.io/programmingblockchain/content/wallet/web-api.html).

## How to run the tests?

This is easy, from repo directory:
```
cd NBXplorer.Tests
dotnet test
```
The tests can take long the first time, as it download Bitcoin Core binaries. (Between 5 and 10 minutes)

## How to add support to my altcoin

First you need to add support for your altcoin to `NBitcoin.Altcoins`. (See [here](https://github.com/MetacoSA/NBitcoin/tree/master/NBitcoin.Altcoins)).

Once this is done and `NBXplorer` updated to use the last version of `NBitcoin.Altcoins`, follow [Litecoin example](NBXplorer.Client/NBXplorerNetworkProvider.Litecoin.cs).

If you want to test if everything is working, modify [ServerTester.Environment.cs](NBXplorer.Tests/ServerTester.Environment.cs) to match your altcoin.

Then run the tests.

## Licence

This project is under MIT License.

## Special thanks

Special thanks to Digital Garage for allowing me to open source the project, which is based on an internal work I have done on Elements.

Thanks to the DG Lab Blockchain Team who had to fight with lots of bugs. (in particular kallewoof :p)

Thanks to Metaco SA, whose constant challenging projects refine my taste on what a perfect Bitcoin API should be.
