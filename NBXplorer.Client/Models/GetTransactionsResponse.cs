using NBitcoin;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class GetTransactionsResponse
	{
		public int Height
		{
			get; set;
		}

		public bool HasChanges()
		{
			return ConfirmedTransactions.HasChanges() || UnconfirmedTransactions.HasChanges() || ReplacedTransactions.HasChanges();
		}

		public TransactionInformationSet ConfirmedTransactions
		{
			get; set;
		} = new TransactionInformationSet();

		public TransactionInformationSet UnconfirmedTransactions
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
		public Bookmark KnownBookmark
		{
			get; set;
		}

		public Bookmark Bookmark
		{
			get; set;
		}
		public List<TransactionInformation> Transactions
		{
			get; set;
		} = new List<TransactionInformation>();
		public bool HasChanges()
		{
			return KnownBookmark != Bookmark;
		}
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
		public int Confirmations
		{
			get; set;
		}
		public int? Height
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
		public Money BalanceChange
		{
			get;
			set;
		}
	}
}
