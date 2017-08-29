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
	public class UTXOChanges : IBitcoinSerializable
	{
		public void ReadWrite(BitcoinStream stream)
		{
			stream.ReadWriteAsVarInt(ref _CurrentHeight);
			stream.ReadWrite(ref _Confirmed);
			stream.ReadWrite(ref _Unconfirmed);
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
	}
	public class UTXOChange : IBitcoinSerializable
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

		public void ReadWrite(BitcoinStream stream)
		{
			stream.ReadWrite(ref _Reset);
			stream.ReadWrite(ref _Hash);
			stream.ReadWrite(ref _UTXOs);
			stream.ReadWrite(ref _SpentOutpoints);
		}
	}

	public class UTXO : IBitcoinSerializable
	{
		public UTXO()
		{

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



		TxOut _Output;
		public TxOut Output
		{
			get
			{
				return _Output;
			}
			set
			{
				_Output = value;
			}
		}

		KeyPath _KeyPath;

		public UTXO(OutPoint outPoint, TxOut output, KeyPath keyPath, int confirmations)
		{
			Outpoint = outPoint;
			Output = output;
			KeyPath = keyPath;
			Confirmations = confirmations;
		}

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

		public void ReadWrite(BitcoinStream stream)
		{
			stream.ReadWrite(ref _Outpoint);
			stream.ReadWrite(ref _Output);
			stream.ReadWriteAsVarInt(ref _Confirmations);

			uint[] indexes = _KeyPath?.Indexes ?? new uint[0];
			stream.ReadWrite(ref indexes);
			if(!stream.Serializing)
				_KeyPath = new KeyPath(indexes);
		}
	}
}
