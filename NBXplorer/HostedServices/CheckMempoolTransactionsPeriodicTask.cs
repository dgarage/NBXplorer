using Dapper;
using NBitcoin;
using NBXplorer.Backends;
using NBXplorer.Backends.Postgres;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer.HostedServices
{
	public class CheckMempoolTransactionsPeriodicTask : IPeriodicTask
	{
		public CheckMempoolTransactionsPeriodicTask(
			DbConnectionFactory dbConnectionFactory,
			IIndexers indexers,
			Broadcaster broadcaster)
		{
			DbConnectionFactory = dbConnectionFactory;
			Indexers = indexers;
			Broadcaster = broadcaster;
		}

		public DbConnectionFactory DbConnectionFactory { get; }
		public IIndexers Indexers { get; }
		public Broadcaster Broadcaster { get; }

		public async Task Do(CancellationToken cancellationToken)
		{
			await using var conn = await DbConnectionFactory.CreateConnection();
			List<(NBXplorerNetwork Network, uint256 Id, Transaction Tx)> txs = new List<(NBXplorerNetwork Network, uint256 Id, Transaction Tx)>();
			foreach (var indexer in Indexers.All())
			{
				// We have an index on (code, mempool IS TRUE)
				foreach (var r in await conn.QueryAsync<(string tx_id, byte[] raw)>("SELECT tx_id, raw FROM txs WHERE code=@code AND mempool IS TRUE", new
				{
					code = indexer.Network.CryptoCode
				}))
				{
					if (r.raw is null)
						continue;
					txs.Add((indexer.Network, uint256.Parse(r.tx_id), Transaction.Load(r.raw, indexer.Network.NBitcoinNetwork)));
				}
			}

			foreach (var tx in txs)
			{
				var result = await Broadcaster.Broadcast(tx.Network, tx.Tx, tx.Id);
				if (result.MempoolConflict)
				{
					await conn.ExecuteAsync("UPDATE txs SET replaced_by=@unk_tx_id WHERE code=@code AND tx_id=@tx_id AND mempool IS TRUE AND replaced_by IS NULL", new { code = tx.Network.CryptoCode, tx_id = tx.Id.ToString(), unk_tx_id = NBXplorerNetwork.UnknownTxId.ToString() });
				}
				else if (result.MissingInput || result.UnknownError)
				{
					await conn.ExecuteAsync("UPDATE txs SET mempool='f' WHERE code=@code AND tx_id=@tx_id AND mempool IS TRUE", new { code = tx.Network.CryptoCode, tx_id = tx.Id.ToString() });
				}
			}

		}
	}
}
