using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.Events
{
    public class BitcoinDStateChangedEvent
	{
		public BitcoinDStateChangedEvent(BitcoinDWaiterState oldState, BitcoinDWaiterState newState)
		{
			OldState = oldState;
			NewState = newState;
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
			return ($"BitcoinDWaiter state changed: {OldState} => {NewState}");
		}
	}
}
