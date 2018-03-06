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

## Bookmarks

Some routes allow the user to specify a bookmark.
Bookmark are used to decrease the traffic between NBXplorer and the client by providing a way for the client to give his current state.

NBXplorer will then just transmit what changed from the client's known state.

## CryptoCode

Most of routes asks for a crypto code. This identify the crypto currency to request data from. Currently supported is `BTC` and `LTC`.

## Track a derivation scheme

After this call, the specified `derivation scheme` will be tracked by NBXplorer

HTTP POST v1/cryptos/{cryptoCode}/derivations/{derivationScheme}

Returns nothing.

## Query transactions

Query all transactions of a `derivation scheme`.

HTTP GET v1/cryptos/{cryptoCode}/derivations/{derivationScheme}/transactions

Optional Parameters:

* `unconfirmedBookmarks` bookmarks known by the client of transactions which have not yet been mined.
* `confirmedBookmarks` bookmarks known by the client of transactions which have been mined.
* `replacedBookmarks` bookmark known by the client of transactions which have been replaced. (RBF)
* `includeTransaction` includes the hex of the transaction, not only information (default: true)
* `longPolling` blocks the call until a change happens since the passed bookmarks (default: false)

Returns:

```
{  
   "height":101,
   "confirmedTransactions":{  
      "knownBookmark":null,
      "bookmark":"cac706ff1568b4e4ffbb19beb8f850ab669eb05f",
      "transactions":[  
         {  
            "blockHash":"2178abc9e2174fff049794c5b859a93fda80d70e8806fd9708e34172f8061130",
            "confirmations":1,
            "height":104,
            "transactionId":"3a5ff59e9767f06a49f468c6b5cfd95ce3d4b37d769538e4e64eddd370004ebd",
            "transaction":"01000.....",
            "timestamp":1519896741
         }
      ]
   },
   "unconfirmedTransactions":{  
      "knownBookmark":null,
      "bookmark":"75b136755ffd13ca5889ca739bf37357441052e1",
      "transactions":[  
         {  
            "blockHash":null,
            "confirmations":0,
            "height":null,
            "transactionId":"242e166811902be901e73c362730ec0291780d664378a09dff93e07020302a53",
            "transaction":"01000.....",
            "timestamp":1519896743
         }
      ]
   },
   "replacedTransactions":{  
      "knownBookmark":null,
      "bookmark":"0000000000000000000000000000000000000000",
      "transactions":[  

      ]
   }
}
```

If `knownBookmark` is not null, the response is just a differential on the state the client already know on top of the specified bookmark.

## Get a transaction

HTTP GET v1/cryptos/{cryptoCode}/transactions/{txId}

Optional Parameters:

* `includeTransaction` includes the hex of the transaction, not only information (default: true)

Returns:

```
{  
   "confirmations":1,
   "transaction":"02000000013acdd9a91a171185dccd8081d590cf5b8f3e68a89d5788c8c04672a2fb81f7960000000049483045022100c35863810697bbe560ca8b604f2b6dd6a08a05f773e13fa03883d2b0c2032d4f022036384099bc14feebcb386b905c1b9e57defe27779dc17338d8c14ea45867e48c01feffffff02280210240100000017a9141d7d417303386d2d9837de2b95166dd8554052b98700e1f505000000001976a9144ec8c45a3f1bed21a57b62f8eaec93a33c25b33688ac67000000",
   "height":100, # Might be null
   "timestamp":1519897446
}
```

## Get connection status to the chain

HTTP GET v1/cryptos/{cryptoCode}/status

Returns:
```
{  
   "bitcoinStatus":{  # can be null
      "blocks":103,
      "headers":103,
      "verificationProgress":1.0,
      "isSynched":true,
      "minRelayTxFee": 1,       # in satoshi/byte
      "incrementalRelayFee": 1  # in satoshi/byte
   },
   "repositoryPingTime":0.0032478,
   "isFullySynched":true,
   "chainHeight":103,
   "syncHeight":103,
   "chainType":"Regtest",
   "cryptoCode":"BTC",
   "supportedCryptoCodes":[  
      "BTC"
   ],
   "version":"1.0.1.16"
}

```

## Get a new unused address

HTTP GET v1/cryptos/{cryptoCode}/derivations/{derivationScheme}/addresses/unused

Optional parameters:

* `feature`: Use `Deposit` to get a deposit address (`0/x`), `Change` to get a change address (`1/x`), or `Direct` to get `x` (default: `Deposit`)
* `skip`: How much address to skip, needed if the user want multiple unused addresses (default:0)
* `reserve`: Mark the returned address as used (default: false)

Returns:

