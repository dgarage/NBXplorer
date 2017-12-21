using System;
using System.Collections.Generic;
using System.Text;

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
	}
    public class StatusResult
    {
		public string Network
		{
			get;
			set;
		}
		public BitcoinStatus BitcoinStatus
		{
			get; set;
		}
		public double RepositoryPingTime
		{
			get;
			set;
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
	}
}
