using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class LockUTXOsRequest
    {
		public Money Amount
		{
			get; set;
		}
		public string Destination
		{
			get; set;
		}
		public IEnumerable<LockUTXOsRequestDestination> GetDestinations()
		{
			if (Destination != null)
			{
				yield return new LockUTXOsRequestDestination() { Destination = Destination, Amount = Amount, SubstractFees = SubstractFees };
			}
			if (Destinations != null)
			{
				foreach (var destination in Destinations)
				{
					yield return destination;
				}
			}
		}
		public LockUTXOsRequestDestination[] Destinations { get; set; }
		public FeeRate FeeRate
		{
			get;
			set;
		}
		public bool SubstractFees { get; set; }
	}

	public class LockUTXOsRequestDestination
	{
		public string Destination { get; set; }
		/// <summary>
		/// Will Send this amount to this destination (Mutually exclusive with: SweepAll)
		/// </summary>
		public Money Amount { get; set; }
		/// <summary>
		/// Will substract the fees of this transaction to this destination (Mutually exclusive with: SweepAll)
		/// </summary>
		public bool SubstractFees { get; set; }
		/// <summary>
		/// Will sweep all the balance of your wallet to this destination (Mutually exclusive with: Amount, SubstractFees)
		/// </summary>
		public bool SweepAll { get; set; }

		public CreatePSBTDestination ToPSBTDestination(NBXplorerNetwork network)
		{
			BitcoinAddress destinationAddress = null;
			try
			{
				destinationAddress = BitcoinAddress.Create(Destination, network.NBitcoinNetwork);
			}
			catch
			{
				throw new NBXplorerException(new NBXplorerError(400, "invalid-destination", "Invalid destination address"));
			}
			if (Amount == null || Amount <= Money.Zero)
				throw new NBXplorerException(new NBXplorerError(400, "invalid-amount", "amount should be equal or less than 0 satoshi"));
			return new CreatePSBTDestination() { Destination = destinationAddress, Amount = Amount, SubstractFees = SubstractFees, SweepAll = SweepAll };
		}
	}
}
