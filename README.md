[![NuGet](https://img.shields.io/nuget/v/NBxplorer.Client.svg)](https://www.nuget.org/packages/NBxplorer.Client) [![Docker Automated buil](https://img.shields.io/docker/automated/jrottenberg/ffmpeg.svg)](https://hub.docker.com/r/nicolasdorier/nbxplorer/)

# NBXplorer

A minimalist UTXO tracker for HD Wallets
The goal is to have a flexible, .NET based UTXO tracker for HD wallets.
The explorer supports only P2PKH derivation for now but will be able to support more complex generation in near future. (m-n, segwit, p2sh)

This explorer is not meant to be exposed on internet, but should be used as an internal tool for tracking the UTXOs of your own service.

## Prerequisite

* Install [.NET Core 2.0](https://www.microsoft.com/net/core)
* Bitcoin Core instance synched and running, at least 0.13.1. (0.14.1 if you want the segwit goodness coming)

## How to run?

If you are using Bitcoin core default settings:

```
git clone https://github.com/dgarage/NBXplorer
cd NBXplorer/NBXplorer
dotnet run
```
The first run might take some time to start, where you see nothing appearing. No worries, it is just downloading dependencies and compiling.
Please use `--network testnet` or `--network regtest` for targeting other network.

## With Docker

Use [our image](https://hub.docker.com/r/nicolasdorier/nbxplorer/).
You can check [the sample](docker-compose.regtest.yml) for configuring and composing it bitcoin core.

## How to develop on it?

If you are on Windows, I recommand Visual Studio 2017 update 3 (15.3).
If you are on other plateform and want lightweight environment, use [Visual Studio Code](https://code.visualstudio.com/).
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

By default, the configuration file is using cookie authentication in default bitcoin core path. So if you run bitcoin core with default settings, things will just work.

The default config file is shows you all options, if you need to change, remove the sharp sign (#) and customize your settings:

```
####Common Commands####
####If Bitcoin Core is running with default settings, you should not need to modify this file####
####All those options can be passed by through command like arguments (ie `-port=19382`)####
## This is the RPC Connection to your node
#rpc.url=http://localhost:18332/
#rpc.user=bitcoinuser
#rpc.password=bitcoinpassword
#rpc.cookiefile=yourbitcoinfolder/.cookie

## This is the connection to your node through P2P
#node.endpoint=localhost:18444

## startheight defines from which block you will start scanning, if -1 is set, it will use current blockchain height
#startheight=-1
## rescan forces a rescan from startheight
#rescan=0


####Server Commands####
#port=24446
#bind=127.0.0.1
#testnet=0
#regtest=0
```

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

## How to run the tests?

This is easy, from repo directory:
```
cd NBXplorer.Tests
dotnet test
```
The tests can take long the first time, as it download Bitcoin Core binaries. (Between 5 and 10 minutes)

## Licence

This project is under MIT License.

## Special thanks

Special thanks to Digital Garage for allowing me to open source the project, which is based on an internal work I have done on Elements.

Thanks to the DG Lab Blockchain Team who had to fight with lots of bugs. (in particular kallewoof :p)

Thanks to Metaco SA, whose constant challenging projects refine my taste on what a perfect Bitcoin API should be.
