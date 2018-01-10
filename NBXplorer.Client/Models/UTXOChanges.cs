using NBitcoin;
using System.Linq;
using NBitcoin.Protocol;
using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin.Crypto;
using System.IO;

namespace NBXplorer.Models
{
	public class UTXOChanges
	{

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
	}
	public class UTXOChange
	{
		byte _Reset;
		public bool Reset
		{
			get
			{
				return _Reset == 1;
			}
			set
			{
				_Reset = (byte)(value ? 1 : 0);
			}
		}

		uint256 _Hash = uint256.Zero;
		public uint256 Hash
		{
			get
			{
				return _Hash;
			}
			set
			{
				_Hash = value;
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
				return Reset || UTXOs.Count != 0 || SpentOutpoints.Count != 0;
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
			Value = coin.TxOut.Value;
			ScriptPubKey = coin.TxOut.ScriptPubKey;
		}

		public Coin AsCoin()
		{
			return new Coin(Outpoint, new TxOut(Value, ScriptPubKey));
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
