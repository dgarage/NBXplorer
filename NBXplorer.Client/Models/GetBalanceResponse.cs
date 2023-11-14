using NBitcoin;

namespace NBXplorer.Models
{
	public class GetBalanceResponse
	{
		/// <summary>
		/// How the confirmed balance would be updated once all the unconfirmed transactions were confirmed.
		/// </summary>
		public IMoney Unconfirmed { get; set; }
		/// <summary>
		/// The balance of all funds in confirmed transactions.
		/// </summary>
		public IMoney Confirmed { get; set; }
		/// <summary>
		/// The total of funds owned (ie, `confirmed + unconfirmed`)
		/// </summary>
		public IMoney Total { get; set; }
		/// <summary>
		/// The total unspendable funds (ie, coinbase reward which need 100 confirmations before being spendable)
		/// </summary>
		public IMoney Immature { get; set; }

		/// <summary>
		/// The total spendable balance. (ie, `total - immature`)
		/// </summary>
		public IMoney Available { get; set; }
	}
}
