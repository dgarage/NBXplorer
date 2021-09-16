using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Altcoins.Elements;
using NBXplorer.Altcoins.Liquid;
using NBitcoin.RPC;
using NBXplorer.Models;
using System;
using NBXplorer.DerivationStrategy;

namespace NBXplorer
{
	public class LiquidRepository : Repository
	{
		private readonly RPCClient _rpcClient;

		internal LiquidRepository(DBTrie.DBTrieEngine engine, NBXplorerNetwork network, KeyPathTemplates keyPathTemplates,
			RPCClient rpcClient) : base(engine, network, keyPathTemplates, rpcClient)
		{
			_rpcClient = rpcClient;
		}

		class ElementsTrackedTransaction : TrackedTransaction
		{
			public ElementsTrackedTransaction(TrackedTransactionKey key, TrackedSource trackedSource, IEnumerable<Coin> receivedCoins, Dictionary<Script, KeyPath> knownScriptMapping) :
				base(key, trackedSource, receivedCoins, knownScriptMapping)
			{
				ClearCoinValues();
				Unblind(receivedCoins, false);
			}
			public ElementsTrackedTransaction(TrackedTransactionKey key, TrackedSource trackedSource, Transaction transaction, Dictionary<Script, KeyPath> knownScriptMapping) :
				base(key, trackedSource, transaction, knownScriptMapping)
			{
				ClearCoinValues();
				Unblind(transaction.Outputs.AsCoins(), false);
			}

			private void ClearCoinValues()
			{
				ReceivedCoins = ReceivedCoins.Select(coin => (ICoin) new Coin(coin.Outpoint.Clone(), coin.TxOut.Clone())).ToHashSet();
				foreach (var coin in ReceivedCoins.OfType<Coin>())
				{
					coin.Amount = null;	
				}
			}

			public override ITrackedTransactionSerializable CreateBitcoinSerializable()
			{
				return new ElementsTransactionMatchData(this);
			}

			public void Unblind(ElementsTransaction unblindedTransaction, bool saveUnblindData)
			{
				Unblind(unblindedTransaction.Outputs.AsCoins(), saveUnblindData);
			}

			public void Unblind(IEnumerable<ICoin> unblindedCoins, bool saveUnblindData)
			{
				foreach (var coin in unblindedCoins)
				{
					AssetMoney assetMoney = null;
					if (coin is AssetCoin assetCoin)
					{
						assetMoney = assetCoin.Money;
					}
					if (coin.TxOut is ElementsTxOut elementsTxOut &&
						elementsTxOut.Asset.AssetId != null &&
						elementsTxOut.Value != null)
					{
						assetMoney = new AssetMoney(elementsTxOut.Asset.AssetId, elementsTxOut.Value.Satoshi);
					}
					if (assetMoney != null &&
						TryGetReceivedCoinByIndex((int)coin.Outpoint.N) is Coin existingCoin)
					{
						if (saveUnblindData)
							Unblinded.TryAdd((int)existingCoin.Outpoint.N, assetMoney);
						this.ReceivedCoins.Remove(existingCoin);
						this.ReceivedCoins.Add(new AssetCoin(assetMoney, existingCoin));
					}
				}
			}

			ICoin TryGetReceivedCoinByIndex(int index)
			{
				return this.ReceivedCoins.FirstOrDefault(r => r.Outpoint.N == index);
			}
			public void Unblind(IEnumerable<ElementsTransactionMatchData.UnblindData> unblindData)
			{
				foreach (var unblind in unblindData)
				{
					if (TryGetReceivedCoinByIndex(unblind.Index) is Coin existingCoin)
					{
						this.ReceivedCoins.Remove(existingCoin);
						var money = new AssetMoney(unblind.AssetId, unblind.Value);
						this.ReceivedCoins.Add(new AssetCoin(money, existingCoin));
						this.Unblinded.Add(unblind.Index, money);
					}
				}
			}
			public Dictionary<int, AssetMoney> Unblinded = new Dictionary<int, AssetMoney>();
		}
		class ElementsTransactionMatchData : TrackedTransaction.TransactionMatchData
		{
			internal class UnblindData : IBitcoinSerializable
			{

				long _Value;
				public long Value
				{
					get
					{
						return _Value;
					}
					set
					{
						_Value = value;
					}
				}

				uint256 _AssetId;
				public uint256 AssetId
				{
					get
					{
						return _AssetId;
					}
					set
					{
						_AssetId = value;
					}
				}

				int _Index;
				public int Index
				{
					get
					{
						return _Index;
					}
					set
					{
						_Index = value;
					}
				}

				public void ReadWrite(BitcoinStream stream)
				{
					stream.ReadWrite(ref _Index);
					stream.ReadWrite(ref _AssetId);
					stream.ReadWrite(ref _Value);
				}
			}


			List<UnblindData> _UnblindData = new List<UnblindData>();
			internal List<UnblindData> Unblind
			{
				get
				{
					return _UnblindData;
				}
				set
				{
					_UnblindData = value;
				}
			}
			public ElementsTransactionMatchData(TrackedTransactionKey key) : base(key)
			{

			}
			public ElementsTransactionMatchData(ElementsTrackedTransaction trackedTransaction) : base(trackedTransaction)
			{
				foreach (var unblind in trackedTransaction.Unblinded)
					_UnblindData.Add(new UnblindData() { Index = unblind.Key, AssetId = unblind.Value.AssetId, Value = unblind.Value.Quantity });
			}

			public override void ReadWrite(BitcoinStream stream)
			{
				base.ReadWrite(stream);
				stream.ReadWrite(ref _UnblindData);
			}
		}

		protected override async Task AfterMatch(TrackedTransaction tx, IReadOnlyCollection<KeyPathInformation> keyInfos)
		{
			await base.AfterMatch(tx, keyInfos);
			if (tx.TrackedSource is DerivationSchemeTrackedSource ts &&
			    !ts.DerivationStrategy.Unblinded() && 
				tx.Transaction is ElementsTransaction elementsTransaction &&
				tx is ElementsTrackedTransaction elementsTracked)
			{
				var keys = keyInfos
					.Select(kv => (KeyPath: kv.KeyPath,
								   Address: kv.Address as BitcoinBlindedAddress,
								   BlindingKey: NBXplorerNetworkProvider.LiquidNBXplorerNetwork.GenerateBlindingKey(ts.DerivationStrategy, kv.KeyPath)))
					.Where(o => o.Address != null)
					.Select(o => new UnblindTransactionBlindingAddressKey()
					{
						Address = o.Address,
						BlindingKey = o.BlindingKey
					}).ToList();
				if (keys.Count != 0)
				{
					var unblinded = await _rpcClient.UnblindTransaction(keys, elementsTransaction, Network.NBitcoinNetwork);
					elementsTracked.Unblind(unblinded, true);
				}
			}
		}

		public override TrackedTransaction CreateTrackedTransaction(TrackedSource trackedSource, TrackedTransactionKey transactionKey, Transaction tx, Dictionary<Script, KeyPath> knownScriptMapping)
		{
			return new ElementsTrackedTransaction(transactionKey, trackedSource, tx, knownScriptMapping);
		}
		public override TrackedTransaction CreateTrackedTransaction(TrackedSource trackedSource, TrackedTransactionKey transactionKey, IEnumerable<Coin> coins, Dictionary<Script, KeyPath> knownScriptMapping)
		{
			return new ElementsTrackedTransaction(transactionKey, trackedSource, coins, knownScriptMapping);
		}
		public override TrackedTransaction CreateTrackedTransaction(TrackedSource trackedSource, ITrackedTransactionSerializable tx)
		{
			var trackedTransaction = (ElementsTrackedTransaction)base.CreateTrackedTransaction(trackedSource, tx);
			trackedTransaction.Unblind(((ElementsTransactionMatchData)tx).Unblind);
			return trackedTransaction;
		}
		protected override ITrackedTransactionSerializable CreateBitcoinSerializableTrackedTransaction(TrackedTransactionKey trackedTransactionKey)
		{
			return new ElementsTransactionMatchData(trackedTransactionKey);
		}
	}
}