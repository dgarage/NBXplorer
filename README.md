[![NuGet](https://img.shields.io/nuget/v/NBxplorer.Client.svg)](https://www.nuget.org/packages/NBxplorer.Client) [![Docker Automated buil](https://img.shields.io/docker/automated/jrottenberg/ffmpeg.svg)](https://hub.docker.com/r/nicolasdorier/nbxplorer/)

# NBXplorer

A minimalist UTXO tracker for HD Wallets.
The goal is to have a flexible, .NET based UTXO tracker for HD wallets.
The explorer supports only P2PKH derivation for now but will be able to support more complex generation in near future. (m-n, segwit, p2sh)

This explorer is not meant to be exposed on internet, but should be used as an internal tool for tracking the UTXOs of your own service.

It currently supports:

* Bitcoin
* Litecoin
* Dogecoin
* Groestlcoin
* Dash
* Polis
* BCash (also known as Bitcoin Cash)
* BGold (also known as Bitcoin Gold)
* Monacoin
* Feathercoin
* Ufo
* Viacoin

## Prerequisite

* Install [.NET Core 2.1](https://www.microsoft.com/net/download)
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
In Windows it is located in

```
C:\Users\<user>\AppData\Roaming\NBXplorer\<network>\settings.config
```

On linux or mac:
```
~/.nbxplorer/<network>/settings.config
```

The default configuration assumes `mainnet` with only `btc` chain supported, and uses the default settings of bitcoind.

You can change the location of the configuration file with the `--conf=pathToConf` command line argument.

### Command line parameters

#### From Source (.NET Core SDK required)
The same settings as above, e.g.
```dotnet run NBXplorer.dll --port=20300 --network=mainnet --nodeendpoint=127.0.0.1:32939```

#### From Built DLL (.NET Core Runtime required)
```dotnet NBXplorer.dll --port=20300 --network=mainnet --nodeendpoint=127.0.0.1:32939```

### Environment variables

The same settings as above, for example `export NBXPLORER_PORT=20300`. This is usefull for configuring docker.

## How to Run 

### Command Line

You can use the [dotnet](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet) command which is part of .NET Core to run NBXplorer. To run from source you must have the .NET Core SDK installed e.g. 
```dotnet run NBXplorer.dll```
As described above you may add configuration parameters if desired.

If you have a compiled version of NBXplorer you should have a file in your build folder named NBXplorer.dll. This cannot itself be directly executed on the command line as it is not an executable file. Instead we can use the `dotnet` runtime to execute the dll file.

e.g.
```dotnet NBXplorer.dll ```


## Important Note

This tool will only start scanning from the configured `startheight`. (By default, the height of the blockchain during your first run)
This means that you might not see old payments from your HD key.

If you need to see old payments, you need to configure `startheight` to a specific height of your choice, then run with again with `-rescan`.

## How to query?

### Using Postman
[Postman](https://www.getpostman.com) is a useful tool for testing and experimenting with REST API's. 

You can test the NBXplorer API quickly and easily using Postman as follows :
* Assumption: you are using the default Cookie Auth , you are running NBXplorer on the same machine as your BTC (or other supported crypto) node or NBXplorer can access the blockchain data files.
* Run NBXplorer and locate you cookie file - note NBXplorer will generate a new Cookie file each time it runs
* In Postman create a new GET API test
* In Authorization select *Basic Auth*, you should see 2 input boxes for username and password
* Open your cookie file with a text editor e.g. Notepad on windows . You should see a cookie string e.g. `__cookie__:0ff9cd83a5ac7c19a6b56a3d1e7a5c96e113d42dba7720a1f72a3a5e8c4b6c66`
* Back in Postman paste the `__cookie__` part of your cookie file into username (whatever comes before the :)
* Paste the Hex string (after the : ) into the password box
* Click the Update Request button in Postman - this will force Postman to generate the correct HTTP headers based on your cookie details
* You should now see a new entry in the Headers section with a Key of *Authorization* and Value of *Basic xxxxxxxxx* where the string after `Basic` will be your Base64 encoded username and password.

You are now ready to test the API - it is easiest to start with something simple such as the fees endpoint e.g.

```http://localhost:24444/v1/cryptos/btc/fees/3```

this should return a JSON payload e.g.

{
    "feeRate": 9,
    "blockCount": 3
}

#### Troubleshooting
If you receive a 401 Unauthorized then your cookie data is not working. Check you are using the current cookie by opening the cookie file again - also check the date/time of the cookie file to ensure it is the latest cookie (generated when you launched NBXplorer).

If you receive a 404 or timeout then Postman cannot see the endpoint
* are you using the correct Port ? 
* are you running postman on localhost ?

## Client API
A better documentation is on the way, for now the only documentation is the client API in C# on [nuget](https://www.nuget.org/packages/NBxplorer.Client).
The `ExplorerClient` classes allows you to query unused addresses, and the UTXO of an HD PubKey.
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
