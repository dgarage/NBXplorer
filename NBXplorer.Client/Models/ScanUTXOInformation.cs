using NBXplorer.DerivationStrategy;
using System.Linq;
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
		[JsonConverter(typeof(Newtonsoft.Json.Converters.UnixDateTimeConverter))]
		public DateTimeOffset StartedAt { get; set; }
		[JsonConverter(typeof(Newtonsoft.Json.Converters.UnixDateTimeConverter))]
		public DateTimeOffset? CompletedAt { get; set; }
		public int Found { get; set; }
		public int BatchNumber { get; set; }
		public int RemainingBatches { get; set; }
		public int CurrentBatchProgress { get; set; }
		public int OverallProgress { get; set; }
		public int From { get; set; }
		public int Count { get; set; }
		public int TotalSearched { get; set; }

		public void UpdateOverallProgress()
		{
			double percentPerBatch = (double)(100.0 / (RemainingBatches + BatchNumber + 1));
			double batchPercent = BatchNumber * percentPerBatch;
			double insideBatchPercent = ((double)CurrentBatchProgress / 100.0 ) * percentPerBatch;
			OverallProgress = (int)Math.Round(batchPercent + insideBatchPercent);
		}

		public void UpdateRemainingBatches(int gapLimit)
		{
			int highestIndex = -1;
			foreach(var index in HighestKeyIndexFound)
			{
				if(index.Value != null)
				{
					if (index.Value > highestIndex)
						highestIndex = index.Value.Value;
				}
			}

			int gapLimitIndex = highestIndex + gapLimit;
			var totalBatchesRequired = (gapLimitIndex / Count) + 1;
			RemainingBatches = totalBatchesRequired - (BatchNumber + 1);
		}
		

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
				TotalSearched = TotalSearched,
				OverallProgress = OverallProgress,
				RemainingBatches = RemainingBatches,
				CompletedAt = CompletedAt,
				StartedAt = StartedAt
			};
		}
	}
	public class ScanUTXOInformation
	{
		public string Error { get; set; }
		[JsonConverter(typeof(Newtonsoft.Json.Converters.UnixDateTimeConverter))]
		public DateTimeOffset QueuedAt { get; set; }

		[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
		public ScanUTXOStatus Status { get; set; }
		public ScanUTXOProgress Progress { get; set; }
	}
}
