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
		public int RemainingSeconds { get; set; }
		public int From { get; set; }
		public int Count { get; set; }
		public int TotalSearched { get; set; }
		public int? TotalSizeOfUTXOSet { get; set; }

		public void UpdateOverallProgress(DateTimeOffset? now = null)
		{
			now = now.HasValue ? now : DateTimeOffset.UtcNow;
			double percentPerBatch = 100.0 / (RemainingBatches + BatchNumber + 1);
			double batchPercent = BatchNumber * percentPerBatch;
			double insideBatchPercent = CurrentBatchProgress * (percentPerBatch / 100.0);
			var overallProgress = batchPercent + insideBatchPercent;

			var timeSpent = now.Value - StartedAt;
			var secondsRemaining = ((100 - overallProgress) / 100.0) * timeSpent.TotalSeconds;
			OverallProgress = (int)Math.Round(overallProgress);
			RemainingSeconds = (int)Math.Round(secondsRemaining);
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
				StartedAt = StartedAt,
				RemainingSeconds = RemainingSeconds
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
