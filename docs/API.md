# API Specification

NBXplorer is a multi crypto currency lightweight block explorer.

NBXplorer does not index the whole blockchain, rather, it listens transactions and blocks from a trusted full node and index only addresses and transactions which belongs to a `DerivationScheme` that you decide to track.

## Authentication

By default a cookie file is generated when NBXplorer is starting, for windows in:
```
C:\Users\<user>\AppData\Roaming\NBXplorer\<network>\.cookie
```

On linux or mac:
```
~/.nbxplorer/<network>/.cookie
```

The content of this cookie must be used is used as HTTP BASIC authentication to use the API.

This can be disabled with `--noauth`.

Also, NBXPlorer listen by default on `127.0.0.1`, if you want to access it from another machine, run `--bind "0.0.0.0"`.

## Derivation Scheme Format

A derivation scheme, also called derivationStrategy in the code, is a flexible way to define how to generate address of a wallet.
NBXplorer will track any addresses on the `0/x`, `1/x` and `x` path.

Here a documentation of the different derivation scheme supported:

| Address type | Format |
| ------------- |-------------|
| P2WPKH | xpub1 |
| P2SH-P2WPKH | xpub1-[p2sh] |
| P2PKH | xpub-[legacy] |
| Multi-sig P2WSH | 2-of-xpub1-xpub2 |
| Multi-sig P2SH-P2WSH | 2-of-xpub1-xpub2-[p2sh] |
| Multi-sig P2SH | 2-of-xpub1-xpub2-[legacy] |

For multisig, the public keys are ordered before generating the address by default for privacy reason, use `-[keeporder]` to disable it.

You can use more than one options at same time, example: `2-of-xpub1-xpub2-[legacy]-[keeporder]`

## CryptoCode

Most of routes asks for a crypto code. This identify the crypto currency to request data from. Currently supported is `BTC` and `LTC`.

## Track a derivation scheme

After this call, the specified `derivation scheme` will be tracked by NBXplorer

HTTP POST v1/cryptos/{cryptoCode}/derivations/{derivationScheme}

Returns nothing.

## Track a specific address

After this call, the specified address will be tracked by NBXplorer

HTTP POST v1/cryptos/{cryptoCode}/addresses/{address}

Returns nothing.

## Query transactions associated to a derivationScheme

To query all transactions of a `derivation scheme`:

HTTP GET v1/cryptos/{cryptoCode}/derivations/{derivationScheme}/transactions

To query a specific transaction:

HTTP GET v1/cryptos/{cryptoCode}/derivations/{derivationScheme}/transactions/{txId}

Optional Parameters:

* `includeTransaction` includes the hex of the transaction, not only information (default: true)

Returns:

```
{
  "height": 104,
  "confirmedTransactions": {
    "transactions": [
      {
        "blockHash": "3e7bcca309f92ab78a47c1cdd1166de9190fa49e97165c93e2b10ae1a14b99eb",
        "confirmations": 1,
        "height": 104,
        "transactionId": "cc33dfaf2ed794b11af83dc6e29303e2d8ff9e5e29303153dad1a1d3d8b43e40",
        "transaction": "020000000166d6befa387fd646f77a10e4b0f0e66b3569f18a83f77104a0c440e4156f80890000000048473044022064b1398653171440d3e79924cb6593633e7b2c3d80b60a2e21d6c6e287ee785a02203899009df443d0a0a1b06cb970aee0158d35166fd3e26d4e3e85570738e706d101feffffff028c02102401000000160014ee0a1889783da2e1f9bba47be4184b6610efd00400e1f5050000000016001452f88af314ef3b6d03d40a5fd1f2c906188a477567000000",
        "outputs": [
          {
            "keyPath": "0/0",
            "scriptPubKey": "001452f88af314ef3b6d03d40a5fd1f2c906188a4775",
            "index": 1,
            "value": 100000000
          }
        ],
        "inputs": [],
        "timestamp": 1540381888,
        "balanceChange": 100000000
      }
    ]
  },
  "unconfirmedTransactions": {
    "transactions": [
      {
        "blockHash": null,
        "confirmations": 0,
        "height": null,
        "transactionId": "7ec0bcbd3b7685b6bbdb4287a250b64bfcb799dbbbcffa78c00e6cc11185e5f1",
        "transaction": null,
        "outputs": [
          {
            "keyPath": "0",
            "scriptPubKey": "0014b39fc4eb5c6dd238d39449b70a2e30d575426d99",
            "index": 1,
            "value": 100000000
          }
        ],
        "inputs": [],
        "timestamp": 1540381889,
        "balanceChange": 100000000
      }
    ]
  },
  "replacedTransactions": {
    "transactions": []
  }
}
```

