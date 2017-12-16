using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class StatusResult
    {
		public int? NodeBlocks
		{
			get; set;
		}
		public int? NodeHeaders
		{
			get; set;
		}
		public bool? IsSynching
		{
			get; set;
		}
		public int ChainHeight
		{
			get; set;
		}

		public bool Connected
		{
			get; set;
		}
		public double RepositoryPingTime
		{
			get;
			set;
		}
		public double? VerificationProgress
		{
			get;
			set;
		}

		public bool IsFullySynched()
		{
			return Connected && IsSynching.HasValue && !IsSynching.Value;
		}
	}
}
