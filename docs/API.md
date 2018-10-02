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

## Track a specific address

After this call, the specified address will be tracked by NBXplorer

HTTP POST v1/cryptos/{cryptoCode}/addresses/{address}

Returns nothing.

## Query transactions associated to a derivationScheme

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
   "height":104,
   "confirmedTransactions":{  
      "knownBookmark":null,
      "bookmark":"3e528a57b54ad288007620b190d8d1282549e151",
      "transactions":[  
         {  
            "blockHash":"587ba520f53684e4b5881557401be31fcfda6ea68eb7599d5cedb6c83c6aa165",
            "confirmations":1,
            "height":104,
            "transactionId":"f4f6c7c8270718885532414f0a287228c8c969633e49091b899637649e5cc8b8",
            "transaction":"0200000001f398dfbceae6e211b54ca55db2ed9136ada5b0a8a1cf4bbf555d364a64856f6f00000000484730440220594000e3526d878edf548e5e7a27b4520c697bdeb9222882ef05ccf31d804b3802204414295584d06bb685a0855a5f620087fb17c74b06b289e5dda8ead4a148b80901feffffff02280210240100000017a914175cf8d6aa049c08637d53e4a073f5e5fda3971c8700e1f505000000001976a914be1cae2bffb9df678d20af6db579467b1e48ac0288ac67000000",
            "outputs":[  
               {  
                  "keyPath":"0/0",
                  "index":1,
                  "value":100000000
               }
            ],
            "inputs":[  

            ],
            "timestamp":1538470162,
            "balanceChange":100000000
         }
      ]
   },
   "unconfirmedTransactions":{  
      "knownBookmark":null,
      "bookmark":"a07691809fc1be6159733cdfcda1e6556a33105d",
      "transactions":[  
         {  
            "blockHash":null,
            "confirmations":0,
            "height":null,
            "transactionId":"94c620d737125232bc94860629ec49bb727a7ba7f2a86623f763b6a2157af08d",
            "transaction":"02000000000101b8c85c9e643796891b09493e6369c9c82872280a4f41325588180727c8c7f6f4000000001716001489d4813bbb1470d9b66d47b39c471d509738fe3ffeffffff0200e1f505000000001976a914be1cae2bffb9df678d20af6db579467b1e48ac0288ac08141a1e0100000017a9145025687e78356848177832dc16662805245f5c228702483045022100f70026ced0a2a9a96ba2d430ec8cf65c7dca76a26c14ea7d08e24aeefafd368502205cae391404189fefb2f0ab60784d9840d3b405d61f4383736228b2daea7ad3a40121031b7c9c74bae9c0ae66aefe6b1cfa484b86b0b1ea36f4f0ad01453799e78daf8268000000",
            "outputs":[  
               {  
                  "keyPath":"0/0",
                  "index":0,
                  "value":100000000
               }
            ],
            "inputs":[  

            ],
            "timestamp":1538470163,
            "balanceChange":100000000
         },
         {  
            "blockHash":null,
            "confirmations":0,
            "height":null,
            "transactionId":"2a70cd89581c62e7863afef1303a5a8d56e20c1d7babef4f02ab929cdb8f9494",
            "transaction":"020000000001018df07a15a2b663f72366a8f2a77b7a72bb49ec29068694bc32521237d720c694010000001716001428f06c87d914987c6199cfd5b51b901d5c8f9744feffffff0200879303000000001976a914be1cae2bffb9df678d20af6db579467b1e48ac0288ace87f861a0100000017a914190b23c71f95ea0dd0c78680253e6a7873a95b0d870247304402202cb1b714c42be83cbb7eeff5039be25ef8ea3d1dcb126d8ecf6555b15e245e760220491a9b9082fcfebd7f96bbd6cda6ff654aee6b19f01ced3c3c312fed4329782d0121036fab1f18eef84bc3c6b1ca981f23ae757f78270d17354a438cda24a440cda57c68000000",
            "outputs":[  
               {  
                  "keyPath":"0/0",
                  "index":0,
                  "value":60000000
               }
            ],
            "inputs":[  

            ],
            "timestamp":1538470164,
            "balanceChange":60000000
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

## Query transactions associated to a specific address

Query all transactions of a tracked address. (Only work if you called the Track operation on this specific address)

HTTP GET v1/cryptos/{cryptoCode}/addresses/{address}/transactions

Optional Parameters:

* `unconfirmedBookmarks` bookmarks known by the client of transactions which have not yet been mined.
* `confirmedBookmarks` bookmarks known by the client of transactions which have been mined.
* `replacedBookmarks` bookmark known by the client of transactions which have been replaced. (RBF)
* `includeTransaction` includes the hex of the transaction, not only information (default: true)
* `longPolling` blocks the call until a change happens since the passed bookmarks (default: false)

Returns:

```
{  
   "height":104,
   "confirmedTransactions":{  
      "knownBookmark":null,
      "bookmark":"6127d5f488be395ed65face936ed020ea43334ea",
      "transactions":[  
         {  
            "blockHash":"3e14b3ae4611eecc7d41f859716282862f54ae1be4b4f728f19a42bc759f6f65",
            "confirmations":1,
            "height":104,
            "transactionId":"8e03011c9e252360b4aef920f6f83e45486c1dd88bce06ebdc7c9726c7beccfb",
            "transaction":"0200000001a85916d153fe68a20aedfa2adc07756524cac11bc863b44bdf97fce04655cbbc000000004948304502210096da773605cf374087aa6d317d500b907f4db6a1507ac14c3f48d2a0fbe8951302204d89f339fd469c31b7723742656cb3d19abfe77f5d096eba32ec00ecbd5fcf3201feffffff02280210240100000017a914af204b5dfad9e60d81c6acf93678b7478720df5e8700e1f505000000001976a91411ad8834bad9d9b00562d1a197e6416a16d8c83888ac67000000",
            "outputs":[  
               {  
                  "index":1,
                  "value":100000000
               }
            ],
            "inputs":[  

            ],
            "timestamp":1538470623,
            "balanceChange":100000000
         }
      ]
   },
   "unconfirmedTransactions":{  
      "knownBookmark":null,
      "bookmark":"918a6c13e02c936d39c7a58ad745fea46e8cadce",
      "transactions":[  
         {  
            "blockHash":null,
            "confirmations":0,
            "height":null,
            "transactionId":"b9b9237761db9244df8a2e1abf42098096455c0f7993112b5ce681d3ca34911b",
            "transaction":"02000000000101fbccbec726977cdceb06ce8bd81d6c48453ef8f620f9aeb46023259e1c01038e000000001716001443c3190482761a0ecd818182165e6648cc6a6a6afeffffff0200e1f505000000001976a91411ad8834bad9d9b00562d1a197e6416a16d8c83888ac08141a1e0100000017a914431278a1c49cd1fee9b9d05129512d153daeaced870247304402207bf7c8beb3aa2f1ebd2903f9eca272b1d7adb1aca22caaaf4475e30d530aa54402206c0ab3af59a4b451adedc981b8d18cac446a699a4c0bc98f873cfec26f97d7eb01210303945540ac4141a21c98fd706d166917ddb0f45ff5384c8be767bffabd8316804e000000",
            "outputs":[  
               {  
                  "index":0,
                  "value":100000000
               }
            ],
            "inputs":[  

            ],
            "timestamp":1538470626,
            "balanceChange":100000000
         },
         {  
            "blockHash":null,
            "confirmations":0,
            "height":null,
            "transactionId":"ffc77d732421d232059c1c93bb3c4fba1158327f9be0ba855a2dee2ab0655fc0",
            "transaction":"020000000001011b9134cad381e65c2b1193790f5c4596800942bf1a2e8adf4492db617723b9b90100000017160014a525769f9c043cac1c201d6b5ce7f66be561bad6feffffff0200879303000000001976a91411ad8834bad9d9b00562d1a197e6416a16d8c83888ace87f861a0100000017a91478587b6e4deaffd40504157bf3dc21a52ca654f58702483045022100cd316c500980d45a9bc05237ef55f8190f9d0bffe5c903fad65f7d0e9286b68302202461bb3358cf4c23a6b2f85b2019fded757025e36e5bfa4d4b98f3c70ea3839701210249e77b07e8f2118f5a2ce683ac6737f55e4a538d0348e72cd4b322f21abd75db68000000",
            "outputs":[  
               {  
                  "index":0,
                  "value":60000000
               }
            ],
            "inputs":[  

            ],
            "timestamp":1538470626,
            "balanceChange":60000000
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

Error codes:

* HTTP 404: Transaction not found

Returns:

```
{  
   "blockId": "00000000000000000013c8848a401c396c1d3c196acb754aa5fe78ea246002d8", # Might be null
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
   "networkType":"Regtest",
   "cryptoCode":"BTC",
   "supportedCryptoCodes":[  
      "BTC"
   ],
   "version":"1.0.1.16"
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

## Get ExtPubKey from scriptPubKey

HTTP GET v1/cryptos/{cryptoCode}/scripts/{script}

Error codes:

* HTTP 404: `cryptoCode-not-supported`

Returns:
```
[
    {  
     "feature":"Direct",
     "derivationStrategy":"tpubD6NzVbkrYhZ4XMQSsvfmMmZkVtLgNjhT4wybUXvMLZhThnXHhLn6YWvDsHMK38FvA8JPpfSjtiBHz4yVh3DHB172VmZ4kCawkana9PirYEi-[legacy]",
     "keyPath":"0",
     "scriptPubKey":"76a914b7acd30d08dc9c9f3081730c69526280b226501e88ac",
     "redeem":null
   }
]
```

## Get Unspent Transaction Outputs (UTXOs)

HTTP GET v1/cryptos/{cryptoCode}/derivations/{derivationScheme}/utxos

Optional parameters:

* `unconfirmedBookmarks` bookmarks known by the client of UTXOs which have not yet been mined.
* `confirmedBookmarks` bookmarks known by the client of UTXOs which have been mined.
* `longPolling` blocks the call until a change happens since the passed bookmarks (default: false)

Error:

* HTTP 404: `cryptoCode-not-supported`

Result:

```
{  
   "trackedSource":"DERIVATIONSCHEME:tpubD6NzVbkrYhZ4WcvX4WvkX2BGK5U6qhdQ6omtBURJCeH23Koog9HQmU7y7mydXVkHodyP27MEByn9bHNKxzx6fcdEEtmf6c62thVTgGvycKz-[legacy]",
   "derivationStrategy":"tpubD6NzVbkrYhZ4WcvX4WvkX2BGK5U6qhdQ6omtBURJCeH23Koog9HQmU7y7mydXVkHodyP27MEByn9bHNKxzx6fcdEEtmf6c62thVTgGvycKz-[legacy]",
   "currentHeight":104,
   "unconfirmed":{  
      "knownBookmark":null,
      "bookmark":"b8eb7b6769a3b92f7dc556ac0d9ae78da9a6b31b",
      "utxOs":[  
         {  
            "feature":"Deposit",
            "outpoint":"1b9134cad381e65c2b1193790f5c4596800942bf1a2e8adf4492db617723b9b900000000",
            "scriptPubKey":"76a91411ad8834bad9d9b00562d1a197e6416a16d8c83888ac",
            "value":100000000,
            "keyPath":"0/0",
            "timestamp":1538470626,
            "confirmations":0
         },
         {  
            "feature":"Deposit",
            "outpoint":"c05f65b02aee2d5a85bae09b7f325811ba4f3cbb931c9c0532d22124737dc7ff00000000",
            "scriptPubKey":"76a91411ad8834bad9d9b00562d1a197e6416a16d8c83888ac",
            "value":60000000,
            "keyPath":"0/0",
            "timestamp":1538470626,
            "confirmations":0
         }
      ],
      "spentOutpoints":[  
         "87cf1bd27f7f5218c659b450ff5aa4cc4f183446059112921b8e2f35f9a6fc1900000000"
      ],
      "hasChanges":true
   },
   "confirmed":{  
      "knownBookmark":null,
      "bookmark":"24be8e9bd9725f82e67258d025af48b499fba676",
      "utxOs":[  
         {  
            "feature":"Deposit",
            "outpoint":"fbccbec726977cdceb06ce8bd81d6c48453ef8f620f9aeb46023259e1c01038e01000000",
            "scriptPubKey":"76a91411ad8834bad9d9b00562d1a197e6416a16d8c83888ac",
            "value":100000000,
            "keyPath":"0/0",
            "timestamp":1538470625,
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

## Get Unspent Transaction Outputs of a specific address

Assuming you use Track on this specific address:

HTTP GET v1/cryptos/{cryptoCode}/addresses/{address}/utxos

Optional parameters:

* `unconfirmedBookmarks` bookmarks known by the client of UTXOs which have not yet been mined.
* `confirmedBookmarks` bookmarks known by the client of UTXOs which have been mined.
* `longPolling` blocks the call until a change happens since the passed bookmarks (default: false)

Error:

* HTTP 404: `cryptoCode-not-supported`

Result:

```
{  
   "trackedSource":"ADDRESS:mh8RfXuWowkiDt2nq8su8KjB9Muuy1DAkx",
   "currentHeight":104,
   "unconfirmed":{  
      "knownBookmark":null,
      "bookmark":"b8eb7b6769a3b92f7dc556ac0d9ae78da9a6b31b",
      "utxOs":[  
         {  
            "outpoint":"1b9134cad381e65c2b1193790f5c4596800942bf1a2e8adf4492db617723b9b900000000",
            "scriptPubKey":"76a91411ad8834bad9d9b00562d1a197e6416a16d8c83888ac",
            "value":100000000,
            "timestamp":1538470626,
            "confirmations":0
         },
         {
            "outpoint":"c05f65b02aee2d5a85bae09b7f325811ba4f3cbb931c9c0532d22124737dc7ff00000000",
            "scriptPubKey":"76a91411ad8834bad9d9b00562d1a197e6416a16d8c83888ac",
            "value":60000000,
            "timestamp":1538470626,
            "confirmations":0
         }
      ],
      "spentOutpoints":[  

      ],
      "hasChanges":true
   },
   "confirmed":{  
      "knownBookmark":null,
      "bookmark":"24be8e9bd9725f82e67258d025af48b499fba676",
      "utxOs":[  
         {  
            "outpoint":"fbccbec726977cdceb06ce8bd81d6c48453ef8f620f9aeb46023259e1c01038e01000000",
            "scriptPubKey":"76a91411ad8834bad9d9b00562d1a197e6416a16d8c83888ac",
            "value":100000000,
            "timestamp":1538470623,
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