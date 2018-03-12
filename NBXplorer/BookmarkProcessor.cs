using NBitcoin;
using NBitcoin.Crypto;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class BookmarkProcessor
	{
		byte[] _Buffer;
		bool _FirstPush = true;
		public BookmarkProcessor(BookmarkProcessor copied)
		{
			_Buffer = copied._Buffer.ToArray();
			_Hasher = new MemoryStream(_Buffer);
			_CurrentHash = copied._CurrentHash.ToArray();
		}
		public BookmarkProcessor(int bufferSize)
		{
			_Buffer = new byte[bufferSize];
			_Hasher = new MemoryStream(_Buffer);
		}

		MemoryStream _Hasher = new MemoryStream();
		byte[] _CurrentHash = new byte[20];
		public Bookmark CurrentBookmark
		{
			get
			{
				return new Bookmark(new uint160(_CurrentHash));
			}
		}

		public void PushNew()
		{
			_Hasher.Position = 0;
			if(!_FirstPush)
				_Hasher.Write(_CurrentHash, 0, 20);
		}
		public void AddData(byte[] data)
		{
			_Hasher.Write(data, 0, data.Length);
		}

		public void AddData(bool data)
		{
			_Hasher.WriteByte((byte)(data ? 1 : 0));
		}

		public void AddData(IBitcoinSerializable serializable)
		{
			var bs = new BitcoinStream(_Hasher, true);
			bs.ReadWrite(ref serializable);
		}

		public void AddData(uint256 serializable)
		{
			var bs = new BitcoinStream(_Hasher, true);
			bs.ReadWrite(ref serializable);
		}

		public void UpdateBookmark()
		{
			_CurrentHash = Hashes.RIPEMD160(_Buffer, _Buffer.Length);
			_FirstPush = false;
		}

		internal void Clear()
		{
			Array.Clear(_CurrentHash, 0, _CurrentHash.Length);
		}

		public BookmarkProcessor Clone()
		{
			return new BookmarkProcessor(this);
		}
	}
}
