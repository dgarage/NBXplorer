using NBitcoin;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using NBXplorer.DerivationStrategy;

namespace NBXplorer.Models
{
    public class LockUTXOsResponse
    {
		public class ChangeInfo
		{
			public KeyPath KeyPath
			{
				get; set;
			}
			public Money Value
			{
				get; set;
			}
		}

		public class SpentCoin
		{
			public KeyPath KeyPath
			{
				get; set;
			}

			public Money Value
			{
				get; set;
			}

			public OutPoint Outpoint
			{
				get; set;
			}
		}
		public string UnlockId
		{
			get;
			set;
		}
		public SpentCoin[] SpentCoins
		{
			get; set;
		}

		public ChangeInfo ChangeInformation
		{
			get; set;
		}

		public Transaction Transaction
		{
			get; set;
		}

		public Transaction Sign(DerivationStrategyBase derivationScheme, ExtKey key, Network network)
		{
			var txBuilder = network.CreateTransactionBuilder();
			txBuilder.AddCoins(SpentCoins.Select(s => CreateCoin(derivationScheme, s)).ToArray());
			txBuilder.AddKeys(SpentCoins.Select(s => key.Derive(s.KeyPath).PrivateKey).ToArray());
			return txBuilder.SignTransaction(Transaction);
		}

		ICoin CreateCoin(DerivationStrategyBase derivationStrategy, SpentCoin spent)
		{
			var derivation = derivationStrategy.Derive(spent.KeyPath);
			ICoin coin = new Coin(spent.Outpoint, new TxOut(spent.Value, derivation.ScriptPubKey));
			if(derivation.Redeem != null)
				coin = ((Coin)coin).ToScriptCoin(derivation.Redeem);
			return coin;
		}
	}
}
