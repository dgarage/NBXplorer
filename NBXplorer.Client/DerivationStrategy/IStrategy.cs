using NBitcoin;
using NBitcoin.Crypto;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.DerivationStrategy
{
	public enum DerivationFeature
	{
		Change,
		Deposit
	}
	public abstract class DerivationStrategyBase
	{
		internal DerivationStrategyBase()
		{

		}
		public abstract DerivationStrategyLine GetLineFor(DerivationFeature feature);

		internal string StringValue
		{
			get; set;
		}


		public override bool Equals(object obj)
		{
			DerivationStrategyBase item = obj as DerivationStrategyBase;
			if(item == null)
				return false;
			return StringValue.Equals(item.StringValue);
		}
		public static bool operator ==(DerivationStrategyBase a, DerivationStrategyBase b)
		{
			if(System.Object.ReferenceEquals(a, b))
				return true;
			if(((object)a == null) || ((object)b == null))
				return false;
			return a.StringValue == b.StringValue;
		}

		public static bool operator !=(DerivationStrategyBase a, DerivationStrategyBase b)
		{
			return !(a == b);
		}

		public override int GetHashCode()
		{
			return StringValue.GetHashCode();
		}

		public override string ToString()
		{
			return StringValue;
		}
	}

	public interface DerivationStrategyLine
	{
		KeyPath Path
		{
			get;
		}

		Derivation Derive(uint i);
	}
}
