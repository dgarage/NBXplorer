using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Logging;
using NBXplorer.ModelBinders;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.Controllers
{
	public partial class MainController
	{
		[HttpPost]
		[Route("cryptos/{network}/derivations/{strategy}/psbt/create")]
		public async Task<IActionResult> CreatePSBT(
			[ModelBinder(BinderType = typeof(NetworkModelBinder))]
			NBXplorerNetwork network,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase strategy,
			[FromBody]
			JObject body)
		{
			if (body == null)
				throw new ArgumentNullException(nameof(body));
			CreatePSBTRequest request = ParseJObject<CreatePSBTRequest>(body, network);
			if (strategy == null)
				throw new ArgumentNullException(nameof(strategy));
			var repo = RepositoryProvider.GetRepository(network);
			var utxos = await GetUTXOs(network.CryptoCode, strategy, null);
			var txBuilder = request.Seed is int s ? network.NBitcoinNetwork.CreateTransactionBuilder(s)
												: network.NBitcoinNetwork.CreateTransactionBuilder();

			if (Waiters.GetWaiter(network).NetworkInfo?.GetRelayFee() is FeeRate feeRate)
			{
				txBuilder.StandardTransactionPolicy.MinRelayTxFee = feeRate;
			}

			txBuilder.OptInRBF = request.RBF;
			if (request.LockTime is LockTime lockTime)
			{
				txBuilder.SetLockTime(lockTime);
				txBuilder.OptInRBF = true;
			}

			var availableCoinsByOutpoint = utxos.GetUnspentCoins(request.MinConfirmations).ToDictionary(o => o.Outpoint);
			if (request.IncludeOnlyOutpoints != null)
			{
				var includeOnlyOutpoints = request.IncludeOnlyOutpoints.ToHashSet();
				availableCoinsByOutpoint = availableCoinsByOutpoint.Where(c => includeOnlyOutpoints.Contains(c.Key)).ToDictionary(o => o.Key, o => o.Value);
			}

			if (request.ExcludeOutpoints?.Any() is true)
			{
				var excludedOutpoints = request.ExcludeOutpoints.ToHashSet();
				availableCoinsByOutpoint = availableCoinsByOutpoint.Where(c => !excludedOutpoints.Contains(c.Key)).ToDictionary(o => o.Key, o => o.Value);
			}
			txBuilder.AddCoins(availableCoinsByOutpoint.Values);

			foreach (var dest in request.Destinations)
			{
				if (dest.SweepAll)
				{
					txBuilder.SendAll(dest.Destination);
				}
				else
				{
					txBuilder.Send(dest.Destination, dest.Amount);
					if (dest.SubstractFees)
						txBuilder.SubtractFees();
				}
			}
			(Script ScriptPubKey, KeyPath KeyPath) change = (null, null);
			bool hasChange = false;
			// We first build the transaction with a change which keep the length of the expected change scriptPubKey
			// This allow us to detect if there is a change later in the constructed transaction.
			// This defend against bug which can happen if one of the destination is the same as the expected change
			// This assume that a script with only 0 can't be created from a strategy, nor by passing any data to explicitChangeAddress
			if (request.ExplicitChangeAddress == null)
			{
				// The dummyScriptPubKey is necessary to know the size of the change
				var dummyScriptPubKey = utxos.Unconfirmed.UTXOs.FirstOrDefault()?.ScriptPubKey ??
								 utxos.Confirmed.UTXOs.FirstOrDefault()?.ScriptPubKey ?? strategy.Derive(0).ScriptPubKey;
				change = (Script.FromBytesUnsafe(new byte[dummyScriptPubKey.Length]), null);
			}
			else
			{
				change = (Script.FromBytesUnsafe(new byte[request.ExplicitChangeAddress.ScriptPubKey.Length]), null);
			}
			txBuilder.SetChange(change.ScriptPubKey);
			PSBT psbt = null;
			try
			{
				if (request.FeePreference?.ExplicitFeeRate is FeeRate explicitFeeRate)
				{
					txBuilder.SendEstimatedFees(explicitFeeRate);
				}
				else if (request.FeePreference?.BlockTarget is int blockTarget)
				{
					try
					{
						var rate = await GetFeeRate(blockTarget, network.CryptoCode);
						txBuilder.SendEstimatedFees(rate.FeeRate);
					}
					catch (NBXplorerException e) when (e.Error.Code == "fee-estimation-unavailable" && request.FeePreference?.FallbackFeeRate is FeeRate fallbackFeeRate)
					{
						txBuilder.SendEstimatedFees(fallbackFeeRate);
					}
				}
				else if (request.FeePreference?.ExplicitFee is Money explicitFee)
				{
					txBuilder.SendFees(explicitFee);
				}
				else
				{
					try
					{
						var rate = await GetFeeRate(1, network.CryptoCode);
						txBuilder.SendEstimatedFees(rate.FeeRate);
					}
					catch (NBXplorerException e) when (e.Error.Code == "fee-estimation-unavailable" && request.FeePreference?.FallbackFeeRate is FeeRate fallbackFeeRate)
					{
						txBuilder.SendEstimatedFees(fallbackFeeRate);
					}
				}
				psbt = txBuilder.BuildPSBT(false);
				hasChange = psbt.Outputs.Any(o => o.ScriptPubKey == change.ScriptPubKey);
			}
			catch (NotEnoughFundsException)
			{
				throw new NBXplorerException(new NBXplorerError(400, "not-enough-funds", "Not enough funds for doing this transaction"));
			}
			if (hasChange) // We need to reserve an address, so we need to build again the psbt
			{
				if (request.ExplicitChangeAddress == null)
				{
					var derivation = await repo.GetUnused(strategy, DerivationFeature.Change, 0, request.ReserveChangeAddress);
					change = (derivation.ScriptPubKey, derivation.KeyPath);
				}
				else
				{
					change = (request.ExplicitChangeAddress.ScriptPubKey, null);
				}
				txBuilder.SetChange(change.ScriptPubKey);
				psbt = txBuilder.BuildPSBT(false);
			}

			var tx = psbt.GetOriginalTransaction();
			if (request.Version is uint v)
				tx.Version = v;
			psbt = txBuilder.CreatePSBTFrom(tx, false, SigHash.All);
			var outputsKeyInformations = repo.GetKeyInformations(psbt.Outputs.Where(o => !o.HDKeyPaths.Any()).Select(o => o.ScriptPubKey).ToArray());
			var utxosByOutpoint = utxos.GetUnspentUTXOs().ToDictionary(u => u.Outpoint);

			// Maybe it is a change that we know about, let's search in the DB
			if (hasChange && change.KeyPath == null)
			{
				var keyInfos = await repo.GetKeyInformations(new[] { request.ExplicitChangeAddress.ScriptPubKey });
				if (keyInfos.TryGetValue(request.ExplicitChangeAddress.ScriptPubKey, out var kis))
				{
					var ki = kis.FirstOrDefault(k => k.DerivationStrategy == strategy);
					if (ki != null)
						change = (change.ScriptPubKey, kis.First().KeyPath);
				}
			}


			var pubkeys = strategy.GetExtPubKeys().Select(p => p.AsHDKeyCache()).ToArray();
			var keyPaths = psbt.Inputs.Select(i => utxosByOutpoint[i.PrevOut].KeyPath).ToList();
			if (hasChange && change.KeyPath != null)
			{
				keyPaths.Add(change.KeyPath);
			}
			var fps = new Dictionary<PubKey, HDFingerprint>();
			foreach (var pubkey in pubkeys)
			{
				// We derive everything the fastest way possible on multiple cores
				pubkey.Derive(keyPaths.ToArray());
				fps.TryAdd(pubkey.GetPublicKey(), pubkey.GetPublicKey().GetHDFingerPrint());
			}

			foreach (var input in psbt.Inputs)
			{
				var utxo = utxosByOutpoint[input.PrevOut];
				foreach (var pubkey in pubkeys)
				{
					var childPubKey = pubkey.Derive(utxo.KeyPath);
					NBitcoin.Extensions.TryAdd(input.HDKeyPaths, childPubKey.GetPublicKey(), new RootedKeyPath(fps[pubkey.GetPublicKey()], utxo.KeyPath));
				}
			}
			
			await Task.WhenAll(psbt.Inputs
				.Select(async (input) =>
				{
					if (input.WitnessUtxo == null) // We need previous tx
					{
						var prev = await repo.GetSavedTransactions(input.PrevOut.Hash);
						if (prev?.Any() is true)
							input.NonWitnessUtxo = prev[0].Transaction;
					}
				}).ToArray());

			var outputsKeyInformationsResult = await outputsKeyInformations;
			foreach (var output in psbt.Outputs)
			{
				foreach (var keyInfo in outputsKeyInformationsResult[output.ScriptPubKey].Where(o => o.DerivationStrategy == strategy))
				{
					foreach (var pubkey in pubkeys)
					{
						var childPubKey = pubkey.Derive(keyInfo.KeyPath);
						NBitcoin.Extensions.TryAdd(output.HDKeyPaths, childPubKey.GetPublicKey(), new RootedKeyPath(fps[pubkey.GetPublicKey()], keyInfo.KeyPath));
					}
				}
			}


			if (request.RebaseKeyPaths != null)
			{
				foreach (var rebase in request.RebaseKeyPaths)
				{
					var rootedKeyPath = rebase.GetRootedKeyPath();
					if (rootedKeyPath == null)
						throw new NBXplorerException(new NBXplorerError(400, "missing-parameter", "rebaseKeyPaths[].rootedKeyPath is missing"));
					psbt.RebaseKeyPaths(rebase.AccountKey, rootedKeyPath);
				}
			}

			var resp = new CreatePSBTResponse()
			{
				PSBT = psbt,
				ChangeAddress = hasChange ? change.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork) : null
			};
			return Json(resp, network.JsonSerializerSettings);
		}

		private T ParseJObject<T>(JObject requestObj, NBXplorerNetwork network)
		{
			if (requestObj == null)
				return default;
			return network.Serializer.ToObject<T>(requestObj);
		}
	}
}