```
{  
   "feature":"Direct",
   "derivationStrategy":"tpubD6NzVbkrYhZ4XMQSsvfmMmZkVtLgNjhT4wybUXvMLZhThnXHhLn6YWvDsHMK38FvA8JPpfSjtiBHz4yVh3DHB172VmZ4kCawkana9PirYEi-[legacy]",
   "keyPath":"0",
   "scriptPubKey":"76a914b7acd30d08dc9c9f3081730c69526280b226501e88ac",
   "redeem":null
}
```

Note: `redeem` is returning the segwit redeem if the derivation scheme is a P2SH-P2WSH or P2WSH, or the p2sh redeem if just a p2sh.

## Get Unspent Transaction Outputs (UTXOs)

HTTP GET v1/cryptos/{cryptoCode}/derivations/{derivationScheme}/utxos

Optional parameters:

* `unconfirmedBookmarks` bookmarks known by the client of UTXOs which have not yet been mined.
* `confirmedBookmarks` bookmarks known by the client of UTXOs which have been mined.
* `longPolling` blocks the call until a change happens since the passed bookmarks (default: false)

Result:

```
{  
   "derivationStrategy":"tpubD6NzVbkrYhZ4XByfrbHxiqLm7pj68BTiJdoBjgn3gJGWHsaCQEdNvPJvBuAJhs7J6NCfuZPS1hxeXs6VrzcocKRCWvLZsbbg5pduiPvdfCR-[legacy]",
   "currentHeight":104,
   "unconfirmed":{  
      "knownBookmark":null,
      "bookmark":"6ab442f74b4b7a9409e1164490540a7d329db7a5",
      "utxOs":[  

      ],
      "spentOutpoints":[  
         "87cf1bd27f7f5218c659b450ff5aa4cc4f183446059112921b8e2f35f9a6fc1900000000"
      ],
      "hasChanges":true
   },
   "confirmed":{  
      "knownBookmark":null,
      "bookmark":"728cd680de48fcb06bd7cca0308e85b2fe913c7d",
      "utxOs":[  
         {  
            "feature":"Deposit",
            "outpoint":"87cf1bd27f7f5218c659b450ff5aa4cc4f183446059112921b8e2f35f9a6fc1900000000",
            "scriptPubKey":"76a91456a7162c9c4906a9590c363c3c0e69ab741a87ab88ac",
            "value":100000000,
            "keyPath":"0/0",
            "timestamp":1519898313,
            "confirmations":1
         }
      ],
      "spentOutpoints":[  

      ],
      "hasChanges":true
   },
   "hasChanges":true
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
    "derivationStrategy": "tpubD6NzVbkrYhZ4YL91Ez5fdgaBPQbFhedFdn5gQL4tSCJn1usmHsV1L6VokzLbgcqzh9hiBnfnQANp5BYW15QdFGRKspZVSW1v2QY917RDs1V-[legacy]",
    "transactionData": {
      "confirmations": 0,
      "transaction": "0200000001d47d612cf0dc6883a962877d663613f61d69b62e4e29e711b39c782af642bfc9000000004847304402205ed7968526be5156c366c7e1ba0864ab4a24704e5628a38b778020af1636a5e4022020a89f60a405c7d5b696d2ee915a744cb0ac05a78ddc82ec4acaa7e9d4bd008901feffffff0200e1f505000000001976a914a20eb0aa66b48525b088c4be78765af3dcd4171c88ac280210240100000017a914611e47639114ef9e9400ba2a198bc72617a0438d8738000000",
      "height": null,
      "timestamp": 1519898839
    },
    "outputs": [
      {
        "feature": "Deposit",
        "derivationStrategy": "tpubD6NzVbkrYhZ4YL91Ez5fdgaBPQbFhedFdn5gQL4tSCJn1usmHsV1L6VokzLbgcqzh9hiBnfnQANp5BYW15QdFGRKspZVSW1v2QY917RDs1V-[legacy]",
        "keyPath": "0/1",
        "scriptPubKey": "76a914a20eb0aa66b48525b088c4be78765af3dcd4171c88ac",
        "redeem": null
      }
    ],
    "inputs": [],
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

HTTP POST cryptos/{cryptoCode}/transactions

Body:

Raw bytes of the transaction.

Returns:

```
{  
   "success":false,
   "rpcCode":-25,
   "rpcCodeMessage":"General error during transaction submission",
   "rpcMessage":"Missing inputs"
}
```

## Get fee rate

HTTP GET cryptos/{cryptoCode}/fees/{blockCount}

Get expected fee rate for being confirmed in `blockCount` blocks.

```
{
    "feeRate": 5,
    "blockCount": 1
}
```

The fee rate is in satoshi/byte.