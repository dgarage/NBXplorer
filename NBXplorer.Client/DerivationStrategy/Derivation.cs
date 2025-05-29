#nullable enable
using NBitcoin;
using System.Collections.Generic;
#if !NO_RECORD
using static NBitcoin.WalletPolicies.MiniscriptNode;
#endif

namespace NBXplorer.DerivationStrategy
{
	public class Derivation
	{
		public Derivation(Script scriptPubKey, Script? redeem = null)
		{
			ScriptPubKey = scriptPubKey;
			Redeem = redeem;
		}
		public Script ScriptPubKey
		{
			get;
		}
		public Script? Redeem
		{
			get; set;
		}
	}
#if !NO_RECORD
	public class PolicyDerivation : Derivation
	{
		public PolicyDerivation(NBitcoin.WalletPolicies.DerivationResult details, Script scriptPubKey, Script? redeem = null)
			: base(scriptPubKey, redeem)
		{
			Details = details;
		}

		public NBitcoin.WalletPolicies.DerivationResult Details { get; }
	}
#endif
}
