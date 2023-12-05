using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer.Backends;
using NBXplorer.Models;

namespace NBXplorer.Controllers;

[ModelBinder<TrackedSourceContextModelBinder>]
public class TrackedSourceContext
{
	public TrackedSource TrackedSource { get; set; }
	public NBXplorerNetwork Network { get; set; }
	public RPCClient RpcClient { get; set; }
	public IIndexer Indexer { get; set; }
	public IRepository Repository { get; set; }

	public class TrackedSourceContextRequirementAttribute : Attribute
	{
		public bool RequireRpc { get; }
		public bool RequireTrackedSource { get; }
		public bool DisallowTrackedSource { get; }
		public Type[] AllowedTrackedSourceTypes { get; }

		public TrackedSourceContextRequirementAttribute(bool requireRPC = false, bool requireTrackedSource = true, bool disallowTrackedSource = false, params Type[] allowedTrackedSourceTypes)
		{
			RequireRpc = requireRPC;
			RequireTrackedSource = requireTrackedSource;
			DisallowTrackedSource = disallowTrackedSource;
			AllowedTrackedSourceTypes = allowedTrackedSourceTypes;

		}
	}

	public class TrackedSourceContextModelBinder : IModelBinder
	{
		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			var cryptoCode = bindingContext.ValueProvider.GetValue("cryptoCode").FirstValue?.ToUpperInvariant();

			if (cryptoCode == null)
				throw new ArgumentNullException(nameof(cryptoCode));

			var addressValue = bindingContext.ValueProvider.GetValue("address").FirstValue;
			var derivationSchemeValue = bindingContext.ValueProvider.GetValue("derivationScheme").FirstValue;
			derivationSchemeValue ??= bindingContext.ValueProvider.GetValue("extPubKey").FirstValue;
			var walletIdValue = bindingContext.ValueProvider.GetValue("walletId").FirstValue;
			var trackedSourceValue = bindingContext.ValueProvider.GetValue("trackedSource").FirstValue;

			var networkProvider = bindingContext.HttpContext.RequestServices.GetService<NBXplorerNetworkProvider>();
			var indexers = bindingContext.HttpContext.RequestServices.GetService<IIndexers>();
			var repositoryProvider = bindingContext.HttpContext.RequestServices.GetService<IRepositoryProvider>();

			var network = networkProvider.GetFromCryptoCode(cryptoCode);

			var indexer = network is null ? null : indexers.GetIndexer(network);
			if (network is null || indexer is null)
			{
				throw new NBXplorerException(new NBXplorerError(404, "cryptoCode-not-supported",
					$"{cryptoCode} is not supported"));
			}

			var requirements = ((ControllerActionDescriptor)bindingContext.ActionContext.ActionDescriptor)
				.MethodInfo.GetCustomAttributes<TrackedSourceContextRequirementAttribute>().FirstOrDefault();


			var rpcClient = indexer.GetConnectedClient();
			if (rpcClient?.Capabilities == null)
			{
				rpcClient = null;
			}

			if (requirements?.RequireRpc is true && rpcClient is null)
			{
				ThrowRpcUnavailableException();
			}

			var ts = GetTrackedSource(derivationSchemeValue, addressValue, walletIdValue,
				trackedSourceValue,
				network);
			if (ts is null && requirements?.RequireTrackedSource is true)
			{

				throw new NBXplorerException(new NBXplorerError(400, "tracked-source-required",
					$"A tracked source is required for this endpoint."));
			}
			if (ts is not null && requirements?.DisallowTrackedSource is true)
			{
				throw new NBXplorerException(new NBXplorerError(400, "tracked-source-unwanted",
					$"This endpoint does not tracked sources.."));
			}
			if (ts is not null && requirements?.AllowedTrackedSourceTypes?.Any() is true && !requirements.AllowedTrackedSourceTypes.Any(t => t.IsInstanceOfType(ts)))
			{
				throw new NBXplorerException(new NBXplorerError(400, "tracked-source-invalid",
					$"The tracked source provided is not valid for this endpoint."));
			}

			bindingContext.Result = ModelBindingResult.Success(new TrackedSourceContext()
			{
				Indexer = indexer,
				Network = network,
				TrackedSource = ts,
				RpcClient = rpcClient,
				Repository = repositoryProvider.GetRepository(network)
			});
			return Task.CompletedTask;
		}
		public static void ThrowRpcUnavailableException()
		{
			throw new NBXplorerError(400, "rpc-unavailable", $"The RPC interface is currently not available.").AsException();
		}

		public static TrackedSource GetTrackedSource(string derivationScheme, string address, string walletId,
			string trackedSource, NBXplorerNetwork network)
		{
			if (trackedSource != null)
				return TrackedSource.Parse(trackedSource, network);
			if (address != null)
				return new AddressTrackedSource(BitcoinAddress.Create(address, network.NBitcoinNetwork));
			if (derivationScheme != null)
				return new DerivationSchemeTrackedSource(network.DerivationStrategyFactory.Parse(derivationScheme));
			if (walletId != null)
				return new WalletTrackedSource(walletId);
			return null;
		}
	}

}