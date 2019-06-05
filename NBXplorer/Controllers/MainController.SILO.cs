using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.ModelBinders;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer.Controllers
{
	public partial class MainController
	{

		[HttpPost]
		[Route("cryptos/{cryptoCode}/locks/{unlockId}/cancel")]
		public async Task<IActionResult> UnlockUTXOs(string cryptoCode, string unlockId)
		{
			var network = GetNetwork(cryptoCode, false);
			var repo = RepositoryProvider.GetRepository(network);
			if (await repo.CancelMatches(unlockId))
				return Ok();
			else
				return NotFound("unlockid-not-found");
		}


		[HttpPost]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/transactions")]
		public async Task<IActionResult> LockUTXOs(string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme,
			[FromBody] LockUTXOsRequest request,
			CancellationToken cancellation = default)
		{
			if (derivationScheme == null)
				throw new ArgumentNullException(nameof(derivationScheme));
			var network = GetNetwork(cryptoCode, false);
			var trackedSource = GetTrackedSource(derivationScheme, null, network.NBitcoinNetwork);

			var repo = RepositoryProvider.GetRepository(network);

			Repository.DBLock walletLock = null;
			try
			{
				walletLock = await repo.TakeWalletLock(derivationScheme, cancellation);

				var psbtRequest = new CreatePSBTRequest();

				foreach (var destination in request.GetDestinations())
				{
					psbtRequest.Destinations.Add(destination.ToPSBTDestination(network));
				}
				if (psbtRequest.Destinations.Count == 0)
					throw new NBXplorerException(new NBXplorerError(400, "invalid-destination", "'destination' or 'destinations' not specified"));
				psbtRequest.FeePreference = new FeePreference() { ExplicitFeeRate = request.FeeRate, BlockTarget = 6 };
				psbtRequest.ReserveChangeAddress = true;

				var psbtActionResult = await this.CreatePSBT(network, derivationScheme, network.Serializer.ToJObject(psbtRequest));
				var psbt = ((psbtActionResult as JsonResult)?.Value as CreatePSBTResponse)?.PSBT;
				var changeAddress = ((psbtActionResult as JsonResult)?.Value as CreatePSBTResponse)?.ChangeAddress;
				if (psbt == null)
					return psbtActionResult;

				LockUTXOsResponse result = new LockUTXOsResponse();
				psbt.TryGetFee(out var fee);
				result.Fee = fee;

				result.SpentCoins = psbt.Inputs.Select(i => new LockUTXOsResponse.SpentCoin()
				{
					KeyPath = i.HDKeyPaths.First().Value.KeyPath,
					Outpoint = i.PrevOut,
					Value = i.GetTxOut().Value
				})
				.ToArray();

				var tx = psbt.GetGlobalTransaction();
				foreach (var input in tx.Inputs)
				{
					var psbtInput = psbt.Inputs.FindIndexedInput(input.PrevOut);
					var coin = psbtInput.GetSignableCoin() ?? psbtInput.GetCoin();
					if (coin is ScriptCoin scriptCoin)
					{
						if (scriptCoin.RedeemType == RedeemType.P2SH)
						{
							input.ScriptSig = new Script(Op.GetPushOp(scriptCoin.Redeem.ToBytes()));
						}
						else if (scriptCoin.RedeemType == RedeemType.WitnessV0)
						{
							input.WitScript = new Script(Op.GetPushOp(scriptCoin.Redeem.ToBytes()));
							if (scriptCoin.IsP2SH)
								input.ScriptSig = new Script(Op.GetPushOp(scriptCoin.Redeem.WitHash.ScriptPubKey.ToBytes()));
						}
					}
				}
				result.Transaction = tx.Clone();

				if (changeAddress != null)
				{
					var changeOutput = psbt.Outputs.Where(c => c.ScriptPubKey == changeAddress.ScriptPubKey).FirstOrDefault();
					if (changeOutput != null)
					{
						result.ChangeInformation = new LockUTXOsResponse.ChangeInfo()
						{
							KeyPath = changeOutput.HDKeyPaths.First().Value.KeyPath,
							Value = changeOutput.Value
						};
					}
				}
				tx.MarkLockUTXO();

				TrackedTransactionKey trackedTransactionKey = new TrackedTransactionKey(tx.GetHash(), null, false);
				TrackedTransaction trackedTransaction = new TrackedTransaction(trackedTransactionKey, trackedSource, tx, new Dictionary<Script, KeyPath>());
				foreach (var c in psbt.Inputs.OfType<PSBTCoin>().Concat(psbt.Outputs).Where(c => c.HDKeyPaths.Any()))
				{
					trackedTransaction.KnownKeyPathMapping.TryAdd(c.GetCoin().ScriptPubKey, c.HDKeyPaths.First().Value.KeyPath);
				}
				trackedTransaction.KnownKeyPathMappingUpdated();
				await repo.SaveMatches(new[] { trackedTransaction });
				result.UnlockId = trackedTransaction.UnlockId;
				return Json(result);
			}
			catch (NotEnoughFundsException)
			{
				throw new NBXplorerException(new NBXplorerError(400, "not-enough-funds", "Not enough funds for doing this transaction"));
			}
			finally
			{
				if (walletLock != null)
					await walletLock.ReleaseLock();
			}
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/balances")]
		public async Task<GetBalanceResponse> GetBalance(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(ModelBinders.DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme)
		{
			var network = GetNetwork(cryptoCode, false);
			var chain = ChainProvider.GetChain(network);
			var repo = RepositoryProvider.GetRepository(network);
			var trackedSource = GetTrackedSource(derivationScheme, null, network.NBitcoinNetwork);

			GetBalanceResponse response = new GetBalanceResponse();

			var transactions = await GetAnnotatedTransactions(repo, chain, trackedSource);

			response.Spendable = transactions
									.UnconfirmedState
									.UTXOByOutpoint
									.Where(utxo => !transactions.UnconfirmedStateWithLocks.SpentUTXOs.Contains(utxo.Value.Outpoint))
									.Select(u => u.Value.Amount).Sum();
			response.Total = transactions.UnconfirmedStateWithLocks.UTXOByOutpoint.Select(u => u.Value.Amount).Sum();

			return response;
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/derivations/{strategy}/addresses")]
		public async Task<IActionResult> GetKeyInformationFromKeyPath(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase strategy,
			[ModelBinder(BinderType = typeof(KeyPathModelBinder))]
			KeyPath keyPath)
		{
			if (strategy == null)
				throw new ArgumentNullException(nameof(strategy));
			var network = GetNetwork(cryptoCode, false);

			// Should we refactor this to send an array with only one entry?
			// This is breaking change to clients
			if (keyPath != null)
			{
				var information = strategy.Derive(keyPath);
				return Json(new KeyPathInformation()
				{
					Address = information.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork).ToString(),
					DerivationStrategy = strategy,
					KeyPath = keyPath,
					ScriptPubKey = information.ScriptPubKey,
					Redeem = information.Redeem,
					Feature = DerivationStrategyBase.GetFeature(keyPath)
				});
			}
			else
			{
				var repository = RepositoryProvider.GetRepository(network);
				var highest = await repository.GetHighestGenerated(strategy);
				List<KeyPathInformation> keyPathInformations = new List<KeyPathInformation>();
				foreach (var kv in highest)
				{
					var accountLevel = strategy.GetLineFor(kv.Key);
					for (int i = 0; i < kv.Value + 1; i++)
					{
						var derivation = accountLevel.Derive((uint)i);
						keyPathInformations.Add(new KeyPathInformation()
						{
							Address = derivation.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork).ToString(),
							DerivationStrategy = strategy,
							KeyPath = keyPath,
							ScriptPubKey = derivation.ScriptPubKey,
							Redeem = derivation.Redeem,
							Feature = kv.Key
						});
					}
				}
				return Json(keyPathInformations);
			}
		}
	}
}