## Query transactions associated to a specific address

Query all transactions of a tracked address. (Only work if you called the Track operation on this specific address)

HTTP GET v1/cryptos/{cryptoCode}/addresses/{address}/transactions

Optional Parameters:

* `includeTransaction` includes the hex of the transaction, not only information (default: true)

Returns:

```json
{
  "height": 104,
  "confirmedTransactions": {
    "transactions": [
      {
        "blockHash": "3e7bcca309f92ab78a47c1cdd1166de9190fa49e97165c93e2b10ae1a14b99eb",
        "confirmations": 1,
        "height": 104,
        "transactionId": "cc33dfaf2ed794b11af83dc6e29303e2d8ff9e5e29303153dad1a1d3d8b43e40",
        "transaction": "020000000166d6befa387fd646f77a10e4b0f0e66b3569f18a83f77104a0c440e4156f80890000000048473044022064b1398653171440d3e79924cb6593633e7b2c3d80b60a2e21d6c6e287ee785a02203899009df443d0a0a1b06cb970aee0158d35166fd3e26d4e3e85570738e706d101feffffff028c02102401000000160014ee0a1889783da2e1f9bba47be4184b6610efd00400e1f5050000000016001452f88af314ef3b6d03d40a5fd1f2c906188a477567000000",
        "outputs": [
          {
            "scriptPubKey": "001452f88af314ef3b6d03d40a5fd1f2c906188a4775",
            "index": 1,
            "value": 100000000
          }
        ],
        "inputs": [],
        "timestamp": 1540381888,
        "balanceChange": 100000000
      }
    ]
  },
  "unconfirmedTransactions": {
    "transactions": [
      {
        "blockHash": null,
        "confirmations": 0,
        "height": null,
        "transactionId": "7ec0bcbd3b7685b6bbdb4287a250b64bfcb799dbbbcffa78c00e6cc11185e5f1",
        "transaction": null,
        "outputs": [
          {
            "scriptPubKey": "0014b39fc4eb5c6dd238d39449b70a2e30d575426d99",
            "index": 1,
            "value": 100000000
          }
        ],
        "inputs": [],
        "timestamp": 1540381889,
        "balanceChange": 100000000
      }
    ]
  },
  "replacedTransactions": {
    "transactions": []
  }
}
```

## Query a single transaction associated to a address or derivation scheme

HTTP GET v1/cryptos/{cryptoCode}/derivations/{derivationScheme}/transactions/{txId}
HTTP GET v1/cryptos/{cryptoCode}/addresses/{address}/transactions/{txId}

Error codes:

* HTTP 404: Transaction not found

Optional Parameters:

* `includeTransaction` includes the hex of the transaction, not only information (default: true)

Returns:

```json
{
    "blockHash": null,
    "confirmations": 0,
    "height": null,
    "transactionId": "7ec0bcbd3b7685b6bbdb4287a250b64bfcb799dbbbcffa78c00e6cc11185e5f1",
    "transaction": null,
    "outputs": [
        {
        "scriptPubKey": "0014b39fc4eb5c6dd238d39449b70a2e30d575426d99",
        "index": 1,
        "value": 100000000
        }
    ],
    "inputs": [],
    "timestamp": 1540381889,
    "balanceChange": 100000000
}
```

## Get a transaction

HTTP GET v1/cryptos/{cryptoCode}/transactions/{txId}

Optional Parameters:

* `includeTransaction` includes the hex of the transaction, not only information (default: true)

Error codes:

* HTTP 404: Transaction not found

Returns:

