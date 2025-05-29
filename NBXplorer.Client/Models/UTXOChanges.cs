using NBitcoin;
using System.Linq;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NBXplorer.DerivationStrategy;
#if !NO_RECORD
using NBitcoin.WalletPolicies;
#endif

namespace NBXplorer.Models
{
	public class UTXOChanges
	{
		public TrackedSource TrackedSource { get; set; }
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public DerivationStrategyBase DerivationStrategy
		{
			get; set;
		}

		uint _CurrentHeight;
		public int CurrentHeight
		{
			get
			{
				return checked((int)_CurrentHeight);
			}
			set
			{
				_CurrentHeight = checked((uint)value);
			}
		}

		UTXOChange _Unconfirmed = new UTXOChange();
		public UTXOChange Unconfirmed
		{
			get
			{
				return _Unconfirmed;
			}
			set
			{
				_Unconfirmed = value;
			}
		}

		public List<UTXO> SpentUnconfirmed
		{
			get;
			set;
		} = new List<UTXO>();

		UTXOChange _Confirmed = new UTXOChange();
		public UTXOChange Confirmed
		{
			get
			{
				return _Confirmed;
			}
			set
			{
				_Confirmed = value;
			}
		}

		public Coin[] GetUnspentCoins(bool excludeUnconfirmedUTXOs = false)
		{
			return GetUnspentCoins(excludeUnconfirmedUTXOs ? 1 : 0);
		}
		public Coin[] GetUnspentCoins(int minConfirmations)
		{
			return GetUnspentUTXOs(minConfirmations).Select(c => c.AsCoin(DerivationStrategy)).ToArray();
		}
		public UTXO[] GetUnspentUTXOs(int minConf)
		{
			var excludeUnconfirmedUTXOs = minConf > 0;
			Dictionary<OutPoint, UTXO> received = new Dictionary<OutPoint, UTXO>();
			foreach (var utxo in Confirmed.UTXOs.Where(u => u.Confirmations >= minConf).Concat(excludeUnconfirmedUTXOs ? (IEnumerable<UTXO>)Array.Empty<UTXO>() : Unconfirmed.UTXOs))
			{
				received.TryAdd(utxo.Outpoint, utxo);
			}
			foreach (var utxo in Confirmed.SpentOutpoints.Concat(Unconfirmed.SpentOutpoints))
			{
				received.Remove(utxo);
			}
			return received.Values.ToArray();
		}
		public UTXO[] GetUnspentUTXOs(bool excludeUnconfirmedUTXOs = false)
		{
			return GetUnspentUTXOs(excludeUnconfirmedUTXOs ? 1 : 0);
		}

		public Key[] GetKeys(ExtKey extKey, bool excludeUnconfirmedUTXOs = false)
		{
			return GetUnspentUTXOs(excludeUnconfirmedUTXOs).Where(u => u.KeyPath is not null).Select(u => extKey.Derive(u.KeyPath).PrivateKey).ToArray();
		}
	}
	public class UTXOChange
	{

		List<UTXO> _UTXOs = new List<UTXO>();
		public List<UTXO> UTXOs
		{
			get
			{
				return _UTXOs;
			}
			set
			{
				_UTXOs = value;
			}
		}

		List<OutPoint> _SpentOutpoints = new List<OutPoint>();
		public List<OutPoint> SpentOutpoints
		{
			get
			{
				return _SpentOutpoints;
			}
			set
			{
				_SpentOutpoints = value;
			}
		}
	}

	public class UTXO
	{
		public UTXO()
		{

		}

		public UTXO(ICoin coin)
		{
			Outpoint = coin.Outpoint;
			Index = (int)coin.Outpoint.N;
			TransactionHash = coin.Outpoint.Hash;
			Value = coin.Amount;
			ScriptPubKey = coin.TxOut.ScriptPubKey;
		}

		[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public DerivationFeature? Feature
		{
			get; set;
		}

		public Coin AsCoin()
		{
			return AsCoin(null);
		}

		public Coin AsCoin(DerivationStrategy.DerivationStrategyBase derivationStrategy)
		{
			if (Value is Money v)
			{
				var coin = new Coin(Outpoint, new TxOut(v, ScriptPubKey));
				if (Redeem is not null)
				{
					coin = coin.ToScriptCoin(Redeem);
				}
				else
				{
					DerivationStrategy.Derivation derivation = null;
					if (derivationStrategy is StandardDerivationStrategyBase kd && KeyPath is not null)
					{
						derivation = kd.GetDerivation(KeyPath);
					}
#if !NO_RECORD
					else if (derivationStrategy is PolicyDerivationStrategy md && Feature is { } f)
					{
						derivation = md.GetDerivation(f, (uint)KeyIndex);
					}
#endif
					if (derivation is not null)
					{
						if (derivation.ScriptPubKey != coin.ScriptPubKey)
							throw new InvalidOperationException($"This Derivation Strategy does not own this coin");
						if (derivation.Redeem != null)
							coin = coin.ToScriptCoin(derivation.Redeem);
					}
				}
				return coin;
			}
			return null;
		}

		OutPoint _Outpoint = new OutPoint();
		public OutPoint Outpoint
		{
			get
			{
				return _Outpoint;
			}
			set
			{
				_Outpoint = value;
			}
		}

		public int Index { get; set; }
		public uint256 TransactionHash { get; set; }

		Script _ScriptPubKey;
		public Script ScriptPubKey
		{
			get
			{
				return _ScriptPubKey;
			}
			set
			{
				_ScriptPubKey = value;
			}
		}

		public BitcoinAddress Address { get; set; }

		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public Script Redeem { get; set; }
		IMoney _Value;
		public IMoney Value
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

		KeyPath _KeyPath;

		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public KeyPath KeyPath
		{
			get
			{
				return _KeyPath;
			}
			set
			{
				_KeyPath = value;
			}
		}


		DateTimeOffset _Timestamp;
		public DateTimeOffset Timestamp
		{
			get
			{
				return _Timestamp;
			}
			set
			{
				_Timestamp = value;
			}
		}


		long _Confirmations;
		public long Confirmations
		{
			get
			{
				return checked((long)_Confirmations);
			}
			set
			{
				_Confirmations = checked((long)value);
			}
		}

		public int KeyIndex { get; set; }
	}
}
