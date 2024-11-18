# API Specification

NBXplorer is a multi crypto currency lightweight block explorer.

NBXplorer does not index the whole blockchain, rather, it listens transactions and blocks from a trusted full node and index only addresses and transactions which belongs to a `DerivationScheme` that you decide to track.

This document describes the concepts, while the [API endpoints are documented here](https://nbxplorerdocs.z5.web.core.windows.net/).

## Table of content

* [Configuration](#configuration)
* [Tracked Sources](#tracked-sources)
  * [Derivation schemes](#derivationScheme)
  * [Groups](#groups)
  * [Addresses](#addresses)
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

### <a name="derivationScheme"></a>Derivation scheme

A derivation scheme, also called `derivationStrategy` in the code, is a flexible way to define how to generate deterministic addresses for a wallet.
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
| P2TR | xpub1-[taproot] |

For multisig, the public keys are ordered before generating the address by default for privacy reason, use `-[keeporder]` to disable it.

You can use more than one options at same time, example: `2-of-xpub1-xpub2-[legacy]-[keeporder]`

Most of routes asks for a `cryptoCode`. This identify the crypto currency to request data from. (eg. `BTC`, `LTC`...)

Note: Taproot is incompatible with all other options.

You can create one by calling [Tracking derivation scheme or address](#track).

### <a name="groups"></a>Groups

A group is a tracked source which serves as a logical method for grouping several tracked sources into a single entity. You can add or remove tracked sources to and from a group.

Additionally, specific addresses can be tracked through the group.

Every address attached by a child tracked source will be added to the group, including all related UTXOs and transactions. 

A group can have any number of children, and a group can also be a child of another group.
Please note that all the children are returned by [Get a group](#get-group). As such, it is advised not to add too many children to avoid slowing down this call.

A group tracked source's format is `GROUP:groupid`.

You can create a new group by calling [Create a group](#create-group).

### <a name="addresses"></a>Addresses

This refers to a tracked source that monitors a single address. It functions similarly to a group, but with only one specific address to it.

The address tracked source's format is `ADDRESS:bc1...`.

You can create one by calling [Tracking derivation scheme or address](#track).

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

This can be disabled with `--noauth`.

Also, NBXPlorer listen by default on `127.0.0.1`, if you want to access it from another machine, run `--bind "0.0.0.0"`.

