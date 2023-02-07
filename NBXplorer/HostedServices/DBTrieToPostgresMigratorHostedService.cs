extern alias DBTrieLib;

using Dapper;
using DBTrieLib::DBTrie;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer.Altcoins.Liquid;
using NBXplorer.Backends;
using NBXplorer.Backends.DBTrie;
using NBXplorer.Backends.Postgres;
using NBXplorer.Configuration;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql.TypeMapping;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static NBXplorer.Backends.Postgres.DbConnectionHelper;

namespace NBXplorer.HostedServices
{
	public class DBTrieToPostgresMigratorHostedService : IHostedService
	{
		public class MigrationProgress
		{
			public bool EventsMigrated { get; set; }
			public bool KeyPathInformationMigrated { get; set; }
			public bool MetadataMigrated { get; set; }
			public bool HighestPathMigrated { get; set; }
			public bool AvailableKeysMigrated { get; set; }
			public bool SavedTransactionsMigrated { get; set; }
			public bool TrackedTransactionsMigrated { get; set; }
			public bool TrackedTransactionsInputsMigrated { get; set; }
			public bool BlocksMigrated { get; set; }
			public bool FullyUpdated => TrackedTransactionsInputsMigrated;
		}
		public DBTrieToPostgresMigratorHostedService(
			RepositoryProvider repositoryProvider,
			IRepositoryProvider postgresRepositoryProvider,
			ILoggerFactory loggerFactory,
			IConfiguration configuration,
			KeyPathTemplates keyPathTemplates,
			IRPCClients rpcClients,
			DbConnectionFactory connectionFactory,
			ExplorerConfiguration explorerConfiguration)
		{
			LegacyRepositoryProvider = repositoryProvider;
			this.LoggerFactory = loggerFactory;
			Configuration = configuration;
			KeyPathTemplates = keyPathTemplates;
			RpcClients = rpcClients;
			ConnectionFactory = connectionFactory;
			ExplorerConfiguration = explorerConfiguration;
			PostgresRepositoryProvider = (PostgresRepositoryProvider)postgresRepositoryProvider;
		}

		public RepositoryProvider LegacyRepositoryProvider { get; }

		public ILoggerFactory LoggerFactory { get; }
		public IConfiguration Configuration { get; }
		public KeyPathTemplates KeyPathTemplates { get; }
		public IRPCClients RpcClients { get; }
		public DbConnectionFactory ConnectionFactory { get; }
		public ExplorerConfiguration ExplorerConfiguration { get; }
		public PostgresRepositoryProvider PostgresRepositoryProvider { get; }

		bool started;
		public async Task StartAsync(CancellationToken cancellationToken)
		{
			if (!LegacyRepositoryProvider.Exists())
				return;

			var migrationId = await PostgresRepositoryProvider.GetMigrationId();

			var migrationState = LegacyRepositoryProvider.GetMigrationState();

			if (migrationId != null && (migrationState.State != RepositoryProvider.MigrationState.NotStarted && migrationState.MigrationId != migrationId))
			{
				throw new ConfigException("The database has started migration of a different DBTrie backend.");
			}

			if (migrationState.MigrationId is not null && migrationId != migrationState.MigrationId)
			{
				if (migrationState.State != RepositoryProvider.MigrationState.Done ||
					!Configuration.GetOrDefault<bool>("deleteaftermigration", false))
				{
					if (migrationState.State == RepositoryProvider.MigrationState.Done)
					{
						var error = "The DBTrie database has been been migrated to a different postgres database. Please do one of the following alternative: " + Environment.NewLine +
							"1. If this is a configuration mistake, switch the postgres database to the one you originally migrated to" + Environment.NewLine +
							"2. If you want to start NBXplorer on the postgres database, turn off --automigrate" + Environment.NewLine +
							$"3. If you want to migrate DBTrie on a new database, delete '{LegacyRepositoryProvider.GetMigrationLockPath()}'";
						throw new ConfigException(error);
					}
					else
					{
						var error = "The DBTrie database is beeing migrated to a different postgres database. Please do one of the following alternative: " + Environment.NewLine +
							"1. If this is a configuration mistake, switch the postgres database  you were originally migrating to" + Environment.NewLine +
							$"2. If you want to migrate DBTrie on a new database, delete '{LegacyRepositoryProvider.GetMigrationLockPath()}'";
						throw new ConfigException(error);
					}
				}
			}

			if (migrationState.State == RepositoryProvider.MigrationState.Done)
			{
				DeleteAfterMigrationOrWarning();
				return;
			}
			if (migrationState.State == RepositoryProvider.MigrationState.NotStarted)
			{
				if (migrationId != null)
				{
					throw new ConfigException("This postgres database is migrating (or migrated) a different DBTrie database.");
				}
				var id = RandomUtils.GetUInt256();
				migrationId = id.ToString();
				File.WriteAllText(LegacyRepositoryProvider.GetMigrationLockPath(), $"InProgress {migrationId}");
				await PostgresRepositoryProvider.SetMigrationId(id);
			}
			LegacyRepositoryProvider.MigrationMode = true;
			await LegacyRepositoryProvider.StartAsync(cancellationToken);
			started = true;
			Stopwatch w = new Stopwatch();
			w.Start();
			try
			{
				foreach (var legacyRepo in LegacyRepositoryProvider.GetRepositories())
				{
					var postgresRepo = PostgresRepositoryProvider.GetRepository(legacyRepo.Network);
					await Migrate(legacyRepo.Network, legacyRepo, (PostgresRepository)postgresRepo,
						LoggerFactory.CreateLogger($"NBXplorer.PostgresMigration.{legacyRepo.Network.CryptoCode}"), cancellationToken);
				}
			}
			catch when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			var logger = LoggerFactory.CreateLogger($"NBXplorer.PostgresMigration");
			await using (var conn = await ConnectionFactory.CreateConnection(builder =>
			{
				builder.CommandTimeout = 60 * 15;
			}))
			{
				logger.LogInformation($"Running ANALYZE and VACUUM FULL...");
				try
				{
					await conn.ExecuteAsync("VACUUM FULL;");
					await conn.ExecuteAsync("ANALYZE;");
				}
				// Don't care if it fails
				catch { }
			}
			w.Stop();
			logger.LogInformation($"The migration completed in {(int)w.Elapsed.TotalMinutes} minutes.");
			File.WriteAllText(LegacyRepositoryProvider.GetMigrationLockPath(), $"Done {migrationId}");
			DeleteAfterMigrationOrWarning();
			GC.Collect();
		}

		private void DeleteAfterMigrationOrWarning()
		{
			var logger = LoggerFactory.CreateLogger("NBXplorer.PostgresMigration");
			if (!Configuration.GetOrDefault<bool>("deleteaftermigration", false))
				logger.LogWarning($"A legacy DBTrie database has been previously migrated to postgres and is still present. You can safely delete it if you do not expect using it in the future. To delete the old DBTrie database, start NBXplorer with --deleteaftermigration (or environment variable: NBXPLORER_DELETEAFTERMIGRATION=1)");
			else
			{
				Directory.Delete(LegacyRepositoryProvider.GetDatabasePath(), true);
				logger.LogInformation($"Old migrated legacy DBTrie database has been deleted");
			}
		}

		private async Task RegisterTypes(System.Data.Common.DbConnection conn)
		{
			var pconn = (Npgsql.NpgsqlConnection)conn;
			try
			{
				await pconn.ExecuteAsync(
					"CREATE TYPE m_txs AS (tx_id TEXT, raw BYTEA, seen_at TIMESTAMPTZ);" +
					"CREATE TYPE m_blks_txs AS (tx_id TEXT, blk_id TEXT);" +
					"CREATE TYPE m_evt AS (id BIGINT, type TEXT, data JSONB);");
			}
			// They may already exists
			catch { }
			pconn.ReloadTypes();
			pconn.TypeMapper.MapComposite<UpdateTransaction>("m_txs");
			pconn.TypeMapper.MapComposite<UpdateBlockTransaction>("m_blks_txs");
			pconn.TypeMapper.MapComposite<InsertEvents>("m_evt");
		}
		private async Task UnregisterTypes(System.Data.Common.DbConnection conn)
		{
			var pconn = (Npgsql.NpgsqlConnection)conn;
			pconn.TypeMapper.UnmapComposite<UpdateTransaction>("m_txs");
			pconn.TypeMapper.UnmapComposite<UpdateBlockTransaction>("m_blks_txs");
			pconn.TypeMapper.UnmapComposite<InsertEvents>("m_evt");
			await pconn.ExecuteAsync(
				"DROP TYPE m_txs;" +
				"DROP TYPE m_blks_txs;" +
				"DROP TYPE m_evt;");
			pconn.ReloadTypes();
		}


		record InsertEvents(long id, string type, string data);
		record InsertDescriptor(string code, string descriptor, string metadata, string wallet_id);
		record InsertMetadata(string wallet_id, string key, string value);
		record UpdateNextIndex(string code, string descriptor, long next_idx);
		record UpdateUsedScript(string code, string descriptor, long idx);
		record UpdateBlock(string code, string blk_id, string prev_id, long height);
		record UpdateTransaction(string tx_id, byte[] raw, DateTime seen_at);
		record UpdateBlockTransaction(string tx_id, string blk_id);
		private async Task Migrate(NBXplorerNetwork network, Repository legacyRepo, PostgresRepository postgresRepo, ILogger logger, CancellationToken cancellationToken)
		{
			using var conn = await postgresRepo.ConnectionFactory.CreateConnection(builder =>
			{
				builder.CommandTimeout = 120;
			});
			var data = await conn.QueryFirstOrDefaultAsync<string>("SELECT data_json FROM nbxv1_settings WHERE code=@code AND key='MigrationProgress'", new { code = network.CryptoCode });
			var progress = data is null ? new MigrationProgress() : JsonConvert.DeserializeObject<MigrationProgress>(data);
			if (progress.FullyUpdated)
				return;
			await RegisterTypes(conn);
			if (!progress.EventsMigrated)
			{
				if (!ExplorerConfiguration.NoMigrateEvents)
				{
					using (var tx = await conn.BeginTransactionAsync())
					{
						logger.LogInformation($"Migrating events to postgres...");
						long lastEventId = -1;
						nextbatch:
						var batch = await legacyRepo.GetEvents(lastEventId, 1000);
						if (batch.Count > 0)
						{
							cancellationToken.ThrowIfCancellationRequested();
							var parameters = batch.Select(e => new InsertEvents(
								e.EventId,
								e.EventType,
								e.ToJson(network.JsonSerializerSettings)
							)).ToArray();
							await conn.ExecuteAsync(
								"INSERT INTO nbxv1_evts " +
								"SELECT @code, id, type, data " +
								"FROM unnest(@records)", 
								new
								{
									code = network.CryptoCode,
									records = parameters
								});
							lastEventId = parameters.Select(p => p.id).Max();
							goto nextbatch;
						}
						await conn.ExecuteAsync("INSERT INTO nbxv1_evts_ids AS ei VALUES (@code, @curr_id) ON CONFLICT (code) DO UPDATE SET curr_id=@curr_id WHERE ei.curr_id < @curr_id", new { code = network.CryptoCode, curr_id = lastEventId });
						progress.EventsMigrated = true;
						await SaveProgress(network, conn, progress);
						await tx.CommitAsync();
						logger.LogInformation($"Events migrated.");
					}
				}
				else
				{
					logger.LogInformation("Events migration skipped");
				}
			}
			Dictionary<string, TrackedSource> hashToTrackedSource = new Dictionary<string, TrackedSource>();
			if (!progress.KeyPathInformationMigrated)
			{
				logger.LogInformation($"Migrating scripts to postgres...");
				using (var tx = await conn.BeginTransactionAsync())
				{
					List<KeyPathInformation> batch = new List<KeyPathInformation>(10_000);
					HashSet<string> processedWalletKeys = new HashSet<string>();
					List<PostgresRepository.WalletKey> walletKeys = new List<PostgresRepository.WalletKey>();
					HashSet<(string code, string descriptor)> processedDescriptors = new HashSet<(string code, string descriptor)>();
					List<InsertDescriptor> descriptors = new List<InsertDescriptor>();
					using var legacyTx = await legacyRepo.engine.OpenTransaction();
					var scriptsTable = legacyTx.GetTable($"{legacyRepo._Suffix}Scripts");
					var total = await scriptsTable.GetRecordCount();
					int migrated = 0;
					// The triggers update the next_idx, used and gap fields of descriptors.
					// Those make the insert very slow, and those are updated when reserverd and available scripts
					// are imported later.
					await conn.ExecuteAsync(
								"ALTER TABLE descriptors_scripts " +
								"DISABLE TRIGGER USER; " +
								"ALTER TABLE descriptors_scripts " +
								"ENABLE TRIGGER descriptors_scripts_wallets_scripts_trigger;");
					await foreach (var row in scriptsTable.Enumerate())
					{
						cancellationToken.ThrowIfCancellationRequested();
						migrated++;
						if (migrated % 10_000 == 0)
							logger.LogInformation($"Progress: " + (int)(((double)migrated / (double)total) * 100.0) + "%");
						using (row)
						{
							var keyInfo = legacyRepo.ToObject<KeyPathInformation>(await row.ReadValue())
											.AddAddress(network.NBitcoinNetwork);
							hashToTrackedSource.TryAdd(keyInfo.TrackedSource.GetHash().ToString(), keyInfo.TrackedSource);
							batch.Add(keyInfo);
							if (keyInfo.TrackedSource is DerivationSchemeTrackedSource ts)
							{
								var keyTemplate = KeyPathTemplates.GetKeyPathTemplate(keyInfo.Feature);
								var k = postgresRepo.GetDescriptorKey(ts.DerivationStrategy, keyInfo.Feature);
								if (processedDescriptors.Add((k.code, k.descriptor)))
									descriptors.Add(new InsertDescriptor(k.code, k.descriptor, network.Serializer.ToString(new LegacyDescriptorMetadata()
									{
										Derivation = ts.DerivationStrategy,
										Feature = keyInfo.Feature,
										KeyPathTemplate = keyTemplate,
										Type = LegacyDescriptorMetadata.TypeName
									}),
									postgresRepo.GetWalletKey(ts.DerivationStrategy).wid));
							}
							var wk = postgresRepo.GetWalletKey(keyInfo.TrackedSource);
							if (processedWalletKeys.Add(wk.wid))
								walletKeys.Add(wk);
							if (batch.Count >= 10_000)
							{
								await CreateWalletAndDescriptor(conn, walletKeys, descriptors);
								walletKeys.Clear();
								descriptors.Clear();
								await postgresRepo.SaveKeyInformations(conn, batch.ToArray());
								batch.Clear();
								walletKeys.Clear();
								descriptors.Clear();
							}
						}
					}
					await CreateWalletAndDescriptor(conn, walletKeys, descriptors);
					await postgresRepo.SaveKeyInformations(conn, batch.ToArray());

					await conn.ExecuteAsync("UPDATE descriptors_scripts SET used='t'");
					await conn.ExecuteAsync(
									"ALTER TABLE descriptors_scripts " +
									"ENABLE TRIGGER USER;");

					await conn.ExecuteAsync("CREATE TABLE tmp_mapping_tracked_sources AS SELECT * FROM unnest (@a, @b) AS r (hash, descriptor)", new
					{
						a = hashToTrackedSource.Select(k => k.Key).ToArray(),
						b = hashToTrackedSource.Select(k => k.Value.ToString()).ToArray()
					});
					logger.LogInformation($"Scripts migrated.");
					progress.KeyPathInformationMigrated = true;
					await SaveProgress(network, conn, progress);
					await tx.CommitAsync();
				}
			}

			if (hashToTrackedSource.Count is 0)
			{
				// We didn't run keypath migration
				logger.LogInformation($"Scanning the tracked source...");
				foreach (var r in await conn.QueryAsync<(string hash, string derivation)>(
					"SELECT hash, descriptor " +
					"FROM tmp_mapping_tracked_sources"))
				{
					hashToTrackedSource.TryAdd(r.hash, TrackedSource.Parse(r.derivation, network));
				}
			}

			if (!progress.MetadataMigrated)
			{
				logger.LogInformation($"Migrating metadata to postgres...");
				using (var tx = await conn.BeginTransactionAsync())
				{
					using var legacyTx = await legacyRepo.engine.OpenTransaction();
					var metadataTable = legacyTx.GetTable($"{legacyRepo._Suffix}Metadata");
					List<InsertMetadata> batch = new List<InsertMetadata>(1000);
					await foreach (var row in metadataTable.Enumerate())
					{
						cancellationToken.ThrowIfCancellationRequested();
						using (row)
						{
							var v = network.Serializer.ToObject<JToken>(legacyRepo.Unzip(await row.ReadValue()));
							var s = Encoding.UTF8.GetString(row.Key.Span).Split('-');
							var trackedSource = hashToTrackedSource[s[0]];
							var key = s[1];
							batch.Add(new InsertMetadata(postgresRepo.GetWalletKey(trackedSource).wid, key, v.ToString(Formatting.None)));
							if (batch.Count >= 1000)
							{
								await conn.ExecuteAsync("INSERT INTO nbxv1_metadata VALUES (@wallet_id, @key, @value::JSONB)", batch);
								batch.Clear();
							}
						}
					}
					await conn.ExecuteAsync("INSERT INTO nbxv1_metadata VALUES (@wallet_id, @key, @value::JSONB)", batch);
					logger.LogInformation($"metadata migrated.");
					progress.MetadataMigrated = true;
					await SaveProgress(network, conn, progress);
					await tx.CommitAsync();
				};
			}

			if (!progress.HighestPathMigrated)
			{
				logger.LogInformation($"Migrating highest path to postgres...");
				using (var tx = await conn.BeginTransactionAsync())
				{
					var batch = new List<UpdateNextIndex>(100);
					using var legacyTx = await legacyRepo.engine.OpenTransaction();
					var highestPath = legacyTx.GetTable($"{legacyRepo._Suffix}HighestPath");
					await foreach (var row in highestPath.Enumerate())
					{
						cancellationToken.ThrowIfCancellationRequested();
						using (row)
						{
							var s = Encoding.UTF8.GetString(row.Key.Span).Split('-');
							var feature = Enum.Parse<DerivationFeature>(s[1]);
							var scheme = ((DerivationSchemeTrackedSource)hashToTrackedSource[s[0]]).DerivationStrategy;
							var key = postgresRepo.GetDescriptorKey(scheme, feature);
							var v = await row.ReadValue();
							uint value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(v.Span);
							value = value & ~0x8000_0000U;
							batch.Add(new UpdateNextIndex(key.code, key.descriptor, value + 1));
							if (batch.Count >= 100)
							{
								await conn.ExecuteAsync("UPDATE descriptors SET next_idx=@next_idx WHERE code=@code AND descriptor=@descriptor", batch);
								batch.Clear();
							}
						}
					}
					await conn.ExecuteAsync("UPDATE descriptors SET next_idx=@next_idx WHERE code=@code AND descriptor=@descriptor", batch);
					logger.LogInformation($"highest path migrated.");
					progress.HighestPathMigrated = true;
					await SaveProgress(network, conn, progress);
					await tx.CommitAsync();
				}
			}

			if (!progress.AvailableKeysMigrated)
			{
				logger.LogInformation($"Migrating available keys to postgres...");
				using var tx = await conn.BeginTransactionAsync();
				var batch = new List<PostgresRepository.DescriptorScriptInsert>(10_000);
				using var legacyTx = await legacyRepo.engine.OpenTransaction();
				var availableTable = legacyTx.GetTable($"{legacyRepo._Suffix}AvailableKeys");
				var total = await availableTable.GetRecordCount();
				int migrated = 0;
				await conn.ExecuteAsync(
									"ALTER TABLE descriptors_scripts " +
									"DISABLE TRIGGER USER;");
				await foreach (var row in availableTable.Enumerate())
				{
					cancellationToken.ThrowIfCancellationRequested();
					migrated++;
					if (migrated % 10_000 == 0)
						logger.LogInformation($"Progress: " + (int)(((double)migrated / (double)total) * 100.0) + "%");
					using (row)
					{
						var s = Encoding.UTF8.GetString(row.Key.Span).Split('-');
						if (!Enum.TryParse<DerivationFeature>(s[1], out var feature))
							continue; // This should never happen, but one user got a corruption on DBTrie before and this happen...
						var scheme = ((DerivationSchemeTrackedSource)hashToTrackedSource[s[0]]).DerivationStrategy;
						var key = postgresRepo.GetDescriptorKey(scheme, feature);
						var idx = int.Parse(s[^1]);
						batch.Add(new PostgresRepository.DescriptorScriptInsert(key.descriptor, idx, null, null, null, true));
						if (batch.Count >= 10_000)
						{
							await conn.ExecuteAsync(
								"UPDATE descriptors_scripts ds SET used='f' " +
								"FROM unnest(@records) r " +
								"WHERE ds.code=@code AND ds.descriptor=r.descriptor AND ds.idx=r.idx AND ds.used IS TRUE", 
								new
								{
									code = network.CryptoCode,
									records = batch
								});
							batch.Clear();
						}
					}
				}
				await conn.ExecuteAsync(
								"UPDATE descriptors_scripts ds SET used='f' " +
								"FROM unnest(@records) r " +
								"WHERE ds.code=@code AND ds.descriptor=r.descriptor AND ds.idx=r.idx AND ds.used IS TRUE",
								new
								{
									code = network.CryptoCode,
									records = batch
								});
				// Update the gap of descriptors
				await conn.ExecuteAsync(
					"WITH cte AS (SELECT descriptor, MAX(ds.idx) last_idx FROM descriptors_scripts ds WHERE ds.code=@code AND ds.used IS TRUE GROUP BY descriptor)" +
					"UPDATE descriptors d " +
					"SET gap = COALESCE(next_idx - (SELECT last_idx FROM cte WHERE descriptor=d.descriptor) - 1, next_idx) " +
					"WHERE code=@code", new { code = network.CryptoCode });
				await conn.ExecuteAsync(
									"ALTER TABLE descriptors_scripts " +
									"ENABLE TRIGGER USER;");
				progress.AvailableKeysMigrated = true;

				// Somehow, it seems some descriptors need to generate a few addresses.
				foreach (var desc in await conn.QueryAsync("SELECT metadata FROM descriptors WHERE code=@code AND gap < @minGap", new { code = network.CryptoCode, minGap = ExplorerConfiguration.MinGapSize }))
				{
					LegacyDescriptorMetadata metadata = network.Serializer.ToObject<LegacyDescriptorMetadata>((string)desc.metadata);
					if (KeyPathTemplates.GetSupportedDerivationFeatures().Contains(metadata.Feature))
					{
						await postgresRepo.GenerateAddressesCore(conn, metadata.Derivation, metadata.Feature, null);
					}
				}

				await SaveProgress(network, conn, progress);
				await tx.CommitAsync();
				logger.LogInformation($"Available keys migrated.");
			}

			if (!progress.BlocksMigrated)
			{
				logger.LogInformation($"Migrating blocks to postgres...");
				using var tx = await conn.BeginTransactionAsync();
				HashSet<uint256> blocksToFetch = new HashSet<uint256>();
				using var legacyTx = await legacyRepo.engine.OpenTransaction();
				var savedTxsTable = legacyTx.GetTable($"{legacyRepo._Suffix}Txs");
				await foreach (var row in savedTxsTable.Enumerate())
				{
					cancellationToken.ThrowIfCancellationRequested();
					using (row)
					{
						if (row.Key.Length == 64)
						{
							blocksToFetch.Add(new uint256(row.Key.Span.Slice(32, 32)));
						}
					}
				}
				var trackedTxs = legacyTx.GetTable($"{legacyRepo._Suffix}Transactions");
				await foreach (var row in trackedTxs.Enumerate())
				{
					cancellationToken.ThrowIfCancellationRequested();
					using (row)
					{
						var key = TrackedTransactionKey.Parse(row.Key.Span);
						if (key.BlockHash is not null)
							blocksToFetch.Add(key.BlockHash);
					}
				}

				var indexProgress = await legacyRepo.GetIndexProgress(legacyTx);
				if (indexProgress?.Blocks is not null)
				{
					foreach (var b in indexProgress.Blocks)
						blocksToFetch.Add(b);
				}
				logger.LogInformation($"Blocks to import: " + blocksToFetch.Count);
				IGetBlockHeaders getBlocks = await GetBlockProvider(network, logger);
				foreach (var batch in blocksToFetch.Batch(500))
				{
					cancellationToken.ThrowIfCancellationRequested();
					var rpc = RpcClients.Get(network);
					var update = await getBlocks.GetUpdateBlocks(batch);
					await conn.ExecuteAsync("INSERT INTO blks VALUES (@code, @blk_id, @height, @prev_id, 't')", update);
				}

				// Here, we just make sure the index progress we save only have confirmed blocks.
				// differences may happen if loading from slim-chain when the node crashed.
				if (indexProgress?.Blocks is not null)
				{
					var confirmedBlocks = await getBlocks.GetUpdateBlocks(indexProgress.Blocks);
					var locator = new BlockLocator();
					foreach (var b in confirmedBlocks)
					{
						locator.Blocks.Add(new uint256(b.blk_id));
					}
					await postgresRepo.SetIndexProgress(conn, locator);
				}

				progress.BlocksMigrated = true;
				await SaveProgress(network, conn, progress);
				await tx.CommitAsync();
				logger.LogInformation($"Blocks migrated.");
			}

			if (!progress.SavedTransactionsMigrated)
			{
				if (!ExplorerConfiguration.NoMigrateRawTxs)
				{
					logger.LogInformation($"Migrating raw transactions...");
					using var tx = await conn.BeginTransactionAsync();
					var batchTxs = new List<UpdateTransaction>(1000);
					var batchBlocksTxs = new List<UpdateBlockTransaction>(1000);

					using var legacyTx = await legacyRepo.engine.OpenTransaction();
					var savedTxsTable = legacyTx.GetTable($"{legacyRepo._Suffix}Txs");
					var total = await savedTxsTable.GetRecordCount();
					HashSet<uint256> processedTxs = new HashSet<uint256>();
					long migrated = 0;
					await foreach (var row in savedTxsTable.Enumerate())
					{
						cancellationToken.ThrowIfCancellationRequested();
						migrated++;
						if (migrated % 10_000 == 0)
							logger.LogInformation($"Progress: " + (int)(((double)migrated / (double)total) * 100.0) + "%");
						using (row)
						{
							var txId = new uint256(row.Key.Span.Slice(0, 32));
							if (!processedTxs.Add(txId))
								continue;
							var savedTx = Repository.ToSavedTransaction(network.NBitcoinNetwork, row.Key, await row.ReadValue());
							if (savedTx.BlockHash is not null)
							{
								batchBlocksTxs.Add(new UpdateBlockTransaction(savedTx.Transaction.GetHash().ToString(), savedTx.BlockHash.ToString()));
							}
							batchTxs.Add(new UpdateTransaction(savedTx.Transaction.GetHash().ToString(), savedTx.Transaction.ToBytes(), savedTx.Timestamp.UtcDateTime));
							if (batchTxs.Count >= 1000)
							{
								await InsertTransactions(conn, network, batchTxs, batchBlocksTxs);
								batchBlocksTxs.Clear();
								batchTxs.Clear();
								processedTxs.Clear();
							}
						}
					}
					await InsertTransactions(conn, network, batchTxs, batchBlocksTxs);
					batchTxs.Clear();
					batchBlocksTxs.Clear();
					progress.SavedTransactionsMigrated = true;
					await SaveProgress(network, conn, progress);
					await tx.CommitAsync();
					logger.LogInformation($"Raw transactions migrated.");
				}
				else
				{
					logger.LogInformation("Raw transactions migration skipped");
				}
			}

			if (!progress.TrackedTransactionsMigrated)
			{
				logger.LogInformation($"Migrating tracked transactions and outputs...");
				using var tx = await conn.BeginTransactionAsync();
				using var legacyTx = await legacyRepo.engine.OpenTransaction();
				var savedTxsTable = legacyTx.GetTable($"{legacyRepo._Suffix}Transactions");
				var total = await savedTxsTable.GetRecordCount();
				long migrated = 0;
				List<UpdateTransaction> batchTxs = new List<UpdateTransaction>(1000);
				List<NewOutRaw> outputs = new List<NewOutRaw>(1000);
				var batchBlocksTxs = new List<UpdateBlockTransaction>(1000);
				HashSet<(uint256, string)> processedTxs = new HashSet<(uint256, string)>();
				await foreach (var row in savedTxsTable.Enumerate())
				{
					cancellationToken.ThrowIfCancellationRequested();
					migrated++;
					if (migrated % 10_000 == 0)
						logger.LogInformation($"Progress: " + (int)(((double)migrated / (double)total) * 100.0) + "%");
					using (row)
					{
						var trackedSourceHash = Encoding.UTF8.GetString(row.Key.Span).Split('-')[0];
						TrackedTransaction tt = await ToTrackedTransaction(network, legacyRepo, hashToTrackedSource, row);
						if (tt.BlockHash is not null)
						{
							batchBlocksTxs.Add(new UpdateBlockTransaction(tt.TransactionHash.ToString(), tt.BlockHash.ToString()));
						}
						if (processedTxs.Add((tt.TransactionHash, trackedSourceHash)))
						{
							batchTxs.Add(new UpdateTransaction(tt.TransactionHash.ToString(), null, tt.FirstSeen.UtcDateTime));
							foreach (var o in tt.GetReceivedOutputs())
							{
								long value;
								string assetId;
								if (o.Value is Money m)
								{
									value = m.Satoshi;
									assetId = "";
								}
								else if (o.Value is AssetMoney am)
								{
									value = am.Quantity;
									assetId = am.AssetId.ToString();
								}
								else if (o.Value is null)
								{
									value = 1;
									assetId = NBXplorerNetwork.UnknownAssetId;
								}
								else
									continue;
								outputs.Add(new NewOutRaw(
									tt.TransactionHash.ToString(),
									o.Index,
									o.ScriptPubKey.ToHex(),
									value,
									assetId));
							}
						}
						if (batchTxs.Count >= 1000)
						{
							await InsertTransactions(conn, network, batchTxs, batchBlocksTxs);
							await InsertOutsMatches(conn, network, outputs);
							outputs.Clear();
							batchTxs.Clear();
							batchBlocksTxs.Clear();
							processedTxs.Clear();
						}
					}
				}
				await InsertTransactions(conn, network, batchTxs, batchBlocksTxs);
				await InsertOutsMatches(conn, network, outputs);
				processedTxs.Clear();
				outputs.Clear();
				batchTxs.Clear();
				batchBlocksTxs.Clear();
				progress.TrackedTransactionsMigrated = true;
				await SaveProgress(network, conn, progress);
				await tx.CommitAsync();
				logger.LogInformation($"Tracked transactions migrated.");
			}

			if (!progress.TrackedTransactionsInputsMigrated)
			{
				logger.LogInformation($"Migrating tracked transactions inputs...");
				using var tx = await conn.BeginTransactionAsync();
				using var legacyTx = await legacyRepo.engine.OpenTransaction();
				var savedTxsTable = legacyTx.GetTable($"{legacyRepo._Suffix}Transactions");
				var total = await savedTxsTable.GetRecordCount();
				long migrated = 0;
				List<NewInRaw> batch = new List<NewInRaw>(2000);
				List<NewInRaw> filteredBatch = new List<NewInRaw>(10_000);
				HashSet<uint256> processTxs = new HashSet<uint256>();
				await foreach (var row in savedTxsTable.Enumerate())
				{
					cancellationToken.ThrowIfCancellationRequested();
					migrated++;
					if (migrated % 5_000 == 0)
						logger.LogInformation($"Progress: " + (int)(((double)migrated / (double)total) * 100.0) + "%");
					using (row)
					{
						TrackedTransaction tt = await ToTrackedTransaction(network, legacyRepo, hashToTrackedSource, row);
						if (tt.Key.IsPruned)
							continue;
						if (!processTxs.Add(tt.Key.TxId))
							continue;
						foreach (var o in tt.SpentOutpoints)
						{
							batch.Add(new NewInRaw(
								tt.TransactionHash.ToString(),
								tt.IndexOfInput(o),
								o.Hash.ToString(),
								o.N));

							if (batch.Count >= 2000)
							{
								await FindInsMatches(network, conn, batch, filteredBatch);
								processTxs.Clear();
							}
							if (filteredBatch.Count >= 10_000)
							{
								await InsertInsMatches(conn, network, filteredBatch);
							}
						}
					}
				}
				await FindInsMatches(network, conn, batch, filteredBatch);
				await InsertInsMatches(conn, network, filteredBatch);
				await conn.ExecuteAsync("DROP TABLE tmp_mapping_tracked_sources;");
				await UnregisterTypes(conn);
				progress.TrackedTransactionsInputsMigrated = true;
				await SaveProgress(network, conn, progress);
				await tx.CommitAsync();
				logger.LogInformation($"Tracked transactions inputs migrated.");
			}

			// Remove transactions which doesn't have any input or outputs
			await conn.ExecuteAsync(
				"DELETE FROM txs t " +
				"WHERE (t.code, t.tx_id) IN ( " +
				"	SELECT t.code, t.tx_id FROM txs t " +
				"	LEFT JOIN outs o ON t.code=o.code AND t.tx_id=o.tx_id " +
				"	LEFT JOIN ins i ON t.code=i.code AND t.tx_id=i.tx_id " +
				"	WHERE o.tx_id IS NULL AND i.tx_id IS NULL " +
				");");
		}

		private static async Task InsertOutsMatches(System.Data.Common.DbConnection conn, NBXplorerNetwork network, List<NewOutRaw> outputs)
		{
			await conn.ExecuteAsync("INSERT INTO outs SELECT @code, tx_id, idx, script, value, asset_id FROM unnest(@records) ON CONFLICT DO NOTHING;", new
			{
				code = network.CryptoCode,
				records = outputs
			});
		}

		private static async Task InsertInsMatches(System.Data.Common.DbConnection conn, NBXplorerNetwork network, List<NewInRaw> filteredBatch)
		{
			await conn.ExecuteAsync("INSERT INTO ins SELECT @code, tx_id, idx, spent_tx_id, spent_idx FROM unnest(@records) ON CONFLICT DO NOTHING", new
			{
				code = network.CryptoCode,
				records = filteredBatch
			});
			filteredBatch.Clear();
		}

		private static async Task FindInsMatches(NBXplorerNetwork network, System.Data.Common.DbConnection conn, List<NewInRaw> batch, List<NewInRaw> filteredBatch)
		{
			var matchedIns = new HashSet<(string tx_id, long idx)>();
			// We could do in one request, but it is too slow for big installs...
			foreach (var r in await conn.QueryAsync<(string tx_id, long idx)>(
				"SELECT o.tx_id, o.idx FROM outs o " +
				"JOIN unnest(@outpoints) p ON o.code=@code AND o.tx_id=p.tx_id AND o.idx=p.idx",
				new
				{
					code = network.CryptoCode,
					outpoints = batch.Select(i => new DbConnectionHelper.OutpointRaw(i.spent_tx_id, i.spent_idx)).ToArray()
				}))
				matchedIns.Add(r);
			foreach (var i in batch)
			{
				if (matchedIns.Contains((i.spent_tx_id, i.spent_idx)))
					filteredBatch.Add(i);
			}
			batch.Clear();
		}

		private async Task InsertTransactions(System.Data.Common.DbConnection conn, NBXplorerNetwork network, List<UpdateTransaction> batchTxs, List<UpdateBlockTransaction> batchBlocksTxs)
		{
			await conn.ExecuteAsync(
				"INSERT INTO txs AS t (code, tx_id, raw, seen_at) " +
				"SELECT @code, tx_id, raw, MIN(seen_at) FROM unnest(@records) " +
				"GROUP BY tx_id, raw " +
				"ON CONFLICT (code, tx_id) DO UPDATE SET raw=COALESCE(t.raw, EXCLUDED.raw), seen_at=LEAST(t.seen_at, EXCLUDED.seen_at) " +
				"WHERE (t.seen_at > EXCLUDED.seen_at) OR (t.raw IS NULL AND EXCLUDED.raw IS NOT NULL);", new
				{
					code = network.CryptoCode,
					records = batchTxs
				});
			await conn.ExecuteAsync(
				"INSERT INTO blks_txs (code, tx_id, blk_id) " +
				"SELECT @code code, tx_id, r.blk_id  " +
				"FROM unnest(@records) r " +
				"JOIN blks b ON b.code=@code AND b.blk_id=r.blk_id " +
				"ON CONFLICT DO NOTHING;", new
				{
					code = network.CryptoCode,
					records = batchBlocksTxs
				});
		}

		private async Task<IGetBlockHeaders> GetBlockProvider(NBXplorerNetwork network, ILogger logger)
		{
			IGetBlockHeaders getBlocks = null;
			using (var token = new CancellationTokenSource(20_000))
			{
				try
				{
					var rpc = RpcClients.Get(network);
					await RPCArgs.TestRPCAsync(network, rpc, token.Token, logger);
					getBlocks = new RPCGetBlockHeaders(rpc);
					logger.LogInformation($"Getting blocks from RPC...");
				}
				catch (Exception ex)
				{
					logger.LogInformation($"Unable to access the full node for block import, fall back the local chain-slim.dat. ({ex.Message})");
					var suffix = network.CryptoCode == "BTC" ? "" : network.CryptoCode;
					var slimCachePath = Path.Combine(ExplorerConfiguration.DataDir, $"{suffix}chain-slim.dat");
					if (!File.Exists(slimCachePath))
						throw new ConfigException($"Impossible to get the blocks from RPC, nor from {slimCachePath}");

					logger.LogInformation($"Getting blocks from chain-slim.dat...");
					using (var file = new FileStream(slimCachePath, FileMode.Open, FileAccess.Read, FileShare.None, 1024 * 1024))
					{
						var chain = new SlimChain(network.NBitcoinNetwork.GenesisHash, (int)(((double)file.Length / 32.0) * 1.05));
						chain.Load(file);
						getBlocks = new SlimChainGetBlockHeaders(network, chain);
					}
				}
			}

			return getBlocks;
		}

		private static async Task<TrackedTransaction> ToTrackedTransaction(NBXplorerNetwork network, Repository legacyRepo, Dictionary<string, TrackedSource> hashToTrackedSource, IRow row)
		{
			var seg = DBTrieLib.DBTrie.PublicExtensions.GetUnderlyingArraySegment(await row.ReadValue());
			MemoryStream ms = new MemoryStream(seg.Array, seg.Offset, seg.Count);
			BitcoinStream bs = new BitcoinStream(ms, false);
			bs.ConsensusFactory = network.NBitcoinNetwork.Consensus.ConsensusFactory;
			var trackedSerializable = legacyRepo.CreateBitcoinSerializableTrackedTransaction(TrackedTransactionKey.Parse(row.Key.Span));
			trackedSerializable.ReadWrite(bs);
			var trackedSource = hashToTrackedSource[Encoding.UTF8.GetString(row.Key.Span).Split('-')[0]];
			var tt = legacyRepo.ToTrackedTransaction(trackedSerializable, trackedSource);
			return tt;
		}

		private static async Task CreateWalletAndDescriptor(System.Data.Common.DbConnection conn, List<PostgresRepository.WalletKey> walletKeys, List<InsertDescriptor> descriptors)
		{
			await conn.ExecuteAsync("INSERT INTO wallets VALUES (@wid, @metadata::JSONB) ON CONFLICT DO NOTHING;", walletKeys);
			await conn.ExecuteAsync(
				"INSERT INTO descriptors VALUES (@code, @descriptor, @metadata::JSONB) ON CONFLICT DO NOTHING;" +
				"INSERT INTO wallets_descriptors (code, descriptor, wallet_id) VALUES (@code, @descriptor, @wallet_id) ON CONFLICT DO NOTHING", descriptors);
		}

		private static async Task SaveProgress(NBXplorerNetwork network, System.Data.Common.DbConnection conn, MigrationProgress progress)
		{
			await conn.ExecuteAsync(
									"INSERT INTO nbxv1_settings (code, key, data_json) VALUES (@code, 'MigrationProgress', @data::JSONB) " +
									"ON CONFLICT (code, key) DO " +
									"UPDATE SET data_json=EXCLUDED.data_json;", new { code = network.CryptoCode, data = JsonConvert.SerializeObject(progress) });
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			if (started)
				await LegacyRepositoryProvider.StopAsync(cancellationToken);
		}

		interface IGetBlockHeaders
		{
			Task<IList<UpdateBlock>> GetUpdateBlocks(IList<uint256> blockHashes);
		}
		class SlimChainGetBlockHeaders : IGetBlockHeaders
		{
			public SlimChainGetBlockHeaders(NBXplorerNetwork network, SlimChain slimChain)
			{
				Network = network;
				SlimChain = slimChain;
			}

			public NBXplorerNetwork Network { get; }
			public SlimChain SlimChain { get; }

			public Task<IList<UpdateBlock>> GetUpdateBlocks(IList<uint256> blockHashes)
			{
				List<UpdateBlock> update = new List<UpdateBlock>(blockHashes.Count);
				foreach (var hash in blockHashes)
				{
					var b = SlimChain.GetBlock(hash);
					if (b != null)
					{
						update.Add(new UpdateBlock(Network.CryptoCode, b.Hash.ToString(), b.Previous?.ToString(), b.Height));
					}
				}
				return Task.FromResult<IList<UpdateBlock>>(update);
			}
		}
		class RPCGetBlockHeaders : IGetBlockHeaders
		{
			public RPCGetBlockHeaders(RPCClient rpc)
			{
				Rpc = rpc;
			}

			public RPCClient Rpc { get; }

			public async Task<IList<UpdateBlock>> GetUpdateBlocks(IList<uint256> blockHashes)
			{
				var rpc = Rpc.PrepareBatch();
				List<Task<SlimChainedBlock>> gettingHeaders = new List<Task<SlimChainedBlock>>(blockHashes.Count);
				foreach (var blk in blockHashes)
				{
					var b = rpc.GetBlockHeaderAsyncEx(blk);
					gettingHeaders.Add(b);
				}
				await rpc.SendBatchAsync();

				List<UpdateBlock> update = new List<UpdateBlock>(blockHashes.Count);
				foreach (var gh in gettingHeaders)
				{
					var blockHeader = await gh;
					if (blockHeader is not null)
						update.Add(new UpdateBlock(Rpc.Network.NetworkSet.CryptoCode, blockHeader.Hash.ToString(), blockHeader.Previous?.ToString(), blockHeader.Height));
				}
				return update;
			}
		}
	}
}
