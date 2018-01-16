using NBitcoin;
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

		public Bookmark KnownBookmark
		{
			get; set;
		}

		public Bookmark Bookmark
		{
			get; set;
		}

		public bool HasChanges()
		{
			return KnownBookmark != Bookmark;
		}


		public List<TransactionInformation> Transactions
		{
			get; set;
		} = new List<TransactionInformation>();
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
	}
}
