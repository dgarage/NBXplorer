using System.Collections.Generic;
using System.Threading.Tasks;
using DBriize;
using NBitcoin;
using NBitcoin.Altcoins.Elements;
using NBitcoin.RPC;
using NBXplorer.Models;

namespace NBXplorer
{
	public class LiquidRepository : Repository
	{
		private readonly RPCClient _rpcClient;

		internal LiquidRepository(DBriizeEngine engine, NBXplorerNetwork network, KeyPathTemplates keyPathTemplates,
			RPCClient rpcClient) : base(engine, network, keyPathTemplates)
		{
			_rpcClient = rpcClient;
		}

		protected override async Task<Transaction> GetTransaction(Transaction tx,
			KeyPathInformation keyInfo)
		{
			if (!(keyInfo.Address is BitcoinBlindedAddress bitcoinBlindedAddress) ||
			    !(tx is ElementsTransaction elementsTransaction)) return await base.GetTransaction(tx, keyInfo);

			var privateKey =
				NBXplorerNetworkProvider.LiquidNBXplorerNetwork.GenerateBlindingKey(
					keyInfo.DerivationStrategy, keyInfo.KeyPath);
			return await _rpcClient.UnblindTransaction(new List<UnblindTransactionBlindingAddressKey>()
				{
					new UnblindTransactionBlindingAddressKey()
					{
						Address = bitcoinBlindedAddress,
						BlindingKey = privateKey
					}
				},
				elementsTransaction, Network.NBitcoinNetwork);
		}
	}
}