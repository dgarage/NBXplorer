using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class NBXplorerNetwork
	{

		public Network NBitcoinNetwork
		{
			get; set;
		}
		
		public int MinRPCVersion
		{
			get;
			internal set;
		}
		public string CryptoCode
		{
			get
			{
				return NBitcoinNetwork.NetworkSet.CryptoCode;
			}
		}
		public NBXplorerDefaultSettings DefaultSettings
		{
			get;
			set;
		}

		public DerivationStrategy.DerivationStrategyFactory DerivationStrategyFactory
		{
			get;
			internal set;
		}
		public bool SupportCookieAuthentication
		{
			get;
			internal set;
		} = true;

		public TimeSpan ChainLoadingTimeout
		{
			get;
			set;
		} = TimeSpan.FromMinutes(15);

		public override string ToString()
		{
			return CryptoCode.ToString();
		}
	}
}
