﻿using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer.Backends;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System;
using System.Threading.Tasks;

namespace NBXplorer.Controllers
{
	public partial class ControllerBase : Controller
	{
		public ControllerBase(
			NBXplorerNetworkProvider networkProvider,
			IRPCClients rpcClients,
			IRepositoryProvider repositoryProvider,
			IIndexers indexers)
		{
			NetworkProvider = networkProvider;
			RPCClients = rpcClients;
			RepositoryProvider = repositoryProvider;
			Indexers = indexers;
		}

		public NBXplorerNetworkProvider NetworkProvider { get; }
		public IRPCClients RPCClients { get; }
		public IRepositoryProvider RepositoryProvider { get; }
		public IIndexers Indexers { get; }

		internal static TrackedSource GetTrackedSource(DerivationStrategyBase derivationScheme, BitcoinAddress address)
		{
			TrackedSource trackedSource = null;
			if (address != null)
				trackedSource = new AddressTrackedSource(address);
			if (derivationScheme != null)
				trackedSource = new DerivationSchemeTrackedSource(derivationScheme);
			return trackedSource;
		}
		internal NBXplorerNetwork GetNetwork(string cryptoCode, bool checkRPC)
		{
			if (cryptoCode == null)
				throw new ArgumentNullException(nameof(cryptoCode));
			cryptoCode = cryptoCode.ToUpperInvariant();
			var network = NetworkProvider.GetFromCryptoCode(cryptoCode);
			if (network == null)
				throw new NBXplorerException(new NBXplorerError(404, "cryptoCode-not-supported", $"{cryptoCode} is not supported"));

			if (checkRPC)
			{
				var rpc = GetAvailableRPC(network);
				if (rpc is null || rpc.Capabilities == null)
					throw new NBXplorerError(400, "rpc-unavailable", $"The RPC interface is currently not available.").AsException();
			}
			return network;
		}
		protected RPCClient GetAvailableRPC(NBXplorerNetwork network)
		{
			return Indexers.GetIndexer(network)?.GetConnectedClient();
		}
	}
}
