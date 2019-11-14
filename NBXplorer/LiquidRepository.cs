using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DBriize;
using NBitcoin;
using NBitcoin.Altcoins.Elements;
using NBitcoin.RPC;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;

namespace NBXplorer
{
	public class LiquidRepository : Repository
	{
		internal LiquidRepository(DBriizeEngine engine, NBXplorerNetwork network, KeyPathTemplates keyPathTemplates,
			RPCClient rpcClient) : base(engine, network, keyPathTemplates, rpcClient)
		{
		}

		protected override async Task<Transaction> GetTransaction(RPCClient rpcClient, Transaction tx,
			KeyPathInformation keyInfo)
		{

			var result = tx;
			if (keyInfo.Address is BitcoinBlindedAddress bitcoinBlindedAddress && tx is ElementsTransaction elementsTransaction)
			{

				var privateKey =
					NBXplorerNetworkProvider.LiquidNBXplorerNetwork.GenerateBlindingKey(
						keyInfo.DerivationStrategy, keyInfo.KeyPath);
				result =  await rpcClient.UnblindTransaction(new List<UnblindTransactionBlindingAddressKey>()
					{
						new UnblindTransactionBlindingAddressKey()
						{
							Address = bitcoinBlindedAddress,
							BlindingKey = privateKey
						}
					},
					elementsTransaction, Network.NBitcoinNetwork);
			}
			else
			{
				result = await base.GetTransaction(rpcClient, tx, keyInfo);
			}
			
			//if there is at least one matching valid(aka unblinded) output, we can pass it along. 
			if (result.Outputs.Any(txout =>
				txout.ScriptPubKey == keyInfo.ScriptPubKey && txout is ElementsTxOut elementsTxOut &&
				elementsTxOut.Value != null))
			{
				return result;
			}

			return null;
		}
	}
}