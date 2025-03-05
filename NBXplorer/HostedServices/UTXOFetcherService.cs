#nullable enable
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer.Backend;
using NBXplorer.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer.HostedServices
{
	public class UTXOFetcherService
	{
		private readonly RepositoryProvider repositoryProvider;

		public Indexers Indexers { get; }
		public NBXplorerNetworkProvider Networks { get; }

		public UTXOFetcherService(
			RepositoryProvider repositoryProvider,
			Indexers indexers,
			NBXplorerNetworkProvider networks)
		{
			this.repositoryProvider = repositoryProvider;
			Indexers = indexers;
			Networks = networks;
		}
		enum UTXORequirement
		{
			WitnessUTXO,
			Unknown,
			NonWitnessUTXO,
			None
		}
		static UTXORequirement RequiredUTXO(PSBTInput input, bool alwaysIncludeNonWitnessUTXO)
		{
			if (input.IsFinalized() || input.NonWitnessUtxo is not null)
				return UTXORequirement.None;

			bool? requireNonWitnessUTXO = null;
			if (alwaysIncludeNonWitnessUTXO)
				requireNonWitnessUTXO = true;
			else if (input.PSBT.Network.Consensus.NeverNeedPreviousTxForSigning)
				requireNonWitnessUTXO = false;
			else if ((input.GetSignableCoin() ?? input.GetCoin())?.IsMalleable is bool isMalleable)
				requireNonWitnessUTXO = isMalleable;

			return requireNonWitnessUTXO switch
			{
				true => UTXORequirement.NonWitnessUTXO,
				null => UTXORequirement.Unknown,
				false when input.WitnessUtxo is null => UTXORequirement.WitnessUTXO,
				_ => UTXORequirement.None
			};
		}
		public async Task<Dictionary<uint256, Transaction>> FetchTransactions(IEnumerable<uint256> txIds, NBXplorerNetwork network)
		{
			var result = new Dictionary<uint256, Transaction>();
			var dummy = Transaction.Create(network.NBitcoinNetwork);
			foreach (var txId in txIds)
				dummy.Inputs.Add(new OutPoint(txId, 0));
			if (dummy.Inputs.Count == 0)
				return result;
			var psbt = PSBT.FromTransaction(dummy, network.NBitcoinNetwork);
			var update = new UpdatePSBTRequest()
			{
				PSBT = psbt,
				AlwaysIncludeNonWitnessUTXO = true
			};
			await UpdateUTXO(update);
			foreach (var input in update.PSBT.Inputs)
			{
				if (input.NonWitnessUtxo is not null)
					result.TryAdd(input.PrevOut.Hash, input.NonWitnessUtxo);
			}
			return result;
		}
		public async Task UpdateUTXO(UpdatePSBTRequest update)
		{
			var network = Networks.GetFromCryptoCode(update.PSBT.Network.NetworkSet.CryptoCode);
			if (network is null)
				return;
			var indexer = Indexers.GetIndexer(network);
			if (indexer is null)
				return;
			var rpc = indexer.GetConnectedClient();
			var repo = repositoryProvider.GetRepository(network);

			var inputWithRequirements = update.PSBT.Inputs.Select(txin => (Input: txin, Requirement: RequiredUTXO(txin, update.AlwaysIncludeNonWitnessUTXO))).ToArray();
			var satifiedInput = inputWithRequirements.Where(r => r.Requirement is UTXORequirement.None).Select(r => r.Input).ToHashSet();
			var needWitnessUTXO = inputWithRequirements.Where(r => r.Requirement is UTXORequirement.WitnessUTXO or UTXORequirement.Unknown);
			var utxosByOutpoints = await repo.GetUTXOs(needWitnessUTXO.Select(r => r.Input.PrevOut).ToHashSet());
			foreach (var r in needWitnessUTXO)
			{
				var input = r.Input;
				if (utxosByOutpoints.TryGetValue(input.PrevOut, out var txout))
				{
					input.WitnessUtxo = txout;
					if (r.Requirement is not UTXORequirement.Unknown ||
						RequiredUTXO(input, update.AlwaysIncludeNonWitnessUTXO) == UTXORequirement.None)
						satifiedInput.Add(input);
				}
			}

			var unSatisfiedInputs = inputWithRequirements
									// No .ToArray() on purpose
									.Where(u => !satifiedInput.Contains(u.Input))
									.Select(u => u.Input);

			var txByHash = await repo.GetSavedTransactions(Repository.SavedTransactionsQuery.GetMany(unSatisfiedInputs.Select(u => u.PrevOut.Hash).ToHashSet()));
			foreach (var input in unSatisfiedInputs)
			{
				if (txByHash.TryGetValue(input.PrevOut.Hash, out var savedTx) && savedTx.Transaction is not null)
				{
					input.NonWitnessUtxo = savedTx.Transaction;
					satifiedInput.Add(input);
					txByHash.Remove(input.PrevOut.Hash);
				}
			}

			var externallyFetchedTxs = new List<Transaction>();
			// We check with rpc's txindex
			if (rpc is not null && indexer.HasTxIndex)
			{
				var txByHashes2 = await rpc.GetRawTransactions(unSatisfiedInputs.Select(c => c.PrevOut.Hash).ToHashSet());
				foreach (var input in unSatisfiedInputs)
				{
					if (txByHashes2.TryGetValue(input.PrevOut.Hash, out var savedTx))
					{
						input.NonWitnessUtxo = savedTx;
						externallyFetchedTxs.Add(savedTx);
						satifiedInput.Add(input);
						txByHash.Remove(input.PrevOut.Hash);
					}
				}
			}

			var txFetchedFromBlocks = await rpc.GetTransactionFromBlocks(txByHash.Where(t => t.Value.BlockHash is not null).Select(t => (t.Value.BlockHash, t.Value.TxId)).ToHashSet());
			foreach (var input in unSatisfiedInputs)
			{
				if (txFetchedFromBlocks.TryGetValue(input.PrevOut.Hash, out var tx))
				{
					input.NonWitnessUtxo = tx;
					externallyFetchedTxs.Add(tx);
					satifiedInput.Add(input);
				}
			}

			if (rpc is not null && unSatisfiedInputs.Any())
			{
				try
				{
					update.PSBT = await rpc.UTXOUpdatePSBT(update.PSBT);
				}
				// Best effort
				catch (RPCException ex) when (ex.RPCCode == RPCErrorCode.RPC_METHOD_NOT_FOUND)
				{
				}
				catch
				{
				}
			}

			// Slim the UTXO if we can. We don't need to check AlwaysIncludeNonWitnessUTXO here. The requirement takes account of it.
			foreach (var r in inputWithRequirements)
			{
				if (r.Requirement is UTXORequirement.WitnessUTXO or UTXORequirement.Unknown && r.Input.NonWitnessUtxo is not null)
					r.Input.TrySlimUTXO();
				if (r.Input.NonWitnessUtxo is not null)
					r.Input.WitnessUtxo = null;
			}

			await repo.SaveTransactionRaw(externallyFetchedTxs);
		}
	}
}
