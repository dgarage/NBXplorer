using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.ModelBinders;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin.RPC;
using NBXplorer.Analytics;
using NBXplorer.Backend;
using NBitcoin.WalletPolicies;

namespace NBXplorer.Controllers
{
	public partial class MainController
	{
		[HttpPost]
		[Route($"{CommonRoutes.DerivationEndpoint}/psbt/create")]
		[TrackedSourceContext.TrackedSourceContextRequirement(allowedTrackedSourceTypes: typeof(DerivationSchemeTrackedSource))]
		public async Task<IActionResult> CreatePSBT(
			TrackedSourceContext trackedSourceContext,
			[FromBody]
			JObject body)
		{
			if (body == null)
				throw new ArgumentNullException(nameof(body));
			var network = trackedSourceContext.Network;
			CreatePSBTRequest request = network.ParseJObject<CreatePSBTRequest>(body);

			var psbtVersion = request.PSBTVersion switch
			{
				2 => PSBTVersion.PSBTv2,
				_ => PSBTVersion.PSBTv0
			};

			var repo = RepositoryProvider.GetRepository(network);
			var txBuilder = request.Seed is int s ? network.NBitcoinNetwork.CreateTransactionBuilder(s)
												: network.NBitcoinNetwork.CreateTransactionBuilder();
			var strategy = ((DerivationSchemeTrackedSource) trackedSourceContext.TrackedSource).DerivationStrategy;
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

			var indexer = Indexers.GetIndexer(network);
			if (indexer.NetworkInfo?.GetRelayFee() is FeeRate feeRate)
			{
				txBuilder.StandardTransactionPolicy.MinRelayTxFee = feeRate;
			}

			if(request.MergeOutputs is bool mergeOutputs)
			{
				txBuilder.MergeOutputs = mergeOutputs;
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
				if (indexer.State is BitcoinDWaiterState.Ready)
				{
					int blockHeight = (await repo.GetTip()).Height;
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
			var utxoChanges = (await CommonRoutesController.GetUTXOs(trackedSourceContext)).As<UTXOChanges>();
			var utxos = utxoChanges.GetUnspentUTXOs(request.MinConfirmations);
			var availableCoinsByOutpoint = utxos.ToDictionary(o => o.Outpoint);
			if (request.IncludeOnlyOutpoints != null)
			{
				var includeOnlyOutpoints = request.IncludeOnlyOutpoints.ToHashSet();
				// IncludeOnlyOutpoints has the ability to include UTXOs that are spent by unconfirmed UTXOs
				{
					// We need to add the unconfirmed utxos that are spent by unconfirmed utxos
					foreach (var u in utxoChanges.SpentUnconfirmed)
					{
						availableCoinsByOutpoint.TryAdd(u.Outpoint, u);
					}
					// We need to add the confirmed utxos that are spent by unconfirmed utxos
					var spentConfs = utxoChanges.Unconfirmed.SpentOutpoints.Select(o => o.Hash).ToHashSet();
					foreach (var u in utxoChanges.Confirmed.UTXOs)
					{
						if (spentConfs.Contains(u.Outpoint.Hash))
							availableCoinsByOutpoint.TryAdd(u.Outpoint, u);
					}
				}
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

			var unconfUtxos = utxos.Where(u => u.Confirmations is 0).ToList();


			// We remove unconf utxos with too many ancestors, as it will result in a transaction
			// that can't be broadcasted.
			// We do only for BTC, as this isn't a shitcoin issue.
			if (network.CryptoCode == "BTC" && unconfUtxos.Count > 0 && request.MinConfirmations == 0)
			{
				HashSet<uint256> requestedTxs = new HashSet<uint256>();
				var rpc = trackedSourceContext.RpcClient;
				rpc = rpc.PrepareBatch();
				var mempoolEntries = 
					unconfUtxos
					.Where(u => requestedTxs.Add(u.Outpoint.Hash))
					.Select(u => (u.Outpoint.Hash, rpc.SendCommandAsync(new RPCRequest("getmempoolentry", new[] { u.Outpoint.Hash.ToString() }) { ThrowIfRPCError = false }))).ToList();
				await rpc.SendBatchAsync();
				foreach (var result in mempoolEntries)
				{
					var mempoolEntryResponse = await result.Item2;
					if (mempoolEntryResponse.Error is not null)
						continue;
					var ancestorCount = mempoolEntryResponse.Result["ancestorcount"].Value<int>();
					// We hardcode the default -limitancestorcount to 25 here since we can't query it via RPC
					if (ancestorCount >= 25)
					{
						var hash = result.Item1;
						foreach (var u in unconfUtxos.Where(u => u.Outpoint.Hash == hash))
						{
							availableCoinsByOutpoint.Remove(u.Outpoint);
						}
					}
				}
			}

			ICoin[] coins = null;
			if (strategy.GetLineFor(KeyPathTemplates, DerivationFeature.Deposit).Derive(0).Redeem != null)
			{
				// We need to add the redeem script to the coins
				var arr = availableCoinsByOutpoint.Values.ToArray();
				coins = new ICoin[arr.Length];
				// Can be very intense CPU wise
				Parallel.For(0, coins.Length, i =>
				{
					coins[i] = arr[i].AsCoin(strategy);
				});
			}
			else
			{
				coins = availableCoinsByOutpoint.Values.Select(v => v.AsCoin()).ToArray();
			}
			txBuilder.AddCoins(coins);
			bool sweepAll = false;
			foreach (var dest in request.Destinations)
			{
				if (dest.Amount is not null && dest.Amount < Money.Zero)
					throw new NBXplorerException(new NBXplorerError(400, "output-too-small", "Amount can't be negative", reason: OutputTooSmallException.ErrorType.TooSmallBeforeSubtractedFee.ToString()));
				if (dest.Destination is null)
					throw new NBXplorerException(new NBXplorerError(400, "missing-parameter", "`destination` is missing"));
				if (dest.SweepAll)
				{
					sweepAll = true;
					txBuilder.SendAll(dest.Destination.ScriptPubKey);
				}
				else
				{
					txBuilder.Send(dest.Destination.ScriptPubKey, dest.Amount);
					if (dest.SubstractFees)
					{
						try
						{
							txBuilder.SubtractFees();
						}
						catch (InvalidOperationException)
						{
							throw new NBXplorerException(new NBXplorerError(400, "output-too-small", "You can't substract fee on this destination, because not enough money was sent to it."));
						}
					}
				}
			}
			Script change = null;
			bool hasChange = false;
			if (request.ExplicitChangeAddress == null)
			{
				var keyInfo = (await GetUnusedAddress(trackedSourceContext, DerivationFeature.Change, autoTrack: true)).As<KeyPathInformation>();
				change = keyInfo.ScriptPubKey;
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
				change = request.ExplicitChangeAddress.ScriptPubKey;
			}
			txBuilder.SetChange(change);
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
				else if (request.FeePreference?.ExplicitFee is null)
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
				if (request.FeePreference?.ExplicitFee is Money explicitFee)
				{
					txBuilder.SendFees(explicitFee);
				}
				if (request.SpendAllMatchingOutpoints is true)
					txBuilder.SendAllRemainingToChange();
				psbt = txBuilder.BuildPSBT(false, psbtVersion);
				hasChange = psbt.Outputs.Any(o => o.ScriptPubKey == change);
			}
			catch (OutputTooSmallException ex) when (ex.Reason == OutputTooSmallException.ErrorType.TooSmallAfterSubtractedFee)
			{
				throw new NBXplorerException(new NBXplorerError(400, "output-too-small",
					message: "You can't substract fee on this destination, because not enough money was sent to it",
					reason: OutputTooSmallException.ErrorType.TooSmallAfterSubtractedFee.ToString()));
			}
			catch (OutputTooSmallException ex) when (ex.Reason == OutputTooSmallException.ErrorType.TooSmallBeforeSubtractedFee)
			{
				if (sweepAll)
				{
					throw new NBXplorerException(new NBXplorerError(400, "not-enough-funds", "You can't sweep funds, because you don't have any."));
				}
				else
				{
					throw new NBXplorerException(new NBXplorerError(400, "output-too-small",
						message: "The amount is being sent is below dust threshold",
						reason: OutputTooSmallException.ErrorType.TooSmallBeforeSubtractedFee.ToString()));
				}
			}
			catch (NotEnoughFundsException)
			{
				throw new NBXplorerException(new NBXplorerError(400, "not-enough-funds", "Not enough funds for doing this transaction"));
			}
			// We made sure we can build the PSBT, so now we can reserve the change address if we need to
			if (hasChange && request.ExplicitChangeAddress == null && request.ReserveChangeAddress)
			{
				var derivation = (await GetUnusedAddress(trackedSourceContext, DerivationFeature.Change, reserve: true, autoTrack: true)).As<KeyPathInformation>();
				// In most of the time, this is the same as previously, so no need to rebuild PSBT
				if (derivation.ScriptPubKey != change)
				{
					change = derivation.ScriptPubKey;
					txBuilder.SetChange(change);
					psbt = txBuilder.BuildPSBT(false, psbtVersion);
				}
			}

			var tx = psbt.GetGlobalTransaction();
			if (request.Version is uint v)
				tx.Version = v;
			txBuilder.SetSigningOptions(SigHash.All);
			psbt = txBuilder.CreatePSBTFrom(tx, psbtVersion, false);

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
				ChangeAddress = hasChange ? change.GetDestinationAddress(network.NBitcoinNetwork) : null,
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
			var update = network.ParseJObject<UpdatePSBTRequest>(body);
			if (update.PSBT == null)
				throw new NBXplorerException(new NBXplorerError(400, "missing-parameter", "'psbt' is missing"));
			await UpdatePSBTCore(update, network);
			return Json(new UpdatePSBTResponse() { PSBT = update.PSBT }, network.JsonSerializerSettings);
		}

		private async Task UpdatePSBTCore(UpdatePSBTRequest update, NBXplorerNetwork network)
		{
			var repo = RepositoryProvider.GetRepository(network);
			await this.UtxoFetcherService.UpdateUTXO(update);
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
			
			var keyInfosByScriptPubKey = new Dictionary<Script, KeyPathInformation>();
			var scriptPubKeys = update.PSBT.Outputs.OfType<PSBTCoin>().Concat(update.PSBT.Inputs)
											.Where(o => !o.HDKeyPaths.Any())
											.Select(o => o.GetTxOut()?.ScriptPubKey)
											.Where(s => s != null).ToArray();
			foreach (var keyInfos in (await repo.GetKeyInformations(scriptPubKeys)))
			{
				var keyInfo = keyInfos.Value.FirstOrDefault(k => k.DerivationStrategy == strategy);
				if (keyInfo != null)
				{
					keyInfosByScriptPubKey.TryAdd(keyInfo.ScriptPubKey, keyInfo);
				}
			}
			List<Script> redeems = new List<Script>();
			if (strategy is StandardDerivationStrategyBase)
			{
				var pubkeys = strategy.GetExtPubKeys().Select(p => p.AsHDKeyCache()).ToArray();
				foreach (var pubkey in pubkeys)
				{
					// We derive everything the fastest way possible on multiple cores
					pubkey.Derive(keyInfosByScriptPubKey.Select(s => s.Value.KeyPath).ToArray());
				}
				foreach (var c in NonFinalizedCoins(update))
				{
					var script = c.GetTxOut()?.ScriptPubKey;
					if (script != null &&
						keyInfosByScriptPubKey.TryGetValue(script, out var keyInfo))
					{
						foreach (var pubkey in pubkeys)
						{
							var childPubKey = pubkey.Derive(keyInfo.KeyPath);
							var rootKeyPath = new RootedKeyPath(pubkey.GetPublicKey().GetHDFingerPrint(), keyInfo.KeyPath);
							if (strategy is TaprootDerivationStrategy)
							{
								if (c is PSBTInput input)
									input.TaprootSighashType = TaprootSigHash.Default;
								c.TaprootInternalKey = childPubKey.GetPublicKey().GetTaprootFullPubKey().InternalKey;
								// Some consumers expect the internal key to be in the HDTaprootKeyPaths
								if (!TaprootPubKey.TryCreate(c.TaprootInternalKey.ToBytes(), out var pk))
									continue;
								NBitcoin.Extensions.AddOrReplace(c.HDTaprootKeyPaths, pk, new TaprootKeyPath(rootKeyPath));
							}
							else
							{
								NBitcoin.Extensions.AddOrReplace(c.HDKeyPaths, childPubKey.GetPublicKey(), rootKeyPath);
							}
							if (keyInfo.Redeem != null && c.RedeemScript is null && c.WitnessScript is null)
								redeems.Add(keyInfo.Redeem);
						}
					}
				}
			}
			else if (strategy is PolicyDerivationStrategy miniscriptDerivation)
			{
				foreach (var c in NonFinalizedCoins(update))
				{
					var script = c.GetTxOut()?.ScriptPubKey;
					if (script != null &&
						keyInfosByScriptPubKey.TryGetValue(script, out var keyInfo))
					{
						var derivation = (PolicyDerivation)miniscriptDerivation.GetDerivation(keyInfo.Feature, (uint)keyInfo.Index);
						var trInfo = derivation.Details.Miniscript.GetTaprootInfo();
						if (trInfo is not null)
						{
							c.TaprootInternalKey = trInfo.InternalPubKey;
							if (c is PSBTInput input)
							{
								input.TaprootSighashType = TaprootSigHash.Default;
								input.TaprootMerkleRoot = trInfo.MerkleRoot;
							}
						}
						foreach (var publicKey in derivation.Details.DerivedKeys)
						{
							if (publicKey.Pubkey is MiniscriptNode.Value.PubKeyValue pk)
							{
								NBitcoin.Extensions.AddOrReplace(c.HDKeyPaths, pk.PubKey, publicKey.Source.RootedKeyPath.Derive(publicKey.KeyPath));
							}
							else if (publicKey.Pubkey is MiniscriptNode.Value.TaprootPubKeyValue tpk)
							{
								uint256[] leaf = publicKey.TaprootBranch is null ? null : [new TapScript(publicKey.TaprootBranch.GetScript(), TapLeafVersion.C0).LeafHash];
								NBitcoin.Extensions.AddOrReplace(c.HDTaprootKeyPaths, tpk.PubKey, new(publicKey.Source.RootedKeyPath.Derive(publicKey.KeyPath), leaf));
							}
							if (derivation.Redeem != null && c.RedeemScript is null && c.WitnessScript is null)
								redeems.Add(derivation.Redeem);
						}
					}
				}
			}
			if (redeems.Count != 0)
				update.PSBT.AddScripts(redeems.ToArray());
		}

		private static IEnumerable<PSBTCoin> NonFinalizedCoins(UpdatePSBTRequest update)
		{
			return update.PSBT.Outputs.OfType<PSBTCoin>().Concat(update.PSBT.Inputs.Where(o => !o.IsFinalized()));
		}
	}
}