```json
{
  "confirmations": 3,
  "blockId": "5efa23803df818cd21faa0c11e84db28c8352e76acb93d0c0adfe123db827190",
  "transactionHash": "ed86c55b519c26ab4ba8130c976294753934c1f9f6d30203e65bb222648a8cdf",
  "transaction": "0200000001205dcde69a5bd2b3281d387e6f125338f9ccb904d94df383ff56d9923599681e000000004847304402200b9d78e01691339acb238d7cd7a40ae620796bdcf8cb167dff4e100b71a2b0950220518e3a955ea7229d57c0160ecf491e8048662d7112fe5feaa312ff71388fda9701feffffff028c02102401000000160014a4ccb74ada7dd01b3018c3308894fea27b4813be00e1f5050000000016001408f86300ddff26ddf779ddce833f7e9e7442156c67000000",
  "height": 104,
  "timestamp": 1540390804
}
```

`height` and `blockId` will be null if the transaction is not confirmed.

## Get connection status to the chain

HTTP GET v1/cryptos/{cryptoCode}/status

Returns:
```json
{
  "bitcoinStatus": {
    "blocks": 103,
    "headers": 103,
    "verificationProgress": 1.0,
    "isSynched": true,
    "incrementalRelayFee": 1,
    "minRelayTxFee": 1,
    "capabilities": {
      "canScanTxoutSet": true,
      "canSupportSegwit": true
    }
  },
  "repositoryPingTime": 0.0087891999999999987,
  "isFullySynched": true,
  "chainHeight": 103,
  "syncHeight": 103,
  "networkType": "Regtest",
  "cryptoCode": "BTC",
  "supportedCryptoCodes": [
    "BTC"
  ],
  "version": "1.0.3.5"
}
```

## Get a new unused address

HTTP GET v1/cryptos/{cryptoCode}/derivations/{derivationScheme}/addresses/unused

Error codes:

* HTTP 404: `cryptoCode-not-supported`
* HTTP 400: `strategy-not-found`

Optional parameters:

* `feature`: Use `Deposit` to get a deposit address (`0/x`), `Change` to get a change address (`1/x`), or `Direct` to get `x` (default: `Deposit`)
* `skip`: How much address to skip, needed if the user want multiple unused addresses (default:0)
* `reserve`: Mark the returned address as used (default: false)

Returns:

```json
{
  "trackedSource": "DERIVATIONSCHEME:tpubD6NzVbkrYhZ4Wo2RMq8Xbnrorf1xnABkKMS3EGshPkQ3Z4N4GN8uyLuDPvnK7Ekc2FHdXbLvcuZny1gPiohMksFGKmaX3APD2DbTeBWj751-[p2sh]",
  "feature": "Deposit",
  "derivationStrategy": "tpubD6NzVbkrYhZ4Wo2RMq8Xbnrorf1xnABkKMS3EGshPkQ3Z4N4GN8uyLuDPvnK7Ekc2FHdXbLvcuZny1gPiohMksFGKmaX3APD2DbTeBWj751-[p2sh]",
  "keyPath": "0/2",
  "scriptPubKey": "a91412cbf6154ef6d9aecf9c978dc2bdc43f1881dd5f87",
  "address": "2MtxcVDMiRrJ3V4zfsAwZGbZfPiDUxSXDY2",
  "redeem": "0014e2eb89edba1fe6c6c0863699eeb78f6ec3271b45"
}
```

Note: `redeem` is returning the segwit redeem if the derivation scheme is a P2SH-P2WSH or P2WSH, or the p2sh redeem if just a p2sh.

## Get ExtPubKey from scriptPubKey

HTTP GET v1/cryptos/{cryptoCode}/derivations/{derivationScheme}/scripts/{script}

Error codes:

* HTTP 404: `cryptoCode-not-supported`

Returns:
```json
{
  "trackedSource": "DERIVATIONSCHEME:tpubD6NzVbkrYhZ4WcPozSqALNCrJEt4C45sPDhEBBuokoCeDgjX6YTs4QVvhD9kao6f2uZLqZF4qcXprYyRqooSXr1uPp1KPH1o4m6aw9nxbiA",
  "feature": "Deposit",
  "derivationStrategy": "tpubD6NzVbkrYhZ4WcPozSqALNCrJEt4C45sPDhEBBuokoCeDgjX6YTs4QVvhD9kao6f2uZLqZF4qcXprYyRqooSXr1uPp1KPH1o4m6aw9nxbiA",
  "keyPath": "0/0",
  "scriptPubKey": "001460c25d29559774803f262acf5ee5c922eff52ccd",
  "address": "tb1qvrp96224ja6gq0ex9t84aewfythl2txdkpdmu0"
}
```

