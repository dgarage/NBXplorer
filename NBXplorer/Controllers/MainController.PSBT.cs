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
using System.Threading;
using System.Threading.Tasks;
using NBitcoin.RPC;
using NBXplorer.Analytics;

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
			var txBuilder = request.Seed is int s ? network.NBitcoinNetwork.CreateTransactionBuilder(s)
												: network.NBitcoinNetwork.CreateTransactionBuilder();

			CreatePSBTSuggestions suggestions = null;
			if (!(request.DisableFingerprintRandomization is true) &&
				fingerprintService.GetDistribution(network) is FingerprintDistribution distribution)
			{
				suggestions ??= new CreatePSBTSuggestions();
				var known = new List<(Fingerprint feature, bool value)>();
				if (request.RBF is bool rbf)
					known.Add((Fingerprint.RBF, rbf));
				if (request.DiscourageFeeSniping is bool feeSnipping)
					known.Add((Fingerprint.FeeSniping, feeSnipping));
				if (request.LockTime is LockTime l)
				{
					if (l == LockTime.Zero)
						known.Add((Fingerprint.TimelockZero, true));
				}
				if (request.Version is uint version)
				{
					if (version == 1)
						known.Add((Fingerprint.V1, true));
					if (version == 2)
						known.Add((Fingerprint.V2, true));
				}
				known.Add((Fingerprint.SpendFromMixed, false));
				known.Add((Fingerprint.SequenceMixed, false));
				if (strategy is DirectDerivationStrategy direct)
				{
					if (direct.Segwit)
						known.Add((Fingerprint.SpendFromP2WPKH, true));
					else
						known.Add((Fingerprint.SpendFromP2PKH, true));
				}
				else
				{
					// TODO: What if multisig? For now we consider it p2wpkh
					known.Add((Fingerprint.SpendFromP2SHP2WPKH, true));
				}

				Fingerprint fingerprint = distribution.PickFingerprint(txBuilder.ShuffleRandom);
				try
				{
					fingerprint = distribution.KnowingThat(known.ToArray())
											  .PickFingerprint(txBuilder.ShuffleRandom);
				}
				catch (InvalidOperationException)
				{

				}

				request.RBF ??= fingerprint.HasFlag(Fingerprint.RBF);
				request.DiscourageFeeSniping ??= fingerprint.HasFlag(Fingerprint.FeeSniping);
				if (request.LockTime is null && fingerprint.HasFlag(Fingerprint.TimelockZero))
					request.LockTime = new LockTime(0);
				if (request.Version is null && fingerprint.HasFlag(Fingerprint.V1))
					request.Version = 1;
				if (request.Version is null && fingerprint.HasFlag(Fingerprint.V2))
					request.Version = 2;
				suggestions.ShouldEnforceLowR = fingerprint.HasFlag(Fingerprint.LowR);
			}

			var waiter = Waiters.GetWaiter(network);
			if (waiter.NetworkInfo?.GetRelayFee() is FeeRate feeRate)
			{
				txBuilder.StandardTransactionPolicy.MinRelayTxFee = feeRate;
			}

			txBuilder.OptInRBF = !(request.RBF is false);
			if (request.LockTime is LockTime lockTime)
			{
				txBuilder.SetLockTime(lockTime);
			}
			// Discourage fee sniping.
			//
			// For a large miner the value of the transactions in the best block and
			// the mempool can exceed the cost of deliberately attempting to mine two
			// blocks to orphan the current best block. By setting nLockTime such that
			// only the next block can include the transaction, we discourage this
			// practice as the height restricted and limited blocksize gives miners
			// considering fee sniping fewer options for pulling off this attack.
			//
			// A simple way to think about this is from the wallet's point of view we
			// always want the blockchain to move forward. By setting nLockTime this
			// way we're basically making the statement that we only want this
			// transaction to appear in the next block; we don't want to potentially
			// encourage reorgs by allowing transactions to appear at lower heights
			// than the next block in forks of the best chain.
			//
			// Of course, the subsidy is high enough, and transaction volume low
			// enough, that fee sniping isn't a problem yet, but by implementing a fix
			// now we ensure code won't be written that makes assumptions about
			// nLockTime that preclude a fix later.
			else if (!(request.DiscourageFeeSniping is false))
			{
				if (waiter.State is BitcoinDWaiterState.Ready)
				{
					int blockHeight = ChainProvider.GetChain(network).Height;
					// Secondly occasionally randomly pick a nLockTime even further back, so
					// that transactions that are delayed after signing for whatever reason,
					// e.g. high-latency mix networks and some CoinJoin implementations, have
					// better privacy.
					if (txBuilder.ShuffleRandom.Next(0, 10) == 0)
					{
						blockHeight = Math.Max(0, blockHeight - txBuilder.ShuffleRandom.Next(0, 100));
					}
					txBuilder.SetLockTime(new LockTime(blockHeight));
				}
				else
				{
					txBuilder.SetLockTime(new LockTime(0));
				}
			}
			var utxos = (await GetUTXOs(network.CryptoCode, strategy, null)).As<UTXOChanges>().GetUnspentUTXOs(request.MinConfirmations);
			var availableCoinsByOutpoint = utxos.ToDictionary(o => o.Outpoint);
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

			if (request.MinValue != null)
			{
				availableCoinsByOutpoint = availableCoinsByOutpoint.Where(c => request.MinValue >= (Money)c.Value.Value).ToDictionary(o => o.Key, o => o.Value);
			}

			ICoin[] coins = null;
			if (strategy.GetDerivation().Redeem != null)
			{
				// We need to add the redeem script to the coins
				var hdKeys = strategy.AsHDRedeemScriptPubKey().AsHDKeyCache();
				var arr = availableCoinsByOutpoint.Values.ToArray();
				coins = new ICoin[arr.Length];
				// Can be very intense CPU wise
				Parallel.For(0, coins.Length, i =>
				{
					coins[i] = ((Coin)arr[i].AsCoin()).ToScriptCoin(hdKeys.Derive(arr[i].KeyPath).ScriptPubKey);
				});
			}
			else
			{
				coins = availableCoinsByOutpoint.Values.Select(v => v.AsCoin()).ToArray();
			}
			txBuilder.AddCoins(coins);

			foreach (var dest in request.Destinations)
			{
				if (dest.SweepAll)
				{
					try
					{
						txBuilder.SendAll(dest.Destination);
					}
					catch
					{
						throw new NBXplorerException(new NBXplorerError(400, "not-enough-funds", "You can't sweep funds, because you don't have any."));
					}
				}
				else
				{
					txBuilder.Send(dest.Destination, dest.Amount);
					if (dest.SubstractFees)
					{
						try
						{
							txBuilder.SubtractFees();
						}
						catch
						{
							throw new NBXplorerException(new NBXplorerError(400, "not-enough-funds", "You can't substract fee on this destination, because not enough money was sent to it"));
						}
					}
				}
			}
			(Script ScriptPubKey, KeyPath KeyPath) change = (null, null);
			bool hasChange = false;
			if (request.ExplicitChangeAddress == null)
			{
				var keyInfo = await repo.GetUnused(strategy, DerivationFeature.Change, 0, false);
				change = (keyInfo.ScriptPubKey, keyInfo.KeyPath);
			}
			else
			{
				// The provided explicit change might have a known keyPath, let's change for it
				KeyPath keyPath = null;
				var keyInfos = await repo.GetKeyInformations(new[] { request.ExplicitChangeAddress.ScriptPubKey });
				if (keyInfos.TryGetValue(request.ExplicitChangeAddress.ScriptPubKey, out var kis))
				{
					keyPath = kis.FirstOrDefault(k => k.DerivationStrategy == strategy)?.KeyPath;
				}
				change = (request.ExplicitChangeAddress.ScriptPubKey, keyPath);
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
			// We made sure we can build the PSBT, so now we can reserve the change address if we need to
			if (hasChange && request.ExplicitChangeAddress == null && request.ReserveChangeAddress)
			{
				var derivation = await repo.GetUnused(strategy, DerivationFeature.Change, 0, true);
				// In most of the time, this is the same as previously, so no need to rebuild PSBT
				if (derivation.ScriptPubKey != change.ScriptPubKey)
				{
					change = (derivation.ScriptPubKey, derivation.KeyPath);
					txBuilder.SetChange(change.ScriptPubKey);
					psbt = txBuilder.BuildPSBT(false);
				}
			}

			var tx = psbt.GetOriginalTransaction();
			if (request.Version is uint v)
				tx.Version = v;
			psbt = txBuilder.CreatePSBTFrom(tx, false, SigHash.All);

			var update = new UpdatePSBTRequest()
			{
				DerivationScheme = strategy,
				PSBT = psbt,
				RebaseKeyPaths = request.RebaseKeyPaths,
				AlwaysIncludeNonWitnessUTXO = request.AlwaysIncludeNonWitnessUTXO,
				IncludeGlobalXPub = request.IncludeGlobalXPub
			};
			await UpdatePSBTCore(update, network);
			var resp = new CreatePSBTResponse()
			{
				PSBT = update.PSBT,
				ChangeAddress = hasChange ? change.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork) : null,
				Suggestions = suggestions
			};
			return Json(resp, network.JsonSerializerSettings);
		}

		[HttpPost]
		[Route("cryptos/{network}/psbt/update")]
		public async Task<IActionResult> UpdatePSBT(
			[ModelBinder(BinderType = typeof(NetworkModelBinder))]
			NBXplorerNetwork network,
			[FromBody]
			JObject body)
		{
			var update = ParseJObject<UpdatePSBTRequest>(body, network);
			if (update.PSBT == null)
				throw new NBXplorerException(new NBXplorerError(400, "missing-parameter", "'psbt' is missing"));
			await UpdatePSBTCore(update, network);
			return Json(new UpdatePSBTResponse() { PSBT = update.PSBT }, network.JsonSerializerSettings);
		}

		private async Task UpdatePSBTCore(UpdatePSBTRequest update, NBXplorerNetwork network)
		{
			var repo = RepositoryProvider.GetRepository(network);
			var rpc = Waiters.GetWaiter(network);
			await UpdateUTXO(update, repo, rpc);
			if (update.DerivationScheme is DerivationStrategyBase derivationScheme)
			{
				if (update.IncludeGlobalXPub is true)
				{
					foreach (var extpub in derivationScheme.GetExtPubKeys().Select(e => e.GetWif(network.NBitcoinNetwork)))
					{
						update.PSBT.GlobalXPubs.AddOrReplace(extpub, new RootedKeyPath(extpub, new KeyPath()));
					}
				}
				await UpdateHDKeyPathsWitnessAndRedeem(update, repo);
			}
			if (!update.AlwaysIncludeNonWitnessUTXO)
			{
				foreach (var input in update.PSBT.Inputs)
					input.TrySlimUTXO();
			}


			HashSet<PubKey> rebased = new HashSet<PubKey>();
			if (update.RebaseKeyPaths != null)
			{
				if (update.RebaseKeyPaths.Any(r => r.AccountKey is null))
					throw new NBXplorerException(new NBXplorerError(400, "missing-parameter", "rebaseKeyPaths[].accountKey is missing"));
				foreach (var rebase in update.RebaseKeyPaths.Where(r => rebased.Add(r.AccountKey.GetPublicKey())))
				{
					if (rebase.AccountKeyPath == null)
						throw new NBXplorerException(new NBXplorerError(400, "missing-parameter", "rebaseKeyPaths[].accountKeyPath is missing"));
					update.PSBT.RebaseKeyPaths(rebase.AccountKey, rebase.AccountKeyPath);
				}
			}

			if (update.DerivationScheme is DerivationStrategyBase derivationScheme2)
			{
				var accountKeyPath = await repo.GetMetadata<RootedKeyPath>(
					new DerivationSchemeTrackedSource(derivationScheme2), WellknownMetadataKeys.AccountKeyPath);
				if (accountKeyPath != null)
				{
					foreach (var pubkey in derivationScheme2.GetExtPubKeys().Where(p => rebased.Add(p.PubKey)))
					{
						update.PSBT.RebaseKeyPaths(pubkey, accountKeyPath);
					}
				}
			}
		}

		private static async Task UpdateHDKeyPathsWitnessAndRedeem(UpdatePSBTRequest update, Repository repo)
		{
			var strategy = update.DerivationScheme;
			var pubkeys = strategy.GetExtPubKeys().Select(p => p.AsHDKeyCache()).ToArray();
			var keyInfosByScriptPubKey = new Dictionary<Script, KeyPathInformation>();
			var scriptPubKeys = update.PSBT.Outputs.OfType<PSBTCoin>().Concat(update.PSBT.Inputs)
											.Where(o => !o.HDKeyPaths.Any())
											.Select(o => o.GetCoin()?.ScriptPubKey)
											.Where(s => s != null).ToArray();
			foreach (var keyInfos in (await repo.GetKeyInformations(scriptPubKeys)))
			{
				var keyInfo = keyInfos.Value.FirstOrDefault(k => k.DerivationStrategy == strategy);
				if (keyInfo != null)
				{
					keyInfosByScriptPubKey.TryAdd(keyInfo.ScriptPubKey, keyInfo);
				}
			}

			var fps = new Dictionary<PubKey, HDFingerprint>();
			foreach (var pubkey in pubkeys)
			{
				// We derive everything the fastest way possible on multiple cores
				pubkey.Derive(keyInfosByScriptPubKey.Select(s => s.Value.KeyPath).ToArray());
				fps.TryAdd(pubkey.GetPublicKey(), pubkey.GetPublicKey().GetHDFingerPrint());
			}

			List<Script> redeems = new List<Script>();
			foreach (var c in update.PSBT.Outputs.OfType<PSBTCoin>().Concat(update.PSBT.Inputs.Where(o => !o.IsFinalized())))
			{
				var script = c.GetCoin()?.ScriptPubKey;
				if (script != null &&
					keyInfosByScriptPubKey.TryGetValue(script, out var keyInfo))
				{
					foreach (var pubkey in pubkeys)
					{
						var childPubKey = pubkey.Derive(keyInfo.KeyPath);
						NBitcoin.Extensions.AddOrReplace(c.HDKeyPaths, childPubKey.GetPublicKey(), new RootedKeyPath(fps[pubkey.GetPublicKey()], keyInfo.KeyPath));
						if (keyInfo.Redeem != null && c.RedeemScript is null && c.WitnessScript is null)
							redeems.Add(keyInfo.Redeem);
					}
				}
			}
			if (redeems.Count != 0)
				update.PSBT.AddScripts(redeems.ToArray());
		}


		static bool NeedUTXO(PSBTInput input)
		{
			if (input.IsFinalized())
				return false;
			var needNonWitnessUTXO = !input.PSBT.Network.Consensus.NeverNeedPreviousTxForSigning &&
									!((input.GetSignableCoin() ?? input.GetCoin())?.IsMalleable is false);
			if (needNonWitnessUTXO)
				return input.NonWitnessUtxo == null;
			else
				return input.WitnessUtxo == null && input.NonWitnessUtxo == null;
		}

		private async Task UpdateUTXO(UpdatePSBTRequest update, Repository repo, BitcoinDWaiter rpc)
		{
			if (rpc?.RPCAvailable is true)
			{
				try
				{
					update.PSBT = await rpc.RPC.UTXOUpdatePSBT(update.PSBT);
				}
				// Best effort
				catch (RPCException ex) when (ex.RPCCode == RPCErrorCode.RPC_METHOD_NOT_FOUND)
				{
				}
				catch
				{
				}
			}

			if (update.DerivationScheme is DerivationStrategyBase derivationScheme)
			{
				AnnotatedTransactionCollection txs = null;
				// First, we check for data in our history
				foreach (var input in update.PSBT.Inputs.Where(psbtInput => update.AlwaysIncludeNonWitnessUTXO || NeedUTXO(psbtInput)))
				{
					txs = txs ?? await GetAnnotatedTransactions(repo, ChainProvider.GetChain(repo.Network), new DerivationSchemeTrackedSource(derivationScheme));
					if (txs.GetByTxId(input.PrevOut.Hash) is AnnotatedTransaction tx)
					{
						if (!tx.Record.Key.IsPruned)
						{
							input.NonWitnessUtxo = tx.Record.Transaction;
						}
						else
						{
							input.WitnessUtxo = tx.Record.ReceivedCoins.FirstOrDefault(c => c.Outpoint.N == input.Index)
								?.TxOut;
						}
					}
				}
			}

			// then, we search data in the saved transactions
			await Task.WhenAll(update.PSBT.Inputs
							.Where(psbtInput => update.AlwaysIncludeNonWitnessUTXO || NeedUTXO(psbtInput))
							.Select(async (input) =>
							{
								// If this is not segwit, or we are unsure of it, let's try to grab from our saved transactions
								if (input.NonWitnessUtxo == null)
								{
									var prev = await repo.GetSavedTransactions(input.PrevOut.Hash);
									if (prev.FirstOrDefault() is Repository.SavedTransaction saved)
									{
										input.NonWitnessUtxo = saved.Transaction;
									}
								}
							}).ToArray());

			// finally, we check with rpc's txindex
			if (rpc?.RPCAvailable is true && rpc?.HasTxIndex is true)
			{
				var batch = rpc.RPC.PrepareBatch();
				var getTransactions = Task.WhenAll(update.PSBT.Inputs
					.Where(psbtInput => update.AlwaysIncludeNonWitnessUTXO || NeedUTXO(psbtInput))
					.Where(input => input.NonWitnessUtxo == null)
					.Select(async input =>
				   {
					   var tx = await batch.GetRawTransactionAsync(input.PrevOut.Hash, false);
					   if (tx != null)
					   {
						   input.NonWitnessUtxo = tx;
					   }
				   }).ToArray());
				await batch.SendBatchAsync();
				await getTransactions;
			}
		}

		private T ParseJObject<T>(JObject requestObj, NBXplorerNetwork network)
		{
			if (requestObj == null)
				return default;
			return network.Serializer.ToObject<T>(requestObj);
		}
	}
}
