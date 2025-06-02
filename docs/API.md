# API Specification

NBXplorer is a multi crypto currency lightweight block explorer.

NBXplorer does not index the whole blockchain, rather, it listens transactions and blocks from a trusted full node and index only addresses and transactions which belongs to a `DerivationScheme` that you decide to track.

This document describes the concepts, while the [API endpoints are documented here](https://dgarage.github.io/NBXplorer/).

## Table of content

* [Configuration](#configuration)
* [Tracked Sources](#tracked-sources)
  * [Derivation scheme](#derivation-scheme)
    * [Standard Derivation schemes](#standardDerivationScheme)
    * [Policy Derivation scheme](#policyDerivationScheme)
  * [Groups](#groups)
  * [Standalone addresses](#addresses)
* [Authentication](#authentication)

## Configuration

You can check the available settings with `--help`.

NBXplorer can be configured in three way:

* Through command line arguments (eg. `--chains btc`)
* Through environment variables (eg. `NBXPLORER_CHAINS=btc`)
* Through configuration file (eg. `chains=btc`)

If you use configuration file, you can find it on windows in:

```pwsh
C:\Users\<user>\AppData\Roaming\NBXplorer\<network>\settings.config
```

On linux or mac:

```bash
~/.nbxplorer/<network>/settings.config
```

Be careful, if you run NBXplorer with `dotnet run`, you should do it this way, with settings after the `--`:

```bash
dotnet run --no-launch-profile --no-build -c Release -p .\NBXplorer\NBXplorer.csproj -- --chains btc
```

Else, launch profiles, which are settings meant to be used only for debugging time, might be taken into account.

## <a name="tracked-source"></a>Tracked Sources

A tracked source is a generic way to track a set of scripts (addresses) and its UTXOs, transactions, and balances.

And there are three different types: `Derivation Schemes`, `Groups` and `Standalone addresses`.

### <a name="derivationScheme"></a>Derivation scheme

A derivation scheme, also called `derivationStrategy` internally, is a flexible way to define how to generate deterministic addresses for a wallet.

A derivation scheme tracked source's format is `DERIVATIONSCHEME:derivationScheme` (eg. `DERIVATIONSCHEME:xpub1`).
You can create one by calling [Tracking derivation scheme](https://dgarage.github.io/NBXplorer/#tag/Derivations/operation/Track).

There are two types of derivation schemes:
* [Standard](#standardDerivationScheme), for simple and standard use cases (single sig, or multi sig)
* [Policy](#policyDerivationScheme), for supporting more complex spending conditions through [Wallet policies (BIP0388)](https://github.com/bitcoin/bips/blob/master/bip-0388.mediawiki). 

#### <a name="standardDerivationScheme"></a>Standard Derivation scheme

A `Standard Derivation Scheme` defines how wallet addresses and keys are generated from a master public key (xpub) using standard hierarchical deterministic (HD) derivation paths. These schemes typically follow BIP standards (like BIP44, BIP49, BIP84, BIP86) and use predefined formats to produce addresses.

They assume the spending conditions are simple: derived keys authorize spending. There’s no support for complex policy scripts, multisig setups, or conditional logic.

NBXplorer will track any addresses on the `0/x`, `1/x` and `x` path.
Here a documentation of the different supported standard derivation schemes:

| Address type | Format |
| ------------- |-------------|
| P2WPKH | xpub1 |
| P2SH-P2WPKH | xpub1-[p2sh] |
| P2PKH | xpub-[legacy] |
| Multi-sig P2WSH | 2-of-xpub1-xpub2 |
| Multi-sig P2SH-P2WSH | 2-of-xpub1-xpub2-[p2sh] |
| Multi-sig P2SH | 2-of-xpub1-xpub2-[legacy] |
| P2TR | xpub1-[taproot] |

For multisig, the public keys are ordered before generating the address by default for privacy reason, use `-[keeporder]` to disable it.

You can use more than one options at same time, example: `2-of-xpub1-xpub2-[legacy]-[keeporder]`

Most of routes asks for a `cryptoCode`. This identify the crypto currency to request data from. (eg. `BTC`, `LTC`...)

#### <a name="policyDerivationScheme"></a>Policy Derivation scheme

A `Policy Derivation Scheme` extends the standard approach. Instead of generating addresses using simple key derivation paths, it defines flexible spending policies using [Miniscript (BIP0379)](https://github.com/bitcoin/bips/blob/master/bip-0379.md) or [Output Descriptors (BIP0380)](https://github.com/bitcoin/bips/blob/master/bip-0380.mediawiki).

This allows you to specify complex conditions like multi-signature setups, timelocks, or combinations of keys. The scheme then generates addresses based on these policies, offering far more control than standard derivation schemes.

You can use this [website](https://bitcoin.sipa.be/miniscript/) to design your own `Miniscript policy`. For example, a simple policy might look like this:

```
and(pk(A), or(pk(B), or(pk(C), older(1000))))
```

This `miniscript policy` allows spending from the wallet if both conditions are met:

* `A` signs, and
* either `B` signs, or `C` signs after the UTXO is `1000` blocks old.

The website will convert this policy into a `Miniscript output`, which NBXplorer needs to generate addresses. In this case:

```
and_v(or_c(pk(B),or_c(pk(C),v:older(1000))),pk(A))
```

If you want to generate a P2WSH address for this script, wrap it with `wsh()`, like so:

```
wsh(and_v(or_c(pk(B),or_c(pk(C),v:older(1000))),pk(A)))
```

Finally, replace `A`, `B`, and `C` with the appropriate xpubs in the following format: `[00112233/44'/55']xpub.../**`.
Where:

* `00112233` is the `xpub fingerprint` (the first four bytes of the hash160 of the master private key)
* `44'/55'` is an arbitrary `key path` used by the wallet that will sign with this key.
* `xpub` is the [hierarchical public key (BIP032)](https://github.com/bitcoin/bips/blob/master/bip-0032.mediawiki) derived at `44'/55'` from the master private key. (sometimes called the `account key`)
* `**` as defined by [BIP0389](https://github.com/bitcoin/bips/blob/master/bip-0389.mediawiki), specifies that deposit addresses are generated from `0/i`  and change addresses from `1/i` on the xpub. You can change it to another pattern (for example, `<2;3>/*`) if desired.  

```
wsh(and_v(or_c(pk([973a74ba/48'/1'/0']tprv8gZh1wDxtsw28saCHAXAKGRStbAZpWAzuT13AVv5erS3CxHMNLmmJUFfpUKtvfBzQx5qajkzcHkghNp8kuPknDhMTwxPdzPA29NZQcr17ZT/**),or_c(pk([39bad04c/48'/1'/1']tprv8fkJnxvpWbmwWd8Ep6YdXoYrhp1WNDFLvVJKsioFwatwA2kyZtfpFhSqi84QGb83VXqPHWBiH4xV8rcrHz1WeWtk1gWS1MwJsE3dJWfp1Fj/**),v:older(1000))),pk([f19e9416/48'/1'/2']tprv8fuFjBofiYNzDW3GAyPeoMqDf2QdrQZxozJjAT74CxaeYkQv7cKZvrPLSTuB2Z6qX8nxfBbTQaGCzDpPzaH1jyV9RiRj8xVFmo34hFzsKb8/**)))
```

When passing this derivation scheme to the API via a URL path (like when calling [Tracking derivation scheme](https://dgarage.github.io/NBXplorer/#tag/Derivations/operation/Track)), don’t forget to escape it properly.

For example, if you need to track this policy, replace:

| Character | Escape Sequence |
| --------- | --------------- |
| `[`       | `%5B`           |
| `]`       | `%5D`           |
| `'`       | `%27`           |
| `/`       | `%2F`           |
| `,`       | `%2C`           |
| `(`       | `%28`           |
| `)`       | `%29`           |
| `:`       | `%3A`           |
| `*`       | `%2A`           |

The call to [Tracking derivation scheme](https://dgarage.github.io/NBXplorer/#tag/Derivations/operation/Track) would then be:
```
HTTP POST /v1/cryptos/BTC/derivations/wsh%28and_v%28or_c%28pk%28%5B973a74ba%2F48%27%2F1%27%2F0%27%5Dtprv8gZh1wDxtsw28saCHAXAKGRStbAZpWAzuT13AVv5erS3CxHMNLmmJUFfpUKtvfBzQx5qajkzcHkghNp8kuPknDhMTwxPdzPA29NZQcr17ZT%2F%2A%2A%29%2Cor_c%28pk%28%5B39bad04c%2F48%27%2F1%27%2F1%27%5Dtprv8fkJnxvpWbmwWd8Ep6YdXoYrhp1WNDFLvVJKsioFwatwA2kyZtfpFhSqi84QGb83VXqPHWBiH4xV8rcrHz1WeWtk1gWS1MwJsE3dJWfp1Fj%2F%2A%2A%29%2Cv%3Aolder%281000%29%29%29%2Cpk%28%5Bf19e9416%2F48%27%2F1%27%2F2%27%5Dtprv8fuFjBofiYNzDW3GAyPeoMqDf2QdrQZxozJjAT74CxaeYkQv7cKZvrPLSTuB2Z6qX8nxfBbTQaGCzDpPzaH1jyV9RiRj8xVFmo34hFzsKb8%2F%2A%2A%29%29%29%23adgpkx0s
```

### <a name="groups"></a>Groups

A group is a tracked source which serves as a logical method for grouping several tracked sources into a single entity. You can add or remove tracked sources to and from a group.

Additionally, specific addresses can be tracked through the group.

Every address attached by a child tracked source will be added to the group, including all related UTXOs and transactions. 

A group can have any number of children, and a group can also be a child of another group.
Please note that all the children are returned by [Get a group](https://dgarage.github.io/NBXplorer/#tag/Groups/operation/Get). As such, it is advised not to add too many children to avoid slowing down this call.

A group tracked source's format is `GROUP:groupid`.

You can create a new group by calling [Create a group](https://dgarage.github.io/NBXplorer/#tag/Groups/operation/Create).

### <a name="addresses"></a>Standalone addresses

This refers to a tracked source that monitors a single address. It functions similarly to a group, but with only one specific address to it.

The address tracked source's format is `ADDRESS:bc1...`.

You can create one by calling [Tracking an address](https://dgarage.github.io/NBXplorer/#tag/Legacy/operation/TrackSingleAddress).

## Authentication

By default a cookie file is generated when NBXplorer is starting, for windows in:

```pwsh
C:\Users\<user>\AppData\Roaming\NBXplorer\<network>\.cookie
```

On linux or mac:

```bash
~/.nbxplorer/<network>/.cookie
```

The content of this cookie must be used is used as HTTP BASIC authentication to use the API.

This can be disabled with `--noauth` or `NBXPLORER_NOAUTH=1`.

Also, NBXPlorer listen by default on `127.0.0.1`, if you want to access it from another machine, run `--bind "0.0.0.0"`.
