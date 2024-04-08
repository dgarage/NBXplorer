﻿using NBitcoin;
using Dapper;
using NBXplorer.Configuration;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using NBitcoin.RPC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NBitcoin.Altcoins.Elements;
using NBXplorer.Altcoins.Liquid;
using NBXplorer.Client;
using NBitcoin.Scripting;
using System.Text.RegularExpressions;
using Npgsql;
using static NBXplorer.Backend.DbConnectionHelper;


namespace NBXplorer.Backend
{
	public class Repository
	{
		private DbConnectionFactory connectionFactory;
		private readonly RPCClient rpc;

		public DbConnectionFactory ConnectionFactory => connectionFactory;
		public Repository(DbConnectionFactory connectionFactory, NBXplorerNetwork network, KeyPathTemplates keyPathTemplates, RPCClient rpc, ExplorerConfiguration conf)
		{
			this.connectionFactory = connectionFactory;
			Network = network;
			KeyPathTemplates = keyPathTemplates;
			this.rpc = rpc;
			Serializer = new Serializer(network);
			InstanceSuffix = string.IsNullOrEmpty(conf.InstanceName) ? string.Empty : $"-{conf.InstanceName}";
		}

		public int BatchSize { get; set; }

		public int MaxPoolSize { get; set; }
		public int MinPoolSize { get; set; }
		public Money MinUtxoValue { get; set; }

		public NBXplorerNetwork Network { get; set; }
		public KeyPathTemplates KeyPathTemplates { get; }
		public Serializer Serializer { get; set; }
		public string InstanceSuffix { get; }

		public async Task CancelReservation(DerivationStrategyBase strategy, KeyPath[] keyPaths)
		{
			await using var conn = await GetConnection();
			var parameters = keyPaths
				.Select(o =>
				{
					var template = KeyPathTemplates.GetKeyPathTemplate(o);
					var descriptor = GetDescriptorKey(strategy, KeyPathTemplates.GetDerivationFeature(o));
					return new
					{
						descriptor.code,
						descriptor.descriptor,
						idx = (int)template.GetIndex(o)
					};
				})
				.ToList();
			// We can only set to used='t' descriptors whose scripts haven't been used on chain
			await conn.Connection.ExecuteAsync(
					"UPDATE descriptors_scripts ds SET used='f' " +
					"FROM scripts s " +
					"WHERE " +
					"ds.code=@code AND ds.descriptor=@descriptor AND ds.idx=@idx AND " +
					"s.code=ds.code AND s.script=ds.script AND s.used IS FALSE", parameters);
		}

		public TrackedTransaction CreateTrackedTransaction(TrackedSource trackedSource, TrackedTransactionKey transactionKey, IEnumerable<Coin> coins, Dictionary<Script, KeyPath> knownScriptMapping)
		{
			return new TrackedTransaction(transactionKey, trackedSource, coins, knownScriptMapping);
		}

		public TrackedTransaction CreateTrackedTransaction(TrackedSource trackedSource, TrackedTransactionKey transactionKey, Transaction tx, Dictionary<Script, KeyPath> knownScriptMapping)
		{
			return new TrackedTransaction(transactionKey, trackedSource, tx, knownScriptMapping);
		}

		public record DescriptorKey(string code, string descriptor);
		internal DescriptorKey GetDescriptorKey(DerivationStrategyBase strategy, DerivationFeature derivationFeature)
		{
			var hash = DBUtils.nbxv1_get_descriptor_id(Network.CryptoCode, strategy.ToString(), derivationFeature.ToString());
			return new DescriptorKey(Network.CryptoCode, hash);
		}
		// metadata isn't really part of the key, but it's handy to have it here when we do INSERT INTO wallets.
		public record WalletKey(string wid, string metadata);
		internal static WalletKey GetWalletKey(DerivationStrategyBase strategy, NBXplorerNetwork network)
		{
			var hash = DBUtils.nbxv1_get_wallet_id(network.CryptoCode, strategy.ToString());
			JObject m = new JObject();
			m.Add(new JProperty("type", new JValue("NBXv1-Derivation")));
			m.Add(new JProperty("code", new JValue(network.CryptoCode)));
			m.Add(new JProperty("derivation", new JValue(strategy.ToString())));
			return new WalletKey(hash, m.ToString(Formatting.None));
		}
		internal static WalletKey GetWalletKey(GroupTrackedSource groupTrackedSource)
		{
			var m = new JObject { new JProperty("type", new JValue("NBXv1-Group")) };
			var res = new WalletKey($"G:" + groupTrackedSource.GroupId, m.ToString(Formatting.None));
			return res;
		}
		internal static WalletKey GetWalletKey(IDestination destination, NBXplorerNetwork network)
		{
			var address = destination.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork);
			var hash = DBUtils.nbxv1_get_wallet_id(network.CryptoCode, address.ToString());
			JObject m = new JObject();
			m.Add(new JProperty("type", new JValue("NBXv1-Address")));
			m.Add(new JProperty("code", new JValue(network.CryptoCode)));
			m.Add(new JProperty("address", new JValue(address.ToString())));
			return new WalletKey(hash, m.ToString(Formatting.None));
		}

		internal WalletKey GetWalletKey(TrackedSource source) => GetWalletKey(source, Network);
		internal static WalletKey GetWalletKey(TrackedSource source, NBXplorerNetwork network)
		{
			if (source is null)
				throw new ArgumentNullException(nameof(source));
			return source switch
			{
				DerivationSchemeTrackedSource derivation => GetWalletKey(derivation.DerivationStrategy, network),
				AddressTrackedSource addr => GetWalletKey(addr.Address, network),
				GroupTrackedSource group => GetWalletKey(group),
				_ => throw new NotSupportedException(source.GetType().ToString())
			};
		}

		internal TrackedSource TryGetTrackedSource(WalletKey walletKey)
		{
			return TryGetTrackedSource(walletKey, Network);
		}
		internal static TrackedSource TryGetTrackedSource(WalletKey walletKey, NBXplorerNetwork network)
		{
			var metadata = JObject.Parse(walletKey.metadata);
			if (metadata.TryGetValue("type", StringComparison.OrdinalIgnoreCase, out JToken typeJToken) &&
				typeJToken.Value<string>() is { } type)
			{
				if ((metadata.TryGetValue("code", StringComparison.OrdinalIgnoreCase, out JToken codeJToken) &&
					 codeJToken.Value<string>() is { } code) && !code.Equals(network.CryptoCode,
						StringComparison.InvariantCultureIgnoreCase))
				{
					return null;
				}

				switch (type)
				{
					case "NBXv1-Derivation":
						var derivation = metadata["derivation"].Value<string>();
						return new DerivationSchemeTrackedSource(network.DerivationStrategyFactory.Parse(derivation));
					case "NBXv1-Address":
						var address = metadata["address"].Value<string>();
						return new AddressTrackedSource(BitcoinAddress.Create(address, network.NBitcoinNetwork));
					case "NBXv1-Group":
						return new GroupTrackedSource(walletKey.wid[2..]); // Skip "G:"
				}
			}

			return null;
		}

		internal record ScriptInsert(string code, string wallet_id, string script, string addr, bool used);
		internal record DescriptorScriptInsert(string descriptor, int idx, string script, string metadata, string addr, bool used);
		public async Task<int> GenerateAddresses(DerivationStrategyBase strategy, DerivationFeature derivationFeature, GenerateAddressQuery query = null)
		{
			await using var connection = await connectionFactory.CreateConnection();
			return await GenerateAddressesCore(connection, strategy, derivationFeature, query);
		}

		record GapNextIndex(long gap, long next_idx);
		internal async Task<int> GenerateAddressesCore(DbConnection connection, DerivationStrategyBase strategy, DerivationFeature derivationFeature, GenerateAddressQuery query)
		{
			var descriptorKey = GetDescriptorKey(strategy, derivationFeature);
			var walletKey = GetWalletKey(strategy, Network);
			var gapNextIndex = await GetGapAndNextIdx(connection, descriptorKey);
			long toGenerate = ToGenerateCount(query, gapNextIndex?.gap);
			if (gapNextIndex is not null && toGenerate == 0)
				return 0;
			var keyTemplate = KeyPathTemplates.GetKeyPathTemplate(derivationFeature);
			if (gapNextIndex is null)
			{
				// Let's generate the wallet
				await connection.ExecuteAsync("INSERT INTO wallets VALUES (@wid, @metadata::JSONB) ON CONFLICT DO NOTHING", walletKey);
				await connection.ExecuteAsync(
					"INSERT INTO descriptors VALUES (@code, @descriptor, @metadata::JSONB) ON CONFLICT DO NOTHING;" +
					"INSERT INTO wallets_descriptors (code, descriptor, wallet_id) VALUES (@code, @descriptor, @wallet_id) ON CONFLICT DO NOTHING;", new
					{
						descriptorKey.code,
						descriptorKey.descriptor,
						metadata = Serializer.ToString(new LegacyDescriptorMetadata()
						{
							Derivation = strategy,
							Feature = derivationFeature,
							KeyPathTemplate = keyTemplate,
							Type = LegacyDescriptorMetadata.TypeName
						}),
						wallet_id = walletKey.wid
					});
				gapNextIndex = await GetGapAndNextIdx(connection, descriptorKey);
				toGenerate = ToGenerateCount(query, gapNextIndex?.gap);
			}
			if (gapNextIndex is null)
				return 0;
			long totalGenerated = 0;

			do
			{
				var nextIndex = gapNextIndex.next_idx;
				var line = strategy.GetLineFor(keyTemplate);
				var scriptpubkeys = new Script[toGenerate];
				var linesScriptpubkeys = new DescriptorScriptInsert[toGenerate];
				Parallel.For(nextIndex, nextIndex + toGenerate, i =>
				{
					var derivation = line.Derive((uint)i);
					scriptpubkeys[i - nextIndex] = derivation.ScriptPubKey;

					var addr = derivation.ScriptPubKey.GetDestinationAddress(Network.NBitcoinNetwork);
					JObject metadata = GetDescriptorScriptMetadata(strategy, line.KeyPathTemplate.GetKeyPath((int)i, false), derivation, addr);
					linesScriptpubkeys[i - nextIndex] = new DescriptorScriptInsert(
						descriptorKey.descriptor,
						(int)i,
						derivation.ScriptPubKey.ToHex(),
						metadata?.ToString(Formatting.None),
						addr.ToString(),
						false);
				});

				// We do not dispose on purpose.
				await ImportDescriptorToRPCIfNeeded(connection, walletKey, nextIndex, toGenerate, keyTemplate);
				foreach (var batch in linesScriptpubkeys.Chunk(10_000))
				{
					await InsertDescriptorsScripts(connection, batch);
				}
				totalGenerated += toGenerate;
				// More than one loop should never happen, but it may happen if after generating the addresses, the new gap may
				// still be not enough, because the scripts we generated may have been tracked before
				// and may have received UTXOs
				gapNextIndex = await GetGapAndNextIdx(connection, descriptorKey);
				toGenerate = ToGenerateCount(null, gapNextIndex?.gap);
				if (query?.MaxAddresses is int m && totalGenerated >= m)
					toGenerate = 0;
			} while (toGenerate != 0);
			return (int)totalGenerated;
		}

		private async Task ImportDescriptorToRPCIfNeeded(DbConnection connection, WalletKey walletKey, long fromIndex, long toGenerate, KeyPathTemplate keyTemplate)
		{
			var helper = new DbConnectionHelper(Network, connection, KeyPathTemplates);
			var importAddressToRPC = ImportRPCMode.Parse(await helper.GetMetadata<string>(walletKey.wid, WellknownMetadataKeys.ImportAddressToRPC));
			if (importAddressToRPC == ImportRPCMode.Descriptors || importAddressToRPC == ImportRPCMode.DescriptorsReadOnly)
			{
				var descriptor = await helper.GetMetadata<string>(walletKey.wid, WellknownMetadataKeys.AccountDescriptor);
				// descriptor: tr([abcdefaa/49'/0'/0']xpub)#checksum
				if (descriptor != null)
				{
					descriptor = descriptor.Substring(0, descriptor.LastIndexOf('#'));
					// descriptor: tr([abcdefaa/49'/0'/0']xpub)
					if (importAddressToRPC == ImportRPCMode.Descriptors)
					{
						var accountHDKey = await helper.GetMetadata<string>(walletKey.wid, WellknownMetadataKeys.AccountHDKey);
						if (accountHDKey != null)
						{
							descriptor = ReplaceBase58(descriptor, accountHDKey);
							// descriptor: tr([abcdefaa/49'/0'/0']xpriv)
						}
					}
					descriptor = ReplaceBase58(descriptor, $"$0/{keyTemplate}");
					// descriptor: tr([abcdefaa/49'/0'/0']xpriv/0/*)
					await rpc.ImportDescriptors(OutputDescriptor.AddChecksum(descriptor), fromIndex, fromIndex + toGenerate - 1, default);
				}
			}
		}

		private static string ReplaceBase58(string descriptor, string newBase58)
		{
			return Regex.Replace(descriptor, "[123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz]{30,}", newBase58);
		}

		private long ToGenerateCount(GenerateAddressQuery query, long? gap)
		{
			var toGenerate = (gap is null || gap >= MinPoolSize) ? 0 : Math.Max(0, MaxPoolSize - gap.Value);
			if (query?.MaxAddresses is int max)
				toGenerate = Math.Min(max, toGenerate);
			if (query?.MinAddresses is int min)
				toGenerate = Math.Max(min, toGenerate);
			return toGenerate;
		}

		private static async Task<GapNextIndex> GetGapAndNextIdx(DbConnection connection, DescriptorKey descriptorKey)
		{
			return await connection.QueryFirstOrDefaultAsync<GapNextIndex>(
							"SELECT gap, next_idx FROM descriptors " +
							"WHERE code=@code AND descriptor=@descriptor", descriptorKey);
		}

		internal const string InsertScriptsScript = "INSERT INTO scripts (code, script, addr, used) SELECT @code code, script, addr, used FROM unnest(@records) ON CONFLICT DO NOTHING;";
		private async Task InsertDescriptorsScripts(DbConnection connection, IList<DescriptorScriptInsert> batch)
		{
			await connection.ExecuteAsync(
								InsertScriptsScript +
								"INSERT INTO descriptors_scripts (code, descriptor, idx, script, metadata, used) SELECT @code code, descriptor, idx, script, metadata, used FROM unnest(@records) ON CONFLICT DO NOTHING;"
								, new
								{
									code = Network.CryptoCode,
									records = batch
								});
		}

		private JObject GetDescriptorScriptMetadata(DerivationStrategyBase strategy, KeyPath keyPath, Derivation derivation, BitcoinAddress addr)
		{
			JObject metadata = null;
			if (derivation.Redeem?.ToHex() is string r)
			{
				metadata ??= new JObject();
				metadata.Add(new JProperty("redeem", r));
			}

			if (Network.IsElement)
			{
				if (!strategy.Unblinded())
				{
					var blindingKey = NBXplorerNetworkProvider.LiquidNBXplorerNetwork.GenerateBlindingKey(strategy, keyPath, addr.ScriptPubKey, Network.NBitcoinNetwork);
					var blindedAddress = new BitcoinBlindedAddress(blindingKey.PubKey, addr);
					metadata ??= new JObject();
					metadata.Add(new JProperty("blindedAddress", blindedAddress.ToString()));
					metadata.Add(new JProperty("blindingPrivateKey", blindingKey.ToHex()));
				}
			}

			return metadata;
		}

		public Task<int> GenerateAddresses(DerivationStrategyBase strategy, DerivationFeature derivationFeature, int maxAddresses)
		{
			return GenerateAddresses(strategy, derivationFeature, new GenerateAddressQuery(null, maxAddresses));
		}

		public async Task<IList<NewEventBase>> GetEvents(long lastEventId, int? limit = null)
		{
			await using var connection = await connectionFactory.CreateConnection();
			var limitClause = string.Empty;
			if (limit is int i && i > 0)
				limitClause = $" LIMIT {i}";
			var res = (await connection.QueryAsync<(long id, string data)>($"SELECT id, data FROM nbxv1_evts WHERE code=@code AND id > @lastEventId ORDER BY id{limitClause}", new { code = Network.CryptoCode, lastEventId }))
				.Select(ToTypedEvent)
				.ToArray();
			return res;
		}

		private NewEventBase ToTypedEvent((long id, string data) r)
		{
			var ev = NewEventBase.ParseEvent(r.data, Serializer.Settings);
			ev.EventId = r.id;
			return ev;
		}

		public async Task<IList<NewEventBase>> GetLatestEvents(int limit = 10)
		{
			await using var connection = await connectionFactory.CreateConnection();
			var limitClause = string.Empty;
			if (limit is int i && i > 0)
				limitClause = $" LIMIT {i}";
			var res = (await connection.QueryAsync<(long id, string data)>($"SELECT id, data FROM nbxv1_evts WHERE code=@code ORDER BY id DESC{limitClause}", new { code = Network.CryptoCode }))
				.Select(ToTypedEvent)
				.ToArray();
			Array.Reverse(res);
			return res;
		}

		public async Task<MultiValueDictionary<Script, KeyPathInformation>> GetKeyInformations(IList<Script> scripts)
		{
			await using var connection = await connectionFactory.CreateConnection();
			return await GetKeyInformations(connection, scripts);
		}
		async Task<MultiValueDictionary<Script, KeyPathInformation>> GetKeyInformations(DbConnection connection, IList<Script> scripts)
		{
			MultiValueDictionary<Script, KeyPathInformation> result = new MultiValueDictionary<Script, KeyPathInformation>();
			foreach (var s in scripts)
				result.AddRange(s, Array.Empty<KeyPathInformation>());
			var command = connection.CreateCommand();
			if (scripts.Count == 0)
				return result;
			string additionalColumn = Network.IsElement ? ", ts.blinded_addr" : "";
			var rows = await connection.QueryAsync($@"
			    SELECT ts.code, ts.script, ts.addr, ts.derivation, ts.keypath, ts.redeem{additionalColumn},
				       ts.wallet_id,
				       w.metadata AS wallet_metadata
				FROM unnest(@records) AS r (script),
				LATERAL (
				    SELECT code, script, wallet_id, addr, descriptor_metadata->>'derivation' derivation, 
				           keypath, descriptors_scripts_metadata->>'redeem' redeem, 
				           descriptors_scripts_metadata->>'blindedAddress' blinded_addr, 
				           descriptors_scripts_metadata->>'blindingKey' blindingKey, 
				           descriptor_metadata->>'descriptor' descriptor
				    FROM nbxv1_keypath_info ki 
				    WHERE ki.code=@code AND ki.script=r.script
				) ts
				JOIN wallets w USING(wallet_id)",
	new { code = Network.CryptoCode, records = scripts.Select(s => s.ToHex()).ToArray() });
			foreach (var r in rows)
			{
				// This might be the case for a derivation added by a different indexer
				if (r.derivation is not null && r.keypath is null)
					continue;
				BitcoinAddress addr = GetAddress(r);
				bool isExplicit = r.derivation is null;
				bool isDescriptor = !isExplicit;
				var script = Script.FromHex(r.script);
				DerivationStrategyBase derivationStrategy = r.derivation is not null ? Network.DerivationStrategyFactory.Parse(r.derivation) : null;
				var keypath = r.keypath is not null ? KeyPath.Parse(r.keypath) : null;
				var redeem = (string)r.redeem;
				string walletMetadata = r.wallet_metadata;
				string wid = r.wallet_id;
				if (wid is null || walletMetadata is null)
					continue;
				var walletKey = new WalletKey(wid, walletMetadata);
				var trackedSource = TryGetTrackedSource(walletKey);
				if (trackedSource is null)
					continue;
				var ki = Network.IsElement && r.blindingKey is not null
					? new LiquidKeyPathInformation()
					{
						BlindingKey = Key.Parse(r.blindingKey, Network.NBitcoinNetwork)
					}
					: new KeyPathInformation();
				ki.Address = addr;
				ki.DerivationStrategy = r.derivation is not null ? derivationStrategy : null;
				ki.KeyPath = keypath;
				ki.ScriptPubKey = script;
				ki.TrackedSource = trackedSource;
				ki.Feature = keypath is null ? DerivationFeature.Deposit : KeyPathTemplates.GetDerivationFeature(keypath);
				ki.Redeem = redeem is null ? null : Script.FromHex(redeem);
				result.Add(script, ki);
			}
			return result;
		}

		public class LiquidKeyPathInformation : KeyPathInformation
		{
			public Key BlindingKey { get; set; }
		}

		private BitcoinAddress GetAddress(dynamic r)
		{
			if (Network.IsElement && r.blinded_addr is not null)
			{
				// The addr field is unblinded, so if it should be blinded, take that instead
				return BitcoinAddress.Create(r.blinded_addr, Network.NBitcoinNetwork);
			}
			return BitcoinAddress.Create(r.addr, Network.NBitcoinNetwork);
		}

		FixedSizeCache<uint256, uint256> noMatchCache = new FixedSizeCache<uint256, uint256>(5000, k => k);

		record ScriptPubKeyQuery(string code, string id);

		public async Task<TrackedTransaction[]> GetMatches(Block block, SlimChainedBlock slimBlock, DateTimeOffset now, bool useCache)
		{
			var matches = await GetMatches(block.Transactions, slimBlock, now, useCache);
			if (matches.Length > 0)
			{
				var blockIndexes = block.Transactions.Select((tx, i) => (tx, i))
								  .ToDictionary(o => o.tx.GetHash(), o => o.i);
				foreach (var match in matches)
					match.BlockIndex = blockIndexes[match.TransactionHash];
			}
			return matches;
		}

		public async Task<TrackedTransaction[]> GetMatches(IList<Transaction> txs, SlimChainedBlock slimBlock, DateTimeOffset now, bool useCache)
		{
			await using var conn = await GetConnection();
			return await GetMatches(conn, txs, slimBlock, now, useCache, false);
		}
		public Task<TrackedTransaction[]> GetMatchesAndSave(DbConnectionHelper conn, IList<Transaction> txs, SlimChainedBlock slimBlock, DateTimeOffset now, bool useCache)
		{
			return GetMatches(conn, txs, slimBlock, now, useCache, true);
		}

		class ElementMatchContext
		{
			public List<UpdateMatchesOuts> matchedOutsUpdate = new List<UpdateMatchesOuts>();
			public HashSet<uint256> txsToUnblind = new HashSet<uint256>();
			public MultiValueDictionary<TrackedTransaction, KeyPathInformation> keyPathInformationsByTrackedTransaction = new MultiValueDictionary<TrackedTransaction, KeyPathInformation>();
			internal void MatchedOut(dynamic r)
			{
				if (r.asset_id == NBXplorerNetwork.UnknownAssetId)
					txsToUnblind.Add(uint256.Parse(r.tx_id));
			}

			public void TrackedTransaction(TrackedTransaction match, KeyPathInformation keyInfo)
			{
				keyPathInformationsByTrackedTransaction.Add(match, keyInfo);
			}

			public async Task Unblind(RPCClient rpc, TrackedTransaction m)
			{
				if (txsToUnblind.Contains(m.TransactionHash))
				{
					var unblinded = await rpc.UnblindTransaction(m, keyPathInformationsByTrackedTransaction[m]);
					foreach (var coin in m.ReceivedCoins.ToList())
					{
						if (coin is Coin)
						{
							m.ReceivedCoins.Remove(coin);
							var elementTxOut = (ElementsTxOut)(unblinded ?? m.Transaction).Outputs[coin.Outpoint.N];
							if (elementTxOut.Asset?.AssetId is not null && elementTxOut.Value is not null)
							{
								var unblindedCoin = new AssetCoin(new AssetMoney(elementTxOut.Asset.AssetId, elementTxOut.Value.Satoshi), coin);
								m.ReceivedCoins.Add(unblindedCoin);
								matchedOutsUpdate.Add(new UpdateMatchesOuts(
									m.TransactionHash.ToString(),
									coin.Outpoint.N,
									unblindedCoin.Money.AssetId.ToString(),
									unblindedCoin.Money.Quantity));
							}
							else
							{
								var unblindedCoin = new AssetCoin(NBXplorerNetwork.UnknownAssetMoney, coin);
								m.ReceivedCoins.Add(unblindedCoin);
							}
						}
					}
				}
			}

			internal async Task UpdateMatchedOuts(DbConnection connection)
			{
				if (matchedOutsUpdate.Count != 0)
				{
					await connection.ExecuteAsync(
						"UPDATE matched_outs SET asset_id=@asset_id, value=@value " +
						"WHERE tx_id=@tx_id AND idx=@idx", matchedOutsUpdate);
				}
			}
		}
		record UpdateMatchesOuts(string tx_id, long idx, string asset_id, long value);
		async Task<TrackedTransaction[]> GetMatches(DbConnectionHelper connection, IList<Transaction> txs, SlimChainedBlock slimBlock, DateTimeOffset now, bool useCache, bool immediateSave)
		{
			var blockIndexes = slimBlock is null ? null : new Dictionary<uint256, int>(txs.Count);
			SaveTransactionRecord CreateTransactionRecord(Transaction tx) => SaveTransactionRecord.Create(
				slimBlock,
				tx,
				blockIndexes?.TryGetValue(tx.GetHash(), out var bi) is true ? bi : null,
				now);
			int i = 0;
			var unconfTxs = slimBlock is null ? null : await connection.GetUnconfirmedTxs();
			List<SaveTransactionRecord> txRecords = new();
			foreach (var tx in txs)
			{
				tx.PrecomputeHash(false, true);
				blockIndexes?.Add(tx.GetHash(), i);
				i++;
				if (unconfTxs?.Contains(tx.GetHash()) is true)
					// If a block has been found, and we have some unconf transactions
					// then we want to add an entry in blks_txs, even if the unconf tx isn't matching
					// any wallet. So we add record.
					txRecords.Add(CreateTransactionRecord(tx));
			}

			var outputCount = txs.Select(tx => tx.Outputs.Count).Sum();
			var inputCount = txs.Select(tx => tx.Inputs.Count).Sum();
			var outpointCount = inputCount + outputCount;

			var scripts = new List<Script>(outpointCount);
			var transactionsPerScript = new MultiValueDictionary<Script, NBitcoin.Transaction>(outpointCount);

			var matches = new Dictionary<string, TrackedTransaction>();
			var noMatchTransactions = slimBlock?.Hash is null ? new HashSet<uint256>(txs.Count) : null;
			var transactions = new Dictionary<uint256, NBitcoin.Transaction>(txs.Count);
			var outpoints = new List<OutPoint>(inputCount);
			var elementContext = Network.IsElement ? new ElementMatchContext() : null;

			foreach (var tx in txs)
			{
				if (slimBlock?.Hash != null && useCache && noMatchCache.Contains(tx.GetHash()))
				{
					continue;
				}
				if (!transactions.TryAdd(tx.GetHash(), tx))
					continue;
				noMatchTransactions?.Add(tx.GetHash());
			}
			if (!await connection.FetchMatches(transactions.Values, slimBlock, MinUtxoValue))
				goto end;

			using (var result = await connection.Connection.QueryMultipleAsync(
				"SELECT * FROM matched_outs;" +
				"SELECT * FROM matched_ins;" +
				// the query matched_conflicts need to fetch wallet_id as we don't want replacing include transaction that aren't owned by the wallet
				// note there might be some dups as one matched_conflicts can match more than one tracked_txs line.
				// but that's ok.
				"SELECT tt.wallet_id, mc.* FROM matched_conflicts mc JOIN nbxv1_tracked_txs tt ON tt.code=mc.code AND tt.tx_id=mc.replaced_tx_id"))
			{
				var matchedOuts = await result.ReadAsync();
				var matchedIns = await result.ReadAsync();
				var matchedConflicts = await result.ReadAsync();
				foreach (var r in matchedConflicts)
				{
					var txId = uint256.Parse(r.replacing_tx_id);
					var tx = transactions[txId];
					txRecords.Add(CreateTransactionRecord(tx));
				}
				foreach (var r in matchedOuts)
				{
					var s = Script.FromHex(r.script);
					scripts.Add(s);
					transactionsPerScript.Add(s, transactions[uint256.Parse(r.tx_id)]);
					elementContext?.MatchedOut(r);
				}
				foreach (var r in matchedIns)
				{
					var s = Script.FromHex(r.script);
					scripts.Add(s);
					transactionsPerScript.Add(s, transactions[uint256.Parse(r.tx_id)]);
				}

				if (scripts.Count > 0)
				{
					var keyInformations = await GetKeyInformations(connection.Connection, scripts);
					foreach (var keyInfoByScripts in keyInformations)
					{
						foreach (var tx in transactionsPerScript[keyInfoByScripts.Key])
						{
							if (keyInfoByScripts.Value.Count != 0)
								noMatchTransactions?.Remove(tx.GetHash());
							foreach (var keyInfo in keyInfoByScripts.Value)
							{
								var matchesGroupingKey = $"{keyInfo.DerivationStrategy?.ToString() ?? keyInfo.ScriptPubKey.ToHex()}-[{tx.GetHash()}]";
								if (!matches.TryGetValue(matchesGroupingKey, out TrackedTransaction match))
								{
									match = CreateTrackedTransaction(keyInfo.TrackedSource,
										new TrackedTransactionKey(tx.GetHash(), slimBlock?.Hash, false),
										tx,
										new Dictionary<Script, KeyPath>());

									var record = CreateTransactionRecord(tx);
									match.BlockHeight = record.BlockHeight;
									match.FirstSeen = now;
									match.BlockIndex = record.BlockIndex;
									match.Inserted = now;
									match.Immature = record.Immature;
									match.Replacing = new HashSet<uint256>();
									foreach (var r in matchedConflicts)
									{
										var wallet_id = GetWalletKey(match.TrackedSource).wid;
										uint256 replacingTxId = uint256.Parse(r.replacing_tx_id);
										if (replacingTxId == match.TransactionHash && r.wallet_id == wallet_id)
										{
											uint256 replacedTxId = uint256.Parse(r.replaced_tx_id);
											match.Replacing.Add(replacedTxId);
										}
									}
									matches.Add(matchesGroupingKey, match);
									txRecords.Add(record);
								}
								match.AddKnownKeyPathInformation(keyInfo);
								elementContext?.TrackedTransaction(match, keyInfo);
							}
						}
					}
					foreach (var m in matches.Values)
					{
						m.OwnedScriptsUpdated();
						if (elementContext is not null)
							await elementContext.Unblind(rpc, m);
					}
				}
			}
			end:
			if (immediateSave && txRecords.Count != 0)
			{
				await connection.SaveTransactions(txRecords);
				if (elementContext is not null)
					await elementContext.UpdateMatchedOuts(connection.Connection);
				retry:
				try
				{
					await connection.Connection.ExecuteAsync("CALL save_matches(@code)", new { code = Network.CryptoCode });
				}
				// Broadcast call this method, and it may be called at same time as the indexer, resulting in a Deadlock
				// I believe we can safely retry in that case.
				catch (NpgsqlException ex) when (ex.SqlState == PostgresErrorCodes.DeadlockDetected)
				{
					goto retry;
				}
			}
			if (noMatchTransactions != null)
			{
				foreach (var txId in noMatchTransactions)
				{
					noMatchCache.Add(txId);
				}
			}
			return matches.Values.Count == 0 ? Array.Empty<TrackedTransaction>() : matches.Values.ToArray();
		}

		public Task<TrackedTransaction[]> GetMatches(Transaction tx, SlimChainedBlock slimBlock, DateTimeOffset now, bool useCache)
		{
			return GetMatches(new[] { tx }, slimBlock, now, useCache);
		}

		record SavedTransactionRow(byte[] raw, string blk_id, long? blk_height, string replaced_by, DateTime seen_at);
		public async Task<SavedTransaction[]> GetSavedTransactions(uint256 txid)
		{
			await using var connection = await connectionFactory.CreateConnectionHelper(Network);
			var tx = await connection.Connection.QueryFirstOrDefaultAsync<SavedTransactionRow>("SELECT raw, blk_id, blk_height, replaced_by, seen_at FROM txs WHERE code=@code AND tx_id=@tx_id", new { code = Network.CryptoCode, tx_id = txid.ToString() });
			if (tx?.raw is null)
				return Array.Empty<SavedTransaction>();
			return new[] { new SavedTransaction()
			{
				BlockHash = tx.blk_id is null ? null : uint256.Parse(tx.blk_id),
				BlockHeight = tx.blk_height,
				Timestamp = new DateTimeOffset(tx.seen_at),
				Transaction = Transaction.Load(tx.raw, Network.NBitcoinNetwork),
				ReplacedBy = tx.replaced_by is null ? null : uint256.Parse(tx.replaced_by)
			}};
		}

		public async Task<TrackedTransaction[]> GetTransactions(TrackedSource trackedSource, uint256 txId = null, bool includeTransactions = true, CancellationToken cancellation = default)
		{
			await using var connection = await connectionFactory.CreateConnectionHelper(Network);
			var tip = await connection.GetTip();
			var txIdCond = txId is null ? string.Empty : " AND tx_id=@tx_id";
			var utxos = await
				connection.Connection.QueryAsync<(string tx_id, long idx, string blk_id, long? blk_height, int? blk_idx, bool is_out, string spent_tx_id, long spent_idx, string script, string addr, long value, string asset_id, bool immature, string keypath, DateTime seen_at)>(
				"SELECT tx_id, idx, blk_id, blk_height, blk_idx, is_out, spent_tx_id, spent_idx, script, s.addr, value, asset_id, immature, keypath, seen_at " +
				"FROM nbxv1_tracked_txs LEFT JOIN scripts s USING (code, script) " +
				$"WHERE code=@code AND wallet_id=@walletId{txIdCond}", new { code = Network.CryptoCode, walletId = GetWalletKey(trackedSource).wid, tx_id = txId?.ToString() });
			utxos.TryGetNonEnumeratedCount(out int c);
			var trackedById = new Dictionary<string, TrackedTransaction>(c);
			foreach (var utxo in utxos)
			{
				var tracked = GetTrackedTransaction(trackedSource, utxo.tx_id, utxo.blk_id, utxo.blk_height, utxo.blk_idx, utxo.seen_at, trackedById);
				if (utxo.is_out)
				{
					var txout = Network.NBitcoinNetwork.Consensus.ConsensusFactory.CreateTxOut();
					txout.ScriptPubKey = Script.FromHex(utxo.script);
					tracked.KnownAddresses.TryAdd(txout.ScriptPubKey, BitcoinAddress.Create(utxo.addr, this.Network.NBitcoinNetwork));
					txout.Value = Money.Satoshis(utxo.value);
					var coin = new Coin(new OutPoint(tracked.Key.TxId, (uint)utxo.idx), txout);
					if (Network.IsElement)
					{
						tracked.ReceivedCoins.Add(new AssetCoin(new AssetMoney(uint256.Parse(utxo.asset_id), utxo.value), coin));
					}
					else
					{
						tracked.ReceivedCoins.Add(coin);
					}

					// TODO: IsCoinBase is actually not used anywhere.
					tracked.IsCoinBase = utxo.immature;
					tracked.Immature = utxo.immature;
					if (utxo.keypath is string)
						tracked.KnownKeyPathMapping.TryAdd(txout.ScriptPubKey, KeyPath.Parse(utxo.keypath));
					tracked.OwnedScripts.Add(txout.ScriptPubKey);
				}
				else
				{
					tracked.SpentOutpoints.Add(new OutPoint(uint256.Parse(utxo.spent_tx_id), (uint)utxo.spent_idx), (int)utxo.idx);
				}
			}

			var txsToFetch = includeTransactions ? trackedById.Keys.AsList() :
												  // For double spend detection, we need the full transactions from unconfs
												  trackedById.Where(t => t.Value.BlockHash is null).Select(t => t.Key).AsList();
			var txRaws = txsToFetch.Count > 0
				? await connection.Connection.QueryAsync<(string tx_id, byte[] raw)>(
					"SELECT	t.tx_id, t.raw FROM unnest(@txId) i " +
					"JOIN txs t ON t.code=@code AND t.tx_id=i " +
					"WHERE t.raw IS NOT NULL;", new { code = Network.CryptoCode, txId = txsToFetch })
				: Array.Empty<(string tx_id, byte[] raw)>();
			foreach (var row in txRaws)
			{
				var tracked = trackedById[row.tx_id];
				tracked.Transaction = Transaction.Load(row.raw, Network.NBitcoinNetwork);
				tracked.Key = new TrackedTransactionKey(tracked.Key.TxId, tracked.Key.BlockHash, false);
				if (tracked.BlockHash is null) // Only need the spend outpoint for double spend detection on unconf txs
					tracked.SpentOutpoints.AddInputs(tracked.Transaction);
			}

			return trackedById.Values.Select(c =>
			{
				c.OwnedScriptsUpdated();
				return c;
			}).ToArray();
		}

		private TrackedTransaction GetTrackedTransaction(TrackedSource trackedSource, string tx_id, string block_id, long? blk_height, int? blk_idx, DateTime seenAt, Dictionary<string, TrackedTransaction> trackedById)
		{
			if (trackedById.TryGetValue(tx_id, out var tracked))
				return tracked;
			TrackedTransactionKey key = new TrackedTransactionKey(uint256.Parse(tx_id), block_id is null ? null : uint256.Parse(block_id), true);
			tracked = CreateTrackedTransaction(trackedSource, key, null as IEnumerable<Coin>, new Dictionary<Script, KeyPath>());
			tracked.FirstSeen = seenAt;
			tracked.BlockHeight = blk_height;
			tracked.BlockIndex = blk_idx;
			trackedById.Add(tx_id, tracked);
			return tracked;
		}

		public async Task<KeyPathInformation> GetUnused(DerivationStrategyBase strategy, DerivationFeature derivationFeature, int n, bool reserve)
		{
			await using var helper = await connectionFactory.CreateConnectionHelper(Network);
			var connection = helper.Connection;
			var key = GetDescriptorKey(strategy, derivationFeature);
			string additionalColumn = Network.IsElement ? ", ds_metadata->>'blindedAddress' blinded_addr" : string.Empty;
			retry:
			var unused = await connection.QueryFirstOrDefaultAsync(
				$"SELECT script, addr, nbxv1_get_keypath(d_metadata, idx) keypath, ds_metadata->>'redeem' redeem {additionalColumn} FROM descriptors_scripts_unused " +
				"WHERE code=@code AND descriptor=@descriptor " +
				"ORDER BY idx " +
				"LIMIT 1 OFFSET @skip", new { key.code, key.descriptor, skip = n });
			if (unused is null)
			{
				// If we don't find unused address, then either:
				// * We never tracked the wallet
				// * We tracked it, but due to a bug from DBTrie implementation, the change descriptor wasn't track.
				var walletId = DBUtils.nbxv1_get_wallet_id(Network.CryptoCode, strategy.ToString());
				if (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wallets WHERE wallet_id=@walletId", new { walletId }) == 0)
				{
					// We never tracked it, returns null
					return null;
				}
				// We tracked it, but encountered the bug from DBTrie. We need to generate the descriptor.
				if (await GenerateAddressesCore(connection, strategy, derivationFeature, null) != 0)
					goto retry;
				return null;
			}
			if (reserve)
			{
				var updated = await connection.ExecuteAsync("UPDATE descriptors_scripts SET used='t' WHERE code=@code AND script=@script AND descriptor=@descriptor AND used='f'", new { key.code, unused.script, key.descriptor });
				if (updated == 0)
					goto retry;
			}
			var keypath = KeyPath.Parse(unused.keypath);
			var keyInfo = new KeyPathInformation()
			{
				Address = GetAddress(unused),
				DerivationStrategy = strategy,
				KeyPath = keypath,
				ScriptPubKey = Script.FromHex(unused.script),
				TrackedSource = new DerivationSchemeTrackedSource(strategy),
				Feature = KeyPathTemplates.GetDerivationFeature(keypath),
				Redeem = unused.redeem is string s ? Script.FromHex(s) : null
			};
			await ImportAddressToRPC(helper, keyInfo.TrackedSource, keyInfo.Address, keyInfo.KeyPath);
			return keyInfo;
		}

		record SingleAddressInsert(string code, string script, string address, string walletid);
		public async Task SaveKeyInformations(KeyPathInformation[] keyPathInformations)
		{
			await using var connection = await connectionFactory.CreateConnection();
			await SaveKeyInformations(connection, keyPathInformations);
		}

		internal async Task SaveKeyInformations(DbConnection connection, KeyPathInformation[] keyPathInformations)
		{
			var inserts = new List<SingleAddressInsert>();
			var descriptorInsert = new List<DescriptorScriptInsert>();
			foreach (var ki in keyPathInformations)
			{
				DescriptorKey descriptorKey = null;
				JObject metadata = null;
				BitcoinAddress addr = ki.Address;
				if (addr is BitcoinBlindedAddress bba)
					addr = bba.UnblindedAddress;

				if (ki.TrackedSource is DerivationSchemeTrackedSource a)
				{
					descriptorKey = GetDescriptorKey(a.DerivationStrategy, ki.Feature);
					var derivation = a.DerivationStrategy.GetDerivation(ki.KeyPath);
					metadata = GetDescriptorScriptMetadata(
								a.DerivationStrategy,
								ki.KeyPath,
								derivation,
								addr);
				}

				var wid = GetWalletKey(ki.TrackedSource).wid;
				if (descriptorKey is not null)
				{
					descriptorInsert.Add(new DescriptorScriptInsert(
						descriptorKey.descriptor,
						ki.GetIndex(KeyPathTemplates),
						ki.ScriptPubKey.ToHex(),
						metadata?.ToString(Formatting.None),
						addr.ToString(),
						false));
				}
				else
				{
					inserts.Add(new SingleAddressInsert(
						Network.CryptoCode,
						ki.ScriptPubKey.ToHex(),
						addr.ToString(),
						wid));
				}
			}

			if (descriptorInsert.Count > 0)
			{
				await InsertDescriptorsScripts(connection, descriptorInsert);
			}

			if (inserts.Count > 0)
				await connection.ExecuteAsync(
					"INSERT INTO scripts VALUES (@code, @script, @address) ON CONFLICT DO NOTHING;" +
					"INSERT INTO wallets_scripts VALUES (@code, @script, @walletid) ON CONFLICT DO NOTHING;", inserts);
		}
		private async Task<ImportRPCMode> GetImportRPCMode(DbConnectionHelper connection, WalletKey walletKey)
		{
			return ImportRPCMode.Parse((await connection.GetMetadata<string>(walletKey.wid, WellknownMetadataKeys.ImportAddressToRPC)));
		}
		private async Task ImportAddressToRPC(DbConnectionHelper connection, TrackedSource trackedSource, BitcoinAddress address, KeyPath keyPath)
		{
			var k = GetWalletKey(trackedSource);
			var shouldImportRPC = await GetImportRPCMode(connection, k);
			if (shouldImportRPC != ImportRPCMode.Legacy)
				return;
			var accountKey = await connection.GetMetadata<BitcoinExtKey>(k.wid, WellknownMetadataKeys.AccountHDKey);
			await ImportAddressToRPC(accountKey, address, keyPath);
		}
		private async Task ImportAddressToRPC(BitcoinExtKey accountKey, BitcoinAddress address, KeyPath keyPath)
		{
			if (accountKey != null)
			{
				await rpc.ImportPrivKeyAsync(accountKey.Derive(keyPath).PrivateKey.GetWif(Network.NBitcoinNetwork), null, false);
			}
			else
			{
				try
				{
					await rpc.ImportAddressAsync(address, null, false);
				}
				catch (RPCException) // Probably the private key has already been imported
				{
				}
			}
		}
		public async Task Prune(IEnumerable<TrackedTransaction> prunable)
		{
			if (prunable.TryGetNonEnumeratedCount(out var c) && c == 0)
				return;
			await using var helper = await GetConnection();
			var receivedCoinsToDelete =
				prunable
				.Where(p => p.BlockHash is not null)
				.SelectMany(c => c.ReceivedCoins)
				.Select(c => new
				{
					code = Network.CryptoCode,
					txId = c.Outpoint.Hash.ToString(),
					idx = (long)c.Outpoint.N
				}).ToArray();
			var spentCoins =
				prunable
				.Where(p => p.BlockHash is not null)
				.SelectMany(c => c.SpentOutpoints.Select(c => c.Outpoint))
				.Select(c => new
				{
					code = Network.CryptoCode,
					txId = c.Hash.ToString(),
					idx = (long)c.N
				}).ToArray();
			await helper.Connection.ExecuteAsync("DELETE FROM outs WHERE code=@code AND tx_id=@txId AND idx=@idx", receivedCoinsToDelete);
			await helper.Connection.ExecuteAsync("DELETE FROM ins WHERE code=@code AND spent_tx_id=@txId AND spent_idx=@idx", spentCoins);

			var mempoolPrunable =
				prunable
				.Where(p => p.BlockHash is null)
				.Select(p => new { code = Network.CryptoCode, txId = p.TransactionHash.ToString() })
				.ToArray();

			await helper.Connection.ExecuteAsync("UPDATE txs SET mempool='f' WHERE code=@code AND tx_id=@txId", mempoolPrunable);
		}

		public async Task<long> SaveEvent(NewEventBase evt)
		{
			await using var helper = await GetConnection();
			await SaveEvent(helper, evt);
			return evt.EventId;
		}
		public Task SaveEvent(DbConnectionHelper conn, NewEventBase evt)
		{
			return SaveEvents(conn, new NewEventBase[] { evt });
		}

		private static string InsertEventQuery()
		{
			return "WITH cte AS (" +
							"INSERT INTO nbxv1_evts_ids AS ei VALUES (@code, 1) ON CONFLICT (code) DO UPDATE SET curr_id=ei.curr_id+1 " +
							"RETURNING curr_id" +
							") " +
							"INSERT INTO nbxv1_evts (code, id, type, data) VALUES (@code, (SELECT * FROM cte), @type, @data::json) RETURNING id";
		}

		public async Task SaveEvents(DbConnectionHelper conn, NewEventBase[] evts)
		{
			for (int i = 0; i < evts.Length; i++)
			{
				var p = new
				{
					code = Network.CryptoCode,
					data = evts[i].ToJObject(Serializer.Settings).ToString(),
					type = evts[i].EventType
				};
				evts[i].EventId = await conn.Connection.ExecuteScalarAsync<long>(InsertEventQuery(), p);
			}
		}

		public async Task SaveMatches(TrackedTransaction[] transactions)
		{
			if (transactions.Length is 0)
				return;
			await using var helper = await connectionFactory.CreateConnectionHelper(Network);
			var connection = helper.Connection;

			var outCount = transactions.Select(t => t.ReceivedCoins.Count).Sum();
			var inCount = transactions.Select(t => t.SpentOutpoints.Count).Sum();
			List<DbConnectionHelper.NewOut> outs = new List<DbConnectionHelper.NewOut>(outCount);
			List<DbConnectionHelper.NewIn> ins = new List<DbConnectionHelper.NewIn>(inCount);
			foreach (var tx in transactions)
			{
				if (!tx.IsCoinBase)
				{
					foreach (var input in tx.SpentOutpoints)
					{
						ins.Add(new DbConnectionHelper.NewIn(
							tx.TransactionHash,
							input.InputIndex,
							input.Outpoint.Hash,
							(int)input.Outpoint.N
							));
					}
				}

				foreach (var output in tx.GetReceivedOutputs())
				{
					outs.Add(new DbConnectionHelper.NewOut(
						tx.TransactionHash,
						output.Index,
						output.ScriptPubKey,
						(Money)output.Value
						));
				}
			}
			await helper.FetchMatches(outs, ins);
			await helper.SaveTransactions(transactions.Select(SaveTransactionRecord.Create));
			await helper.Connection.ExecuteAsync("CALL save_matches(@code)", new { code = Network.CryptoCode });
		}

		public async Task SaveMetadata<TMetadata>(TrackedSource source, string key, TMetadata value) where TMetadata : class
		{
			await using var helper = await connectionFactory.CreateConnectionHelper(Network);
			var walletKey = GetWalletKey(source);
			if (!await helper.SetMetadata(walletKey.wid, key, value))
			{
				await helper.Connection.ExecuteAsync("INSERT INTO wallets VALUES (@wid, @metadata::JSONB) ON CONFLICT DO NOTHING", walletKey);
				await helper.SetMetadata(walletKey.wid, key, value);
			}
		}

		public async Task<TMetadata> GetMetadata<TMetadata>(TrackedSource source, string key) where TMetadata : class
		{
			await using var helper = await connectionFactory.CreateConnectionHelper(Network);
			var walletKey = GetWalletKey(source);
			return await helper.GetMetadata<TMetadata>(walletKey.wid, key);
		}

		public async Task<List<SavedTransaction>> SaveTransactions(DateTimeOffset now, Transaction[] transactions, SlimChainedBlock slimBlock)
		{
			await using var helper = await connectionFactory.CreateConnectionHelper(Network);
			await helper.SaveTransactions(transactions.Select(t => new SaveTransactionRecord(t, null as uint256, slimBlock?.Hash, null as int?, (long?)slimBlock?.Height, false, new DateTimeOffset?(now))));
			return transactions.Select(t => new SavedTransaction()
			{
				BlockHash = slimBlock?.Hash,
				Timestamp = now,
				Transaction = t
			}).ToList();
		}

		public async Task SetIndexProgress(BlockLocator locator)
		{
			await using var conn = await connectionFactory.CreateConnection();
			await SetIndexProgress(conn, locator);
		}
		internal async Task SetIndexProgress(DbConnection conn, BlockLocator locator)
		{
			if (locator is not null)
			{
				await conn.ExecuteAsync(
					"INSERT INTO nbxv1_settings VALUES (@code, @key, @data)" +
					"ON CONFLICT (code, key) DO UPDATE SET data_bytes=EXCLUDED.data_bytes;", new { code = Network.CryptoCode, key = $"BlockLocator-{InstanceSuffix}", data = locator.ToBytes() });
			}
			else
			{
				await conn.ExecuteAsync("DELETE FROM nbxv1_settings WHERE code=@code AND key=@key;", new { code = Network.CryptoCode, key = $"BlockLocator-{InstanceSuffix}" });
			}
		}
		public async Task<BlockLocator> GetIndexProgress()
		{
			await using var connection = await connectionFactory.CreateConnection();
			return await GetIndexProgressCore(connection);
		}

		private async Task<BlockLocator> GetIndexProgressCore(DbConnection connection)
		{
			var data = await connection.QueryFirstOrDefaultAsync<byte[]>("SELECT data_bytes FROM nbxv1_settings WHERE code=@code AND key=@key", new { code = Network.CryptoCode, key = $"BlockLocator-{InstanceSuffix}" });
			if (data is null)
				return null;
			var locator = new BlockLocator();
			locator.ReadWrite(data, Network.NBitcoinNetwork);
			return locator;
		}

		public async Task Track(IDestination address)
		{
			await using var conn = await GetConnection();
			var walletKey = GetWalletKey(address, Network);
			await conn.Connection.ExecuteAsync(
				WalletInsertQuery +
				"INSERT INTO scripts VALUES (@code, @script, @addr) ON CONFLICT DO NOTHING;" +
				"INSERT INTO wallets_scripts VALUES (@code, @script, @wid) ON CONFLICT DO NOTHING"
				, new { code = Network.CryptoCode, script = address.ScriptPubKey.ToHex(), addr = address.ScriptPubKey.GetDestinationAddress(Network.NBitcoinNetwork).ToString(), walletKey.wid, walletKey.metadata });
		}

		public async ValueTask<int> TrimmingEvents(int maxEvents, CancellationToken cancellationToken = default)
		{
			await using var ds = connectionFactory.CreateDataSourceBuilder(o => o.CommandTimeout = Constants.FifteenMinutes).Build();
			await using var conn = await ds.ReliableOpenConnectionAsync();
			var id = await conn.ExecuteScalarAsync<long?>("SELECT id FROM nbxv1_evts WHERE code=@code ORDER BY id DESC OFFSET @maxEvents LIMIT 1", new { code = Network.CryptoCode, maxEvents = maxEvents - 1 });
			if (id is long i)
				return await conn.ExecuteAsync("DELETE FROM nbxv1_evts WHERE code=@code AND id < @id", new { code = Network.CryptoCode, id = i });
			return 0;
		}

		private Task<DbConnectionHelper> GetConnection()
		{
			return connectionFactory.CreateConnectionHelper(Network);
		}

		public async Task UpdateAddressPool(DerivationSchemeTrackedSource trackedSource, Dictionary<DerivationFeature, int?> highestKeyIndexFound)
		{
			await using var conn = await GetConnection();

			var parameters = KeyPathTemplates
				.GetSupportedDerivationFeatures()
				.Select(p =>
				{
					if (highestKeyIndexFound.TryGetValue(p, out var highest) && highest is int h)
						return new { DerivationFeature = p, HighestKeyIndexFound = h };
					return null;
				})
				.Where(p => p is not null)
				.Select(p => new
				{
					code = Network.CryptoCode,
					descriptor = this.GetDescriptorKey(trackedSource.DerivationStrategy, p.DerivationFeature).descriptor,
					next_index = p.HighestKeyIndexFound + 1
				})
				.ToArray();
			await conn.Connection.ExecuteAsync("UPDATE descriptors SET next_idx=@next_index WHERE code=@code AND descriptor=@descriptor", parameters);
			await conn.Connection.ExecuteAsync("UPDATE descriptors_scripts SET used='t' WHERE code=@code AND descriptor=@descriptor AND idx < @next_index", parameters);

			foreach (var p in highestKeyIndexFound.Where(k => k.Value is not null))
				await GenerateAddresses(trackedSource.DerivationStrategy, p.Key);
		}

		public async Task<SlimChainedBlock> GetTip()
		{
			await using var conn = await GetConnection();
			return await conn.GetTip();
		}

		record BlockRow(string blk_id, long height, string prev_id);
		internal async Task<SlimChainedBlock> GetLastIndexedSlimChainedBlock(BlockLocator progress)
		{
			await using var conn = await connectionFactory.CreateConnection();
			var row = await conn.QueryFirstOrDefaultAsync<BlockRow>(
				"SELECT blk_id, height, prev_id FROM blks " +
				"WHERE code=@code AND blk_id=ANY(@blk_ids) AND confirmed IS TRUE " +
				"ORDER BY height DESC " +
				"LIMIT 1;", new { code = Network.CryptoCode, blk_ids = progress.Blocks.Select(b => b.ToString()).ToArray() });
			if (row is null)
				return null;
			return new SlimChainedBlock(uint256.Parse(row.blk_id), row.height == 0
				? null
				: uint256.Parse(row.prev_id), (int)row.height);

		}

		public async Task SaveBlocks(IList<SlimChainedBlock> slimBlocks)
		{
			await using var conn = await GetConnection();
			var parameters =
				slimBlocks
				.Select(s => new
				{
					code = Network.CryptoCode,
					blk_id = s.Hash.ToString(),
					prev_id = s.Previous.ToString(),
					height = s.Height
				}).ToList();
			await conn.Connection.ExecuteAsync("INSERT INTO blks (code, blk_id, prev_id, height, confirmed) VALUES (@code, @blk_id, @prev_id, @height, 't') ON CONFLICT DO NOTHING", parameters);
		}

		public async Task EnsureWalletCreated(DerivationStrategyBase strategy)
		{
			await EnsureWalletCreated(GetWalletKey(strategy, Network));
		}

		public async Task EnsureWalletCreated(TrackedSource trackedSource)
		{
			await EnsureWalletCreated(GetWalletKey(trackedSource));
		}

		record WalletHierarchyInsert(string child, string parent);
		public async Task EnsureWalletCreated(WalletKey walletKey)
		{
			await using var connection = await ConnectionFactory.CreateConnection();
			await connection.ExecuteAsync(WalletInsertQuery, walletKey);
		}

		internal static readonly string WalletInsertQuery = "INSERT INTO wallets (wallet_id, metadata) VALUES (@wid, @metadata::JSONB) ON CONFLICT DO NOTHING;";
	}
}