## Get Unspent Transaction Outputs (UTXOs)

HTTP GET v1/cryptos/{cryptoCode}/derivations/{derivationScheme}/utxos

Error:

* HTTP 404: `cryptoCode-not-supported`

Result:

```json
{
  "trackedSource": "DERIVATIONSCHEME:tpubD6NzVbkrYhZ4XQVi1sSEDBWTcicDqVSCTnYDxpwGwcSZVbPii2b7baRg57YfL64ed36sBRe6GviihHwhy3D1cnBe5uXb27DjrDZCKUA7PQi",
  "derivationStrategy": "tpubD6NzVbkrYhZ4XQVi1sSEDBWTcicDqVSCTnYDxpwGwcSZVbPii2b7baRg57YfL64ed36sBRe6GviihHwhy3D1cnBe5uXb27DjrDZCKUA7PQi",
  "currentHeight": 107,
  "unconfirmed": {
    "utxOs": [
      {
        "feature": "Deposit",
        "outpoint": "10ba4bcadd03130b1bd98b0bc7aea9910f871b25b87ec06e484456e84440c88a01000000",
        "index": 1,
        "transactionHash": "8ac84044e85644486ec07eb8251b870f91a9aec70b8bd91b0b1303ddca4bba10",
        "scriptPubKey": "00149681ae465a045e2068460b9d281cf97dede87cd8",
        "value": 100000000,
        "keyPath": "0/0",
        "timestamp": 1540376171,
        "confirmations": 0
      }
    ],
    "spentOutpoints": [],
    "hasChanges": true
  },
  "confirmed": {
    "utxOs": [
      {
        "feature": "Deposit",
        "outpoint": "29ca6590f3f03a6523ad79975392e74e385bf2b7dafe6c537ffa12f9e124348800000000",
        "index": 0,
        "transactionHash": "883424e1f912fa7f536cfedab7f25b384ee792539779ad23653af0f39065ca29",
        "scriptPubKey": "001436a37f2f508650f7074bec4d091fc82bb01cc57f",
        "value": 50000000,
        "keyPath": "0/3",
        "timestamp": 1540376174,
        "confirmations": 1
      }
    ],
    "spentOutpoints": [
      "9345f9585d643a31202e686ec7a4c2fe17917a5e7731a79d2327d24d25c0339f01000000"
    ],
    "hasChanges": true
  },
  "hasChanges": true
}
```

This call does not returns conflicted unconfirmed UTXOs.

## Get Unspent Transaction Outputs of a specific address

Assuming you use Track on this specific address:

HTTP GET v1/cryptos/{cryptoCode}/addresses/{address}/utxos

Error:

* HTTP 404: `cryptoCode-not-supported`

Result:

```json
{
  "trackedSource": "ADDRESS:moD8QpWufPMFP9y7gC8m5ih9rmejavbf3K",
  "currentHeight": 105,
  "unconfirmed": {
    "utxOs": [],
    "spentOutpoints": [],
    "hasChanges": true
  },
  "confirmed": {
    "utxOs": [
      {
        "outpoint": "f532022bebe8d90c72853a2663c26ca9d42fad5d9cde21d35bad38135a5dfd0701000000",
        "index": 1,
        "transactionHash": "07fd5d5a1338ad5bd321de9c5dad2fd4a96cc263263a85720cd9e8eb2b0232f5",
        "scriptPubKey": "76a9145461f6c342451142e07d95dd2a42b48af9114cea88ac",
        "value": 100000000,
        "timestamp": 1540390664,
        "confirmations": 2
      },
      {
        "outpoint": "a470a71144d4cdaef2b9bd8d24f20ebc8d6548bae523869f8cceb2cef5b4538a01000000",
        "index": 1,
        "transactionHash": "8a53b4f5ceb2ce8c9f8623e5ba48658dbc0ef2248dbdb9f2aecdd44411a770a4",
        "scriptPubKey": "76a9145461f6c342451142e07d95dd2a42b48af9114cea88ac",
        "value": 100000000,
        "timestamp": 1540390666,
        "confirmations": 1
      },
      {
        "outpoint": "1710a1b61cb1f988182347be52a16502bae5a78fa9740a68107f9ddc6e30896a00000000",
        "index": 0,
        "transactionHash": "6a89306edc9d7f10680a74a98fa7e5ba0265a152be47231888f9b11cb6a11017",
        "scriptPubKey": "76a9145461f6c342451142e07d95dd2a42b48af9114cea88ac",
        "value": 60000000,
        "timestamp": 1540390666,
        "confirmations": 1
      }
    ],
    "spentOutpoints": [],
    "hasChanges": true
  },
  "hasChanges": true
}
```

