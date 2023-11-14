using NBitcoin;

namespace NBXplorer.DerivationStrategy
{
	public class Derivation
	{
		public Derivation()
		{

		}
		public Script ScriptPubKey
		{
			get; set;
		}
		public Script Redeem
		{
			get; set;
		}
	}
}
