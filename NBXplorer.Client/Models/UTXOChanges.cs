using NBitcoin;
using System.Linq;
using NBitcoin.Protocol;
using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin.Crypto;
using System.IO;
using Newtonsoft.Json;
using NBXplorer.DerivationStrategy;

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

		public bool HasChanges
		{
			get
			{
				return Confirmed.HasChanges || Unconfirmed.HasChanges;
			}
		}

		public Coin[] GetUnspentCoins(bool excludeUnconfirmedUTXOs = false)
		{
			if (Confirmed.KnownBookmark != null || Unconfirmed.KnownBookmark != null)
				throw new InvalidOperationException("This UTXOChanges is partial, it is calculate the unspent coins");
			return GetUnspentUTXOs(excludeUnconfirmedUTXOs).Select(c => c.AsCoin(DerivationStrategy)).ToArray();
		}

		public UTXO[] GetUnspentUTXOs(bool excludeUnconfirmedUTXOs = false)
		{
			Dictionary<OutPoint, UTXO> received = new Dictionary<OutPoint, UTXO>();
			foreach (var utxo in Confirmed.UTXOs.Concat(excludeUnconfirmedUTXOs ? (IEnumerable<UTXO>)Array.Empty<UTXO>() : Unconfirmed.UTXOs))
			{
				received.TryAdd(utxo.Outpoint, utxo);
			}
			foreach (var utxo in Confirmed.SpentOutpoints.Concat(Unconfirmed.SpentOutpoints))
			{
				received.Remove(utxo);
			}
			return received.Values.ToArray();
		}

		public Key[] GetKeys(ExtKey extKey, bool excludeUnconfirmedUTXOs = false)
		{
			return GetUnspentUTXOs(excludeUnconfirmedUTXOs).Select(u => extKey.Derive(u.KeyPath).PrivateKey).ToArray();
		}
	}
	public class UTXOChange
	{

		Bookmark _KnownBookmark;
		public Bookmark KnownBookmark
		{
			get
			{
				return _KnownBookmark;
			}
			set
			{
				_KnownBookmark = value;
			}
		}

		Bookmark _Bookmark = null;
		public Bookmark Bookmark
		{
			get
			{
				return _Bookmark;
			}
			set
			{
				_Bookmark = value;
			}
		}

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

		public bool HasChanges
		{
			get
			{
				return KnownBookmark != Bookmark || UTXOs.Count != 0 || SpentOutpoints.Count != 0;
			}
		}
	}

	public class UTXO
	{
		public UTXO()
		{

		}

		public UTXO(Coin coin)
		{
			Outpoint = coin.Outpoint;
			Index = (int)coin.Outpoint.N;
			TransactionHash = coin.Outpoint.Hash;
			Value = coin.TxOut.Value;
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
			var coin = new Coin(Outpoint, new TxOut(Value, ScriptPubKey));
			if (derivationStrategy != null)
			{
				var derivation = derivationStrategy.Derive(KeyPath);
				if (derivation.ScriptPubKey != coin.ScriptPubKey)
					throw new InvalidOperationException($"This Derivation Strategy does not own this coin");
				if (derivation.Redeem != null)
					coin = coin.ToScriptCoin(derivation.Redeem);
			}
			return coin;
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


		Money _Value;
		public Money Value
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


		uint _Confirmations;
		public int Confirmations
		{
			get
			{
				return checked((int)_Confirmations);
			}
			set
			{
				_Confirmations = checked((uint)value);
			}
		}
	}
}
