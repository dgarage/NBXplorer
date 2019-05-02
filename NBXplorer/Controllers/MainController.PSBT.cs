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
			if (request.ExplicitChangeAddress == null)
			{
				if (request.ReserveChangeAddress) // We will reserve the key only when we are sure we have enough funds
				{
					var derivation = strategy.Derive(0); // We can't take random key, as the length of the scriptPubKey influences fees
					change = (derivation.ScriptPubKey, null);
				}
				else
				{
					var unused = await RepositoryProvider.GetRepository(network).GetUnused(strategy, DerivationFeature.Change, 0, false);
					change = (unused.ScriptPubKey, unused.KeyPath);
				}
			}
			else
			{
				change = (request.ExplicitChangeAddress.ScriptPubKey, null);
			}
			txBuilder.SetChange(change.ScriptPubKey);
			PSBT psbt = null;
			try
			{
				if (request.FeePreference?.ExplicitFeeRate is FeeRate feeRate)
				{
					txBuilder.SendEstimatedFees(feeRate);
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
			if (hasChange && request.ReserveChangeAddress) // We need to reserve an address, so we need to build again the psbt
			{
				var derivation = await repo.GetUnused(strategy, DerivationFeature.Change, 0, true);
				change = (derivation.ScriptPubKey, derivation.KeyPath);
				txBuilder.SetChange(change.ScriptPubKey);
				psbt = txBuilder.BuildPSBT(false);
			}
			
			var tx = psbt.GetOriginalTransaction();
			if (request.Version is uint v)
				tx.Version = v;
			psbt = txBuilder.CreatePSBTFrom(tx, false, SigHash.All);

			var utxosByOutpoint = utxos.GetUnspentUTXOs().ToDictionary(u => u.Outpoint);
			var keyPaths = psbt.Inputs.Select(i => utxosByOutpoint[i.PrevOut].KeyPath).ToHashSet();

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

			if (hasChange && change.KeyPath != null)
			{
				keyPaths.Add(change.KeyPath);
			}

			foreach (var hd in strategy.GetExtPubKeys())
			{
				psbt.AddKeyPath(hd, keyPaths.ToArray());
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
