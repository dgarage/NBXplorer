using NBitcoin;
using Newtonsoft.Json;

namespace NBXplorer.Models
{
	public class BitcoinStatus
	{
		public int Blocks
		{
			get; set;
		}
		public int Headers
		{
			get; set;
		}
		public double VerificationProgress
		{
			get; set;
		}
		public bool IsSynched
		{
			get;
			set;
		}
		public FeeRate IncrementalRelayFee
		{
			get;
			set;
		}
		public FeeRate MinRelayTxFee
		{
			get;
			set;
		}
		public string[] ExternalAddresses { get; set; }
		public NodeCapabilities Capabilities { get; set; }
	}
	public class NodeCapabilities
	{
		public bool CanScanTxoutSet { get; set; }
		public bool CanSupportSegwit { get; set; }
		public bool CanSupportTaproot { get; set; }
		public bool CanSupportTransactionCheck { get; set; }
	}
	public class StatusResult
    {
		public BitcoinStatus BitcoinStatus
		{
			get; set;
		}
		public bool IsFullySynched
		{
			get; set;
		}
		public int ChainHeight
		{
			get;
			set;
		}
		public int? SyncHeight
		{
			get;
			set;
		}
		public string InstanceName
		{
			get;
			set;
		}
		[JsonConverter(typeof(NBitcoin.JsonConverters.ChainNameJsonConverter))]
		public ChainName NetworkType
		{
			get;
			set;
		}
		public string CryptoCode
		{
			get;
			set;
		}

		public string[] SupportedCryptoCodes
		{
			get; set;
		}
		public string Version
		{
			get;
			set;
		}
	}
}
