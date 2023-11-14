using NBitcoin;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace NBXplorer.Models
{
	public class GetTransactionsResponse
	{
		public int Height
		{
			get; set;
		}

		public TransactionInformationSet ConfirmedTransactions
		{
			get; set;
		} = new TransactionInformationSet();

		public TransactionInformationSet UnconfirmedTransactions
		{
			get; set;
		} = new TransactionInformationSet();

		public TransactionInformationSet ImmatureTransactions
		{
			get; set;
		} = new TransactionInformationSet();

		public TransactionInformationSet ReplacedTransactions
		{
			get; set;
		} = new TransactionInformationSet();
	}

	public class TransactionInformationSet
	{
		public List<TransactionInformation> Transactions
		{
			get; set;
		} = new List<TransactionInformation>();
	}

	public class TransactionInformationMatch
	{
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public KeyPath KeyPath
		{
			get; set;
		}
		public int Index
		{
			get; set;
		}
		public Money Value
		{
			get; set;
		}
	}
	public class TransactionInformation
	{
		public uint256 BlockHash
		{
			get; set;
		}
		public long Confirmations
		{
			get; set;
		}
		public long? Height
		{
			get; set;
		}

		public bool IsMature
		{
			get; set;
		}
		public uint256 TransactionId
		{
			get; set;
		}
		public Transaction Transaction
		{
			get; set;
		}
		public List<MatchedOutput> Outputs
		{
			get; set;
		} = new List<MatchedOutput>();

		public List<MatchedOutput> Inputs
		{
			get; set;
		} = new List<MatchedOutput>();
		public DateTimeOffset Timestamp
		{
			get;
			set;
		}
		public IMoney BalanceChange
		{
			get;
			set;
		}
		public uint256 ReplacedBy { get; set; }
		public uint256 Replacing { get; set; }
		public bool Replaceable { get; set; }
	}
}
