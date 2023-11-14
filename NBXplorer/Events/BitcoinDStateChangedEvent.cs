using NBXplorer.Backend;

namespace NBXplorer.Events
{
    public class BitcoinDStateChangedEvent
	{
		public BitcoinDStateChangedEvent(NBXplorerNetwork network, BitcoinDWaiterState oldState, BitcoinDWaiterState newState)
		{
			OldState = oldState;
			NewState = newState;
			Network = network;
		}

		public NBXplorerNetwork Network
		{
			get; set;
		}

		public BitcoinDWaiterState OldState
		{
			get; set;
		}
		public BitcoinDWaiterState NewState
		{
			get; set;
		}

		public override string ToString()
		{
			return ($"{Network.CryptoCode}: Node state changed: {OldState} => {NewState}");
		}
	}
}
