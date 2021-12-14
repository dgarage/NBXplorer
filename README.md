[![NuGet](https://img.shields.io/nuget/v/NBxplorer.Client.svg)](https://www.nuget.org/packages/NBxplorer.Client) [![Docker Automated buil](https://img.shields.io/docker/automated/jrottenberg/ffmpeg.svg)](https://hub.docker.com/r/nicolasdorier/nbxplorer/)
[![Build status](https://ci.appveyor.com/api/projects/status/c7kqj7a3nf7vmhsk?svg=true)](https://ci.appveyor.com/project/NicolasDorier/nbxplorer) [![CircleCI](https://circleci.com/gh/dgarage/NBXplorer.svg?style=svg)](https://circleci.com/gh/dgarage/NBXplorer)

# NBXplorer

A minimalist UTXO tracker for HD Wallets.
The goal is to have a flexible, .NET based UTXO tracker for HD wallets.
The explorer supports P2SH,P2PKH,P2WPKH,P2WSH and Multi-sig derivation.

This explorer is not meant to be exposed on internet, but should be used as an internal tool for tracking the UTXOs of your own service.

It has a bunch of features:

* Can pass arguments via environment variable, command line or configuration file
* Automatically reconnect to your node if the connection goes temporarily down
* An easy to use REST API
* Persistence (via in-file no-SQL datbase called DBreeze)
* Connect via RPC to broadcast transaction instead of using the P2P protocol like this example
* Connect via RPC to your trusted node to get the proper fee rate.
* Altcoin support
* Huge test suite
* Pruning of transaction data (in practice, we don't need to save the whole transaction, only the spent outpoint and received coin for the wallet)
* Multi-wallet
* Flexible address generation schemes (multisig, segwit, legacy etc...)
* Pruning for big wallets (Removal of tracked transaction which do not impact the resulting UTXO set)

It currently supports the following altcoins:

* Althash
* Argoneum
* BCash (also known as Bitcoin Cash)
* BGold (also known as Bitcoin Gold)
* BitCore
* Chaincoin 
* ColossusXT
* Dash
* Dogecoin
* Feathercoin
* Gobyte
* Groestlcoin
* Litecoin
* Monacoin
* MonetaryUnit
* Monoeci
* Polis
* Qtum
* Terracoin
* Ufo
* Viacoin

Read our [API Specification](docs/API.md), or our the [internal design of NBXplorer](docs/Design.md).

## Prerequisite

* Install [.NET Core SDK v3.1.0 or above](https://www.microsoft.com/net/download)
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

## How to use the API?

Check [the API documentation](docs/API.md), you can then use any client library:
* [NBXplorer.NodeJS](https://github.com/junderw/NBXplorer.NodeJS) for NodeJS clients.
* [NBXplorer.Client](https://www.nuget.org/packages/NBxplorer.Client) for .NET clients.

Here is [a small C# example](Examples/MultiSig/Program.cs) showing a 2-2 multisig with Alice and Bob that you can run on regtest.

## With Docker

Use [our image](https://hub.docker.com/r/nicolasdorier/nbxplorer/).
You can check [the sample](docker-compose.regtest.yml) for configuring and composing it bitcoin core.

## How to develop on it?

If you are on Windows, I recommend Visual Studio 2017 update 4 (15.4).
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
Please note that NBXplorer uses cookie authentication by default. If you run your Bitcoin/Litecoin/Dash nodes using their daemon (like `bitcoind`, `litecoind` or `dashd`), they generate a new cookie every time you start them, and that should work without any extra configuration. 
If you run the node(s) using the GUI versions, like Bitcoin\Litecoin\Dash Core Qt with the `-server` parameter while you set the rpcusername and rpcpassword in their `.conf` files, you must set those values for every crypto you are planning to support. 
See samples below.

#### Run from source (requires .NET Core SDK)
You should use `run.ps1` (Windows) or `run.sh` (Linux) to execute NBXplorer, but you can also execute it manually with the following command:
```dotnet run --no-launch-profile -p .\NBXplorer\NBXplorer.csproj -- <parameters>```

#### Run using built DLL (requires .NET Core Runtime only)
If you already have a compiled DLL, you can run the executable with the following command:
```dotnet NBXplorer.dll <parameters>```

#### Sample parameters
Running NBXplorer HTTP server on port 20300, connecting to the BTC mainnet node locally.
```--port=20300 --network=mainnet --btcnodeendpoint=127.0.0.1:32939```

Running NBXplorer on testnet, supporting Bitcoin, Litecoin and Dash, using cookie authentication for BTC and LTC, and RPC username and password for Dash, connecting to all of them locally. 
```--chains=btc,ltc,dash --network=testnet --dashrpcuser=myuser --dashrpcpassword=mypassword```


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

If you need to see old payments, you need to configure `--[crypto]startheight` to a specific height of your choice, then run it again with `--[crypto]rescan`, e.g.    
`./run.sh --chains=ltc --ltcrescan --ltcstartheight=101`

## How to query?

### Using Postman
[Postman](https://www.getpostman.com) is a useful tool for testing and experimenting with REST API's. 

You can test the [NBXplorer API](docs/API.md) quickly and easily using Postman.

If you use cookie authentication (enabled by default) in your locally run NBXplorer, you need to set that up in Postman:
* Run NBXplorer and locate you cookie file (NBXplorer will generate a new Cookie file each time it runs in [its default data folder](docs/API.md#authentication))
* In Postman create a new GET API test
* In Authorization select *Basic Auth*, you should see 2 input boxes for username and password
* Open your cookie file with a text editor e.g. Notepad on windows . You should see a cookie string e.g. `__cookie__:0ff9cd83a5ac7c19a6b56a3d1e7a5c96e113d42dba7720a1f72a3a5e8c4b6c66`
* Back in Postman paste the `__cookie__` part of your cookie file into username (whatever comes before the :)
* Paste the Hex string (after the : ) into the password box
* Click the Update Request button in Postman - this will force Postman to generate the correct HTTP headers based on your cookie details
* You should now see a new entry in the Headers section with a Key of *Authorization* and Value of *Basic xxxxxxxxx* where the string after `Basic` will be your Base64 encoded username and password.
* Enter the API URL you are going to test

You can also disable authentication in NBXplorer for testing with the `--noauth` parameter. This makes testing quicker:
* Run NBXplorer with the `--noauth` parameter
* In Postman create a new GET API test
* In Authorization select *No Auth*
* Enter the API URL you are going to test

You are now ready to test the API - it is easiest to start with something simple such as the fees endpoint e.g.

```http://localhost:24444/v1/cryptos/btc/fees/3```

this should return a JSON payload e.g.

{
    "feeRate": 9,
    "blockCount": 3
}

## Message Brokers
### Azure Service Bus
Support has been added for Azure Service Bus as a message broker. Currently 2 Queues and 2 Topics are supported

### Queues
* New Block
* New Transaction

### Topics
* New Block
* New Transaction


Filters should be applied on the client, if required. 

To activate Azure Service Bus Mesages you should add an Azure Service Bus Connection string to your config file or on the command line.

* To use queues you should specify the queue names you wish to use
* To use topics you should specify the topic names you wish to use

You can use both queues and topics at the same time.

#### Config Settings

If you use the Configuration file to setup your NBXplorer options:

```ini
asbcnstr=Your Azure Service Bus Connection string
asbblockq=Name of queue to send New Block message to
asbtranq=Name of queue to send New Transaction message to
asbblockt=Name of topic to send New Block message to
asbtrant=Name of queue to send New Transaction message to
```

### RabbitMq
Support has been added for RabbitMq as a message broker. Currently 2 exchanges supported;

* New Block
* New Transaction

Filters can be applied on the client by defining routing keys;  

For transactions;  
* `transactions.#` to get all transactions.
* `transactions.[BTC].#` to get all [Bitcoin] transactions.
* `transactions.[BTC].confirmed` to get only confirmed [Bitcoin] transactions.
* `transactions.[BTC].unconfirmed` to get only unconfirmed [Bitcoin] transactions.
* `transactions.*.confirmed` to get all confirmed transactions.
* `transactions.*.unconfirmed` to get all unconfirmed transactions.

For blocks;    
* `blocks.#` to get all blocks.
* `blocks.[BTC]` to get all [Bitcoin] blocks.

To activate RabbitMq mesages you should add following settings to your config file or on the command line.

* rmqhost, rmquser, rmqpass

#### Config Settings

If you use the Configuration file to setup your NBXplorer options:

```ini
rmqhost= RabbitMq host name
rmqvirtual= RabbitMq virtual host
rmquser= RabbitMq username
rmqpass= RabbitMq password
rmqtranex= Name of exchange to send transaction messages
rmqblockex= Name of exchange to send block messages
```

Payloads are JSON and map to `NewBlockEvent`, `NewTransactionEvent` in the `NBXplorer.Models` namespace. There is no support in NBXplorer client for message borkers at the current time. You will need to use the `Serializer` in `NBXplorer.Client` to de-serialize the objects or then implement your own JSON de-serializers for the custom types used in the payload.  

For configuring serializers you can get crypto code info from `BasicProperties.Headers[CryptoCode]` of RabbitMq messages or `UserProperties[CryptoCode]` of Azure Service Bus messages.  
Examples can be found in unit tests.

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