This call does not returns conflicted unconfirmed UTXOs.

## Notifications via websocket

NBXplorer implements real-time notification via websocket supports for new block or transaction.

HTTP GET v1/cryptos/{cryptoCode}/connect

Once you are connected to the websocket, you can subscribe to block notifications by sending the following JSON to it.

```
{
  "type": "subscribeblock",
  "data": {
    "cryptoCode": "BTC"
  }
}
```

Then a notification will be delivered through the websocket when a new block is mined:

```
{
  "type": "newblock",
  "data": {
    "height": 104,
    "hash": "10b0e5178aaf42c4a938f0d37430413b7d76feae14b01fc07e1f23300b8821ce",
    "previousBlockHash": "4c6a9c1cadf143c87249519639e86e236feac9d3cea2904e4c42bc5bc32a48a7",
    "cryptoCode": "BTC"
  }
}
```

For notification concerning `Derivation Scheme` transactions, you can subscribe by sending through the websocket:

```
{
  "type": "subscribetransaction",
  "data": {
    "cryptoCode": "BTC",
    "derivationSchemes": [
      "tpubD6NzVbkrYhZ4YL91Ez5fdgaBPQbFhedFdn5gQL4tSCJn1usmHsV1L6VokzLbgcqzh9hiBnfnQANp5BYW15QdFGRKspZVSW1v2QY917RDs1V-[legacy]"
    ]
  }
}
```

Then you will receive such notifications when a transaction is impacting the `derivation scheme`:

```
{
  "type": "newtransaction",
  "data": {
    "blockId": null,
    "trackedSource": "DERIVATIONSCHEME:tpubD6NzVbkrYhZ4X2p2D8kx6XV9V5iCJKMBHuBim1BLnZAZC1JobYkdwSrwF8R74V2oUWkJG3H24LwxnXs9wb6Ksivs2gj4RudMteyVai2AsmA-[p2sh]",
    "derivationStrategy": "tpubD6NzVbkrYhZ4X2p2D8kx6XV9V5iCJKMBHuBim1BLnZAZC1JobYkdwSrwF8R74V2oUWkJG3H24LwxnXs9wb6Ksivs2gj4RudMteyVai2AsmA-[p2sh]",
    "transactionData": {
      "confirmations": 0,
      "blockId": null,
      "transactionHash": "f135537b40ac7a524273176b60e464b7f279f622031ec53af302d959966d7364",
      "transaction": "0200000001dd7f53b09438fed83abe25dd6cdc30ee2092ce8c855cb9e7b0faa38aba8bc0f500000000484730440220093a837ff4be4b64b2ed4625abb128966caad0cb7830cac7af4f615bbf6b52ce02206227a3ddec3fac9e49f414eeab1388d0e67829620ac3a8fb2f4bbfc5b67bd02901feffffff0200e1f5050000000017a91476de0c5d07fd202880672bc702162b7f18e13aca87640210240100000017a9147cfa038496438a6d3c95cfac990f4dffc6cb44f28768000000",
      "height": null,
      "timestamp": 1540434424
    },
    "outputs": [
      {
        "keyPath": "0/1",
        "scriptPubKey": "a91476de0c5d07fd202880672bc702162b7f18e13aca87",
        "redeem": "00147d31e1c7959cd047bb7b9b35e4c877a28efe2f0b",
        "index": 0,
        "value": 100000000
      }
    ],
    "cryptoCode": "BTC"
  }
}
```

If you want all transactions of all derivation schemes of BTC, send this to the WebSocket:

```
{
  "type": "subscribetransaction",
  "data": {
    "cryptoCode": "BTC"
  }
}
```

If you want all transactions of all derivation schemes of all crypto currencies, send this to the WebSocket:

```
{
  "type": "subscribetransaction",
  "data": {
    "cryptoCode": "*"
  }
}
```

## Broadcast a transaction

HTTP POST v1/cryptos/{cryptoCode}/transactions

Body:

Raw bytes of the transaction.

Error codes:

* HTTP 404: `cryptoCode-not-supported`
* HTTP 400: `rpc-unavailable`

Returns:

```
{  
   "success":false,
   "rpcCode":-25,
   "rpcCodeMessage":"General error during transaction submission",
   "rpcMessage":"Missing inputs"
}
```

## Rescan a transaction

NBXplorer does not rescan the whole blockchain when tracking a new derivation scheme.
This means that if the derivation scheme already received UTXOs in the past, NBXplorer will not be aware of it and might reuse addresses already generated in the past, and will not show past transactions.

By using this route, you can ask NBXplorer to rescan specific transactions found in the blockchain.
This way, the transactions and the UTXOs present before tracking the derivation scheme will appear correctly.

HTTP POST v1/cryptos/{cryptoCode}/rescan

Body:

```
{  
   "transactions":[  
      # Specify the blockId and transactionId to scan. Your node must not be pruned for this to work.
      {  
         "blockId":"19b44484c79c40d4e74da406e25390348b86a252c1ab784cfd7198c724a0169f",
         "transactionId":"f83c7f31e2c39202bbbca619ab354ca8841721cf3440a253e056a7bea43e9745",
      },
      # Only the transactionId is specified. Your node must run --txindex=1 for this to work
      {  
         "transactionId":"754c14060b958de0ff4e77e2ccdca617964c939d40ec9a01ef21fca2aad78d00",
      },
      # This will index the transaction without using RPC. Careful: A wrong blockId will corrupt the database.
      {  
         "blockId":"19b44484c79c40d4e74da406e25390348b86a252c1ab784cfd7198c724a0169f",
         "transaction":"02000000000101008dd7aaa2fc21ef019aec409d934c9617a6dccce2774effe08d950b06144c750000000000feffffff026c3e2e12010000001600143072110b34b66acd9469b2882d6d57a8ae27183900e1f505000000001600140429b3eebb7d55c50ca36ace12ae874ff2fd16af0247304402202e32739cc6e42877699d4159159941f3cc39027c7626f9962cca9a865816d43502205389e9d6c1a4cab41f2c504413cf0f46a5c1f8814f368e03c9bf1f8017c6787e012103b8858085f2a0c9c906fb793bedb2c115c340de1f7b279d6099f675ddf3eec0bf67000000"
      }
   ]
}
```

Returns:

HTTP 200

Error codes:

* HTTP 400: `rpc-unavailable`

## Get fee rate

HTTP GET v1/cryptos/{cryptoCode}/fees/{blockCount}

Get expected fee rate for being confirmed in `blockCount` blocks.

Error codes:

* HTTP 400: `fee-estimation-unavailable`
* HTTP 404: `cryptoCode-not-supported`
* HTTP 400: `rpc-unavailable`

Returns:

```
{
    "feeRate": 5,
    "blockCount": 1
}
```

The fee rate is in satoshi/byte.

## Scan UTXO Set

NBXplorer can scan the UTXO Set for output belonging to your derivationScheme.

HTTP POST v1/cryptos/BTC/derivations/{derivationScheme}/utxos/scan

In order to not consume too much RAM, NBXplorer splits the addresses to scan in several `batch` and scan the whole UTXO set sequentially.
Three branches are scanned: 0/x, 1/x and x.

If a UTXO in one branch get found at a specific x, then all addresses inferior to index x will be considered used and not proposed when fetching a new unused address.

Query parameters:

* `batchSize` the number of addresses scanned at once per derivation scheme branch (default: 1000)
* `gapLimit` If no UTXO are detected in this interval, the scan stop (default: 10000)
* `from` the first address index to check (default: 0)

This call queue the request for scanning and returns immediately.

Error codes:

* HTTP 405: `scanutxoset-not-suported` ScanUTXOSet is not supported for this currency
* HTTP 409: `scanutxoset-in-progress` ScanUTXOSet has already been called for this derivationScheme
* HTTP 400: `rpc-unavailable`

## Get scan status

You can poll the status of the scan. Note that if the scan is complete, the result will be kept for 24H.
The state can be:

* `Queued` the demand has been done, but the scan request is queuing to be started
* `Pending` the scan is in progress
* `Complete` the scan is successful
* `Error` the scan errored

