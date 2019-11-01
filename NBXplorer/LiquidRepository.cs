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
			if (keyInfo is NBXplorerNetworkProvider.LiquidKeyPathInformation liquidKeyPathInformation &&
			    liquidKeyPathInformation.BlindingKey != null && tx is ElementsTransaction elementsTransaction)
			{
				result =  await rpcClient.UnblindTransaction(new List<UnblindTransactionBlindingAddressKey>()
					{
						new UnblindTransactionBlindingAddressKey()
						{
							Address = new BitcoinBlindedAddress(liquidKeyPathInformation.Address,
								Network.NBitcoinNetwork),
							BlindingKey = liquidKeyPathInformation.BlindingKey
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

		protected override KeyPathInformation GetKeyPathInformation(Derivation derivation, TrackedSource trackedSource,
			DerivationFeature derivationFeature, KeyPath keyPath)
		{
			var result = base.GetKeyPathInformation(derivation, trackedSource, derivationFeature, keyPath);
			return new NBXplorerNetworkProvider.LiquidKeyPathInformation(result,
				derivation is NBXplorerNetworkProvider.LiquidDerivation liquidDerivation
					? liquidDerivation.BlindingKey
					: null);
		}

		protected override KeyPathInformation GetKeyPathInformation(IDestination derivation)
		{
			if (derivation is BitcoinBlindedAddress bitcoinBlindedAddress)
			{
				throw new NotSupportedException("Individual blinded address tracking is not currently supported");
			}

			return base.GetKeyPathInformation(derivation);
		}

		protected override KeyPathInformation GetKeyPathInformation(byte[] value)
		{
			return ToObject<NBXplorerNetworkProvider.LiquidKeyPathInformation>(value)
				.AddAddress(Network.NBitcoinNetwork, out _);
		}
	}
}