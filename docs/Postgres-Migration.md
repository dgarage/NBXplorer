# Migration from DBTrie backend to Postgres backend

> [!WARNING]  
> The last version to support the migration is `2.3.67`. If you are running a version newer than this and need to migrate, please upgrade to `2.3.67` first.

For an extended period, NBXplorer depended on an embedded database dubbed DBTrie. This internal database imposed limitations for various reasons, prompting us to upgrade NBXplorer to employ a Postgres backend rather than DBTrie.

Although we continue to support DBTrie, it is now deemed obsolete. We offer a migration pathway for existing deployments.

| Command line argument  | Environment variable | Description |
|---|---|---|
| --deleteaftermigration | NBXPLORER_DELETEAFTERMIGRATION=1  | Once migration succeed, delete the original DBTrie database (default: false) |
| --postgres  |  NBXPLORER_POSTGRES="..."  | The connection string to postgres  |
| --automigrate  | NBXPLORER_AUTOMIGRATE=1  | If DBTrie database exists, migrate it (default: false)|
| --nomigrateevts  | NBXPLORER_NOMIGRATEEVTS=1  | Do not migrate the events table (default: false) |
| --nomigraterawtxs  | NBXPLORER_NOMIGRATERAWTXS=1  | Do not migrate the raw bytes of transactions (default: false) |

`automigrate`: will seamlessly determine if a DBTrie database necessitates migration, disregarding the flag if migration is unnecessary or already executed.

`nomigrateevts`: may be employed for services reliant on NBXplorer that do not query past events, thereby hastening the migration process. (BTCPay Server, for example, does not utilize past events)

`nomigraterawtxs`: may be utilized if preserving raw transaction bytes is nonessential, consequently expediting migration. Raw transactions are typically crucial for signing with a non-segwit wallet.

The majority of instances will complete migration in under five minutes.

For larger instances, our BTCPay Server's mainnet demo server with 800,000 addresses and 44,000 transactions and the DBTrie database approximating 5GB took roughly 40 minutes.

If you use BTCPay Server, ensure that its environment variable `BTCPAY_EXPLORERPOSTGRES` is assigned the same connection string as NBXplorer.

You can find more information in this [blog post](https://blog.btcpayserver.org/nbxplorer-postgres/).