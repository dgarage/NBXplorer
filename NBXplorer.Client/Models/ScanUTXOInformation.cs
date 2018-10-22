using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public enum ScanUTXOStatus
	{
		Queued,
		Pending,
		Error,
		Complete
	}

	public class ScanUTXOProgress
	{
		public int Found { get; set; }
		public int BatchNumber { get; set; }
		public double CurrentBatchProgress { get; set; }
		public int From { get; set; }
		public int Count { get; set; }
		public int TotalSearched { get; set; }

		public Dictionary<DerivationFeature, int?> HighestKeyIndexFound { get; set; } = new Dictionary<DerivationFeature, int?>();

		public ScanUTXOProgress Clone()
		{
			return new ScanUTXOProgress()
			{
				Found = Found,
				BatchNumber = BatchNumber,
				CurrentBatchProgress = CurrentBatchProgress,
				From = From,
				Count = Count,
				HighestKeyIndexFound = new Dictionary<DerivationFeature, int?>(HighestKeyIndexFound),
				TotalSearched = TotalSearched
			};
		}
	}
	public class ScanUTXOInformation
	{
		public string Error { get; set; }

		[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
		public ScanUTXOStatus Status { get; set; }
		public ScanUTXOProgress Progress { get; set; }
	}
}
