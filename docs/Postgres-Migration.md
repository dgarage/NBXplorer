# Migration from DBTrie backend to Postgres backend

NBXplorer for a long time relied on an embedded database called DBTrie.
This internal database was limiting for a number of reason, and we decided to update NBXplorer to use a postgres backend instead of DBTrie.

While we still support DBTrie, it is deprecated. We provide a migration path for currently current deployments.

| Command line argument  | Environment variable | Description |
|---|---|---|
| --deleteaftermigration | NBXPLORER_DELETEAFTERMIGRATION=1  | Once migration succeed, delete the original DBTrie database (default: false) |
| --postgres  |  NBXPLORER_POSTGRES="..."  | The connection string to postgres  |
| --automigrate  | NBXPLORER_AUTOMIGRATE=1  | If DBTrie database exists, migrate it (default: false)|
| --nomigrateevts  | NBXPLORER_NOMIGRATEEVTS=1  | Do not migrate the events table (default: false) |
| --nomigraterawtxs  | NBXPLORER_NOMIGRATERAWTXS=1  | Do not migrate the raw bytes of transactions (default: false) |

`automigrate` will automatically detect if a DBTrie database need to be migrated. As such, if the migration already happened or that there is nothing to migrate, the flag will be ignored.

`nomigrateevts` can be used if the services depending on NBXplorer isn't querying past events. It can make the migration a bit faster. (BTCPay Server doesn't use the past events)

`nomigraterawtxs` can be used if you don't need to keep the raw transaction bytes. It can make the migration a bit faster. The raw transactions are typically needed when you need to sign a non-segwit wallet.

The migration will take less than 5 minutes for the majority of small instance.

On bigger instance, our testing on the mainnet demo server of BTCPay Server with 800 000 addresses and 44 000 transactions (around the DBTrie database being around 5GB) took around 40 minutes.