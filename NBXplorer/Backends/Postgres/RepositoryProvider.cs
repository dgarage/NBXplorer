using NBitcoin;
using Dapper;
using NBXplorer.Configuration;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBXplorer.Logging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Microsoft.Extensions.Hosting;


namespace NBXplorer.Backends
{
	public class RepositoryProvider : IHostedService
	{
		Dictionary<string, Repository> _Repositories = new Dictionary<string, Repository>();
		ExplorerConfiguration _Configuration;

		public Task StartCompletion => Task.CompletedTask;

		public NBXplorerNetworkProvider Networks { get; }
		public DbConnectionFactory ConnectionFactory { get; }
		public KeyPathTemplates KeyPathTemplates { get; }

		public RepositoryProvider(NBXplorerNetworkProvider networks,
			ExplorerConfiguration configuration,
			DbConnectionFactory connectionFactory,
			KeyPathTemplates keyPathTemplates)
		{
			Networks = networks;
			_Configuration = configuration;
			ConnectionFactory = connectionFactory;
			KeyPathTemplates = keyPathTemplates;
		}

		public Repository GetRepository(string cryptoCode)
		{
			_Repositories.TryGetValue(cryptoCode.ToUpperInvariant(), out Repository repository);
			return repository;
		}
		public Repository GetRepository(NBXplorerNetwork network)
		{
			return GetRepository(network.CryptoCode);
		}

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			foreach (var net in Networks.GetAll())
			{
				var settings = GetChainSetting(net);
				if (settings != null)
				{
					var repo = new Repository(ConnectionFactory, net, KeyPathTemplates, settings.RPC, _Configuration);
					repo.MaxPoolSize = _Configuration.MaxGapSize;
					repo.MinPoolSize = _Configuration.MinGapSize;
					repo.MinUtxoValue = settings.MinUtxoValue;
					_Repositories.Add(net.CryptoCode, repo);
				}
			}
			foreach (var repo in _Repositories.Select(kv => kv.Value))
			{
				if (GetChainSetting(repo.Network) is ChainConfiguration chainConf &&
				chainConf.Rescan &&
				(chainConf.RescanIfTimeBefore is null || chainConf.RescanIfTimeBefore.Value >= DateTimeOffset.UtcNow))
				{
					Logs.Configuration.LogInformation($"{repo.Network.CryptoCode}: Rescanning the chain...");
					await repo.SetIndexProgress(null);
				}
			}
			if (_Configuration.TrimEvents > 0)
			{
				Logs.Explorer.LogInformation("Trimming the event table if needed...");
				int trimmed = 0;
				foreach (var repo in _Repositories.Select(kv => kv.Value))
				{
					if (GetChainSetting(repo.Network) is ChainConfiguration chainConf)
					{
						trimmed += await repo.TrimmingEvents(_Configuration.TrimEvents, cancellationToken);
					}
				}
				if (trimmed != 0)
					Logs.Explorer.LogInformation($"Trimmed {trimmed} events in total...");
			}
		}

		public async Task<string> GetMigrationId()
		{
			await using var conn = await ConnectionFactory.CreateConnection();
			var v = await conn.ExecuteScalarAsync<string>("SELECT data_json FROM nbxv1_settings WHERE code='' AND key='MigrationId'");
			return v is null ? null : v[1..^1];
		}
		public async Task SetMigrationId(uint256 newId)
		{
			await using var conn = await ConnectionFactory.CreateConnection();
			await conn.ExecuteScalarAsync<string>(
				"INSERT INTO nbxv1_settings AS ns (code, key, data_json) VALUES ('', 'MigrationId', @data::JSONB) " +
				"RETURNING data_json", new { data = $"\"{newId}\"" });
		}

		private ChainConfiguration GetChainSetting(NBXplorerNetwork net)
		{
			return _Configuration.ChainConfigurations.FirstOrDefault(c => c.CryptoCode == net.CryptoCode);
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}
	}

	public class LegacyDescriptorMetadata
	{
		public const string TypeName = "NBXv1-Derivation";
		[JsonProperty]
		public string Type { get; set; }
		[JsonProperty]
		public DerivationStrategyBase Derivation { get; set; }
		[JsonProperty]
		public KeyPathTemplate KeyPathTemplate { get; set; }
		[JsonConverter(typeof(StringEnumConverter))]
		public DerivationFeature Feature { get; set; }
	}
}
