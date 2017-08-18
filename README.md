# NBXplorer

A minimalist UTXO tracker for HD Wallets
The goal is to have a flexible, .NET based UTXO tracker for HD wallets.
The explorer supports only P2PKH derivation for now but will be able to support more complex generation in near future. (m-n, segwit, p2sh)

This explorer is not meant to be exposed on internet, but should be used as an internal tool for tracking the UTXOs of your own service.

## Prerequisite

* Install [.NET Core 2.0](https://www.microsoft.com/net/core)
* Have Bitcoin Core instance synched and running

## How to run?

If you are using Bitcoin core default settings:

```
git clone https://github.com/dgarage/NBXplorer
cd NBXplorer
dotnet run
```

Else, you will have to configure manually.

## How to configure?

If you are not using standard install for bitcoind, you will have to change the configuration file:
In Windows it is located on 

```
C:\Users\<user>\AppData\Roaming\NBXplorer\<network>\settings.config
```

On linux or mac:
```
~/.nbxplorer/<network>/settings.config
```

The configuration file allows to configure the connection to your Bitcoin RPC, and to choose a specific Bitcoin Cash node for broadcasting your transaciton.
By default, the configuration file is using cookie authentication in default bitcoin core path, and connect to a random Bitcoin Cash node. So if you run bitcoin core with default settings, things will just work.

The default config file is shows you all options, if you need to change, remove the sharp sign (#) and customize your settings:

```
####Common Commands####
####If Bitcoin Core is running with default settings, you should not need to modify this file####
####All those options can be passed by commandline through (like -port=19382)####
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

## Important Note

This tool will only start scanning from the configured `startheight`. (By default, the height of the blockchain during your first run)
This mean that you might not see old payment from you HD key.

If you need to see old payments, you need to configure `startheight` to a specific height of your choice, then run with again with `-rescan`.

## Licence

This project is under MIT License.

## Special thanks

Special thanks to Digital Garage for allowing me to open source the project, which is based on an internal work I have done on Elements.
Thanks to the DG Lab Blockchain Team who had to fight with lots of bugs. (in particular kallewoof :p)