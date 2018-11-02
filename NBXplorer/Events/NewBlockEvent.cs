using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.Events
{
    public class NewBlockEvent
    {
		public NewBlockEvent()
		{

		}
		public NewBlockEvent(string cryptoCode, uint256 block, int? height)
		{
			BlockId = block;
			Height = height;
			CryptoCode = cryptoCode;
		}

		public string CryptoCode
		{
			get; set;
		}

		public uint256 BlockId
		{
			get; set;
		}
		public int? Height { get; }

		public override string ToString()
		{
			var heightSuffix = Height.HasValue ? $" ({Height.Value})" : string.Empty;
			return $"{CryptoCode}: New block {BlockId}{heightSuffix}";
		}
	}
}
