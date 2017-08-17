using NBitcoin;
using System.Linq;
using NBitcoin.Protocol;
using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin.Crypto;
using System.IO;

namespace ElementsExplorer
{
	public class UTXOChanges : IBitcoinSerializable
	{
		public void ReadWrite(BitcoinStream stream)
		{
			stream.ReadWrite(ref _Confirmed);
			stream.ReadWrite(ref _Unconfirmed);
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

		public void LoadChanges(Transaction tx, Func<Script, KeyPath> getKeyPath)
		{
			if(tx == null)
				throw new ArgumentNullException("tx");
			tx.CacheHashes();

			
			var existingUTXOs = new HashSet<OutPoint>(UTXOs.Select(u => u.Outpoint));
			var removedUTXOs = new HashSet<OutPoint>(SpentOutpoints);

			foreach(var input in tx.Inputs)
			{
				if(existingUTXOs.Remove(input.PrevOut))
					removedUTXOs.Add(input.PrevOut);
			}

			int index = -1;
			foreach(var output in tx.Outputs)
			{
				index++;
				if(!existingUTXOs.Contains(new OutPoint(tx.GetHash(), index)))
				{
					var keyPath = getKeyPath(output.ScriptPubKey);
					if(keyPath != null)
					{
						var outpoint = new OutPoint(tx.GetHash(), index);
						UTXOs.Add(new UTXO(outpoint, output, keyPath));
						existingUTXOs.Add(outpoint);
					}
				}
			}
			UTXOs = UTXOs.Where(u => existingUTXOs.Contains(u.Outpoint)).ToList();
			SpentOutpoints = removedUTXOs.ToList();
		}

		public bool HasConflict(Transaction tx)
		{
			var existingUTXOs = new HashSet<OutPoint>(UTXOs.Select(u => u.Outpoint));
			var spentOutpoints = new HashSet<OutPoint>(SpentOutpoints);

			//If there is double spending
			foreach(var input in tx.Inputs)
			{
				if(spentOutpoints.Contains(input.PrevOut))
					return true;
				spentOutpoints.Add(input.PrevOut);
			}

			var index = -1;
			foreach(var output in tx.Outputs)
			{
				index++;
				var outpoint = new OutPoint(tx.GetHash(), index);
				if(existingUTXOs.Contains(outpoint) || spentOutpoints.Contains(outpoint))
					return true;
				existingUTXOs.Add(outpoint);
			}
			return false;
		}

		public UTXOChange Diff(UTXOChange previousChange)
		{
			var previousUTXOs = previousChange.UTXOs.ToDictionary(u => u.Outpoint);
			var currentUTXOs = UTXOs.ToDictionary(u => u.Outpoint);

			var deletedUTXOs = previousChange.UTXOs.Where(utxo => !currentUTXOs.ContainsKey(utxo.Outpoint));
			var addedUTXOs = UTXOs.Where(utxo => !previousUTXOs.ContainsKey(utxo.Outpoint));

			var diff = new UTXOChange();
			diff.Hash = this.Hash;
			diff.Reset = Reset;
			foreach(var deleted in deletedUTXOs)
			{
				diff.SpentOutpoints.Add(deleted.Outpoint);
			}
			foreach(var added in addedUTXOs)
			{
				diff.UTXOs.Add(added);
			}
			return diff;
		}

		internal void Clear()
		{
			Reset = false;
			UTXOs.Clear();
			SpentOutpoints.Clear();
		}

		public uint256 GetHash()
		{
			MemoryStream ms = new MemoryStream();
			BitcoinStream bs = new BitcoinStream(ms, true);
			bs.ReadWrite(ref _UTXOs);
			bs.ReadWrite(ref _SpentOutpoints);
			return Hashes.Hash256(ms.ToArray());
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


		ConfidentialAsset _Asset;
		public ConfidentialAsset Asset
		{
			get
			{
				return _Asset;
			}
			set
			{
				_Asset = value;
			}
		}


		ConfidentialValue _Value;
		public ConfidentialValue Value
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


		ConfidentialNonce _Nonce;
		public ConfidentialNonce Nonce
		{
			get
			{
				return _Nonce;
			}
			set
			{
				_Nonce = value;
			}
		}


		byte[] _RangeProof;
		public byte[] RangeProof
		{
			get
			{
				return _RangeProof;
			}
			set
			{
				_RangeProof = value;
			}
		}


		byte[] _SurjectionProof;
		public byte[] SurjectionProof
		{
			get
			{
				return _SurjectionProof;
			}
			set
			{
				_SurjectionProof = value;
			}
		}


		KeyPath _KeyPath;

		public UTXO(OutPoint outPoint, TxOut output, KeyPath keyPath)
		{
			Outpoint = outPoint;
			RangeProof = output.RangeProof;
			SurjectionProof = output.SurjectionProof;
			Nonce = output.Nonce;
			Asset = output.Asset;
			Value = output.ConfidentialValue;
			ScriptPubKey = output.ScriptPubKey;
			KeyPath = keyPath;
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

		public void ReadWrite(BitcoinStream stream)
		{
			stream.ReadWrite(ref _Outpoint);
			stream.ReadWrite(ref _ScriptPubKey);
			stream.ReadWrite(ref _Asset);
			stream.ReadWrite(ref _Value);
			stream.ReadWrite(ref _Nonce);
			stream.ReadWriteAsVarString(ref _RangeProof);
			stream.ReadWriteAsVarString(ref _SurjectionProof);

			uint[] indexes = _KeyPath?.Indexes ?? new uint[0];
			stream.ReadWrite(ref indexes);
			if(!stream.Serializing)
				_KeyPath = new KeyPath(indexes);
		}
	}
}