```json
{
  "error": null,
  "queuedAt": 1540439841,
  "status": "Pending",
  "progress": {
    "startedAt": 1540439841,
    "completedAt": null,
    "found": 2,
    "batchNumber": 9,
    "remainingBatches": 1,
    "currentBatchProgress": 50,
    "remainingSeconds": 10,
    "overallProgress": 91,
    "from": 900,
    "count": 100,
    "totalSearched": 2700,
    "totalSizeOfUTXOSet": null,
    "highestKeyIndexFound": {
      "change": null,
      "deposit": 51,
      "direct": null
    }
  }
}
```

`TotalSizeOfUTXOSet` is set only when the scan is complete.

Error codes:

* HTTP 404 `scanutxoset-info-not-found` if the scan has been done above the last 24H.

## Query event stream

All notifications sent through websocket are also saved in a crypto specifc event stream.

HTTP GET v1/cryptos/{cryptoCode}/events

Query parameters:

* `lastEventId`: Will query all events which happened after this event id, the first event has id 1 (default: 0)
* `longPolling`: If no events have been received since `lastEventId`, the call will block (default: false)
* `limit`: Limit the maximum number of events to return (default: null)

All events are registered in a query stream which you can replay by keeping track of the `lastEventId`.
The smallest `eventId` is 1.

```json
[
  {
    "eventId": 1,
    "type": "newblock",
    "data": {
      "height": 104,
      "hash": "1f31c605c0a5d54b65fa39dc8cb4db025be63c66280279ade9338571a9e63d35",
      "previousBlockHash": "7639350b31f3ce07ff976ebae772fef1602b30a10ccb8ca69047fe0fe8b9083c",
      "cryptoCode": "BTC",
    }
  },
  {
    "eventId": 2,
    "type": "newtransaction",
    "data": {
      "blockId": null,
      "trackedSource": "DERIVATIONSCHEME:tpubD6NzVbkrYhZ4XfeFUTn2D4RQ7D5HpvnHywa3eZYhxZBriRTsfe8ZKFSDMcEMBqGrAighxxmq5VUqoRvo7DnNMS5VbJjRHwqDfCAMXLwAL5j",
      "derivationStrategy": "tpubD6NzVbkrYhZ4XfeFUTn2D4RQ7D5HpvnHywa3eZYhxZBriRTsfe8ZKFSDMcEMBqGrAighxxmq5VUqoRvo7DnNMS5VbJjRHwqDfCAMXLwAL5j",
      "transactionData": {
        "confirmations": 0,
        "blockId": null,
        "transactionHash": "500359d971698c021587ea952bd38bd57dafc2b99615f71f7f978af394682737",
        "transaction": "0200000001b8af58c5dbed4bd0ea60ae8ba7e68e66143440b8c1c69b6eaaf719566676ab1b0000000048473044022040b419aeb9042a53fb2d03abec911901ed42fc50d6a143e322bc61d51e4e35a9022073c10fe827b53332d50fbde581e36ad31f57b98ec35a125562dc8c739762ec8901feffffff028c02102401000000160014b6bedaf0cb795c01a1e427bd7752d6ef058964f100e1f50500000000160014c5e0b07f40b8dbe69b22864d84d83d5b4120835368000000",
        "height": null,
        "timestamp": 1542703963
      },
      "outputs": [
        {
          "keyPath": "0/0",
          "scriptPubKey": "0014c5e0b07f40b8dbe69b22864d84d83d5b41208353",
          "index": 1,
          "value": 100000000
        }
      ],
      "cryptoCode": "BTC",
    }
  }
]
```

## Create Partially Signed Bitcoin Transaction

Create a [Partially Signed Bitcoin Transaction](https://github.com/bitcoin/bips/blob/master/bip-0174.mediawiki) (PSBT).

A PSBT is a standard format to represent a transaction with pending signatures associated to it.
A PSBT can be signed independently by many signers, and combined together before broadcast.

HTTP GET v1/cryptos/{cryptoCode}/derivations/{derivationScheme}/psbt/create

Error codes:

* HTTP 400: `not-enough-funds`
* HTTP 400: `fee-estimation-unavailable`
* HTTP 404: `cryptoCode-not-supported`

Fields:

```json
{
  "seed": 1,
  "rbf": false,
  "destinations": [
    {
      "destination": "mu5kevv6FiLygJfVvxQnB4hArXCUArMC7C",
      "amount": 50000000,
      "substractFees": false,
      "sweepAll": false
    }
  ],
  "feePreference": {
    "explicitFeeRate": 10,
    "explicitFee": 23000,
    "blockTarget": 1,
    "fallbackFeeRate": 100
  },
  "reserveChangeAddress": false
}
```

* `seed`: Optional, default to null, a seed to specific to get a deterministic PSBT (useful for tests)
* `rbf`: Optional, default to false, determine if the transaction should have Replace By Fee (RBF) activated
* `reserveChangeAddress`: default to false, Whether the creation of this PSBT will reserve a new change address.
* `destinations`: Required, the destinations where to send the money
* `destinations[].destination`: Required, the destination address
* `destinations[].amount` Send this amount to the destination (Mutually exclusive with: sweepAll)
* `destinations[].substractFees` Default to false, will substract the fees of this transaction to this destination (Mutually exclusive with: sweepAll)
* `destinations[].sweepAll` Deault to false, will sweep all the balance of your wallet to this destination (Mutually exclusive with: amount, substractFees)
* `feePreference`: Optional, determines how fees for the transaction are calculated, default to the full node estimation for 1 block target.
* `feePreference.explicitFeeRate`: An explicit fee rate for the transaction in Satoshi per vBytes (Mutually exclusive with: blockTarget, explicitFee, fallbackFeeRate)
* `feePreference.explicitFee`: An explicit fee for the transaction in Satoshi (Mutually exclusive with: blockTarget, explicitFeeRate, fallbackFeeRate)
* `feePreference.blockTarget`: A number of blocks after which the user expect one confirmation (Mutually exclusive with: explicitFeeRate, explicitFee)
* `feePreference.fallbackFeeRate`: If the NBXplorer's node does not have proper fee estimation, this specific rate will be use in Satoshi per vBytes, this make sure that `fee-estimation-unavailable` is never sent. (Mutually exclusive with: explicitFeeRate, explicitFee)

Response:

```json
{
  "psbt": "cHNidP8BAHcBAAAAASjvZHM29AbxO4IGGHbk3IE82yciSQFr2Ihge7P9P1HeAQAAAAD/////AmzQMAEAAAAAGXapFG1/TpHnIajdweam5Z3V9s6oGWBRiKyAw8kBAAAAABl2qRSVNmCfrnVeIwVkuTrCR6EvRFCP7IisAAAAAAABAP10AQEAAAACe9C2c9VL+gfYpic4c+Wk/Nn7bvhewA82owtcUDo/tPoAAAAAakcwRAIgUlLS0SDj7IXeY44x21eUg16Vh4qbJe+NDQ/ywUrB84kCIGLU5Vec2bjL1DZhUmDueLrf0uh/PycOK7FWg/Ptvwi0ASED7OpQGf+HzIRwWKZ1Hmd8h6vxkFOt5RlJ3u/flzNTesv/////818+qp4hLnw9DWOD+a601fLjFciZ/4iCNT1M9g+kMvkAAAAAakcwRAIgfk+bUUYfRs6AU1mt5unV4fZxCit34g8pE5fsawUM7H0CIBGpSil8+JCHdAHxKU2I7CvEBzAyz3ggd9RlH+QQSnlkASEC/wwlQ07b3xdSQaEf+wRJEnzEJT2GPNTY4Wb3Gg1hxFz/////AoDw+gIAAAAAGXapFHoZHSjaWNcmJk7sSHvRG29RaqIiiKxQlPoCAAAAABl2qRTSKm2x4ITWeuYLwCv3PUDtt+CL+YisAAAAACIGA1KRWHyJqdpbUzuezCSzj4+bj1+gNWGEibLG0BMj9/RmDDAn+hsBAAAAAgAAAAAiAgIuwas0MohgjmGIXoOgS95USEDawK//ZqrVEi5UIfP/FAwwJ/obAQAAAAMAAAAAAA==",
  "changeAddress": "mqVvTQKsdJ36Z8m5uFWQSA5nhrJ5NHQ2Hs"
}
```

* `psbt`: The partially signed bitcoin transaction in Base64.
* `changeAddress`: The change address of the transaction, useful for tests (can be null) 