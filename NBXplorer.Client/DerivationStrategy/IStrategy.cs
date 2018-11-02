using NBitcoin;
using NBitcoin.Crypto;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.DerivationStrategy
{
	public enum DerivationFeature
	{
		Change =  1,
		Deposit = 0,
        	Direct =  2  
    	}
	public abstract class DerivationStrategyBase
	{
		internal DerivationStrategyBase()
		{

		}

		public static KeyPath GetKeyPath(DerivationFeature derivationFeature)
		{
			return derivationFeature == DerivationFeature.Direct ? new KeyPath() : new KeyPath((uint)derivationFeature);
		}
		public static DerivationFeature GetFeature(KeyPath path)
		{
			if (path == null)
				return DerivationFeature.Deposit;
			return path.Indexes.Length == 1 ? DerivationFeature.Direct : (DerivationFeature)path.Indexes[0];
		}
		public DerivationStrategyBase GetLineFor(DerivationFeature derivationFeature)
		{
            		return derivationFeature == DerivationFeature.Direct ? this :
                    		GetLineFor(GetKeyPath(derivationFeature));
        	}

		public abstract DerivationStrategyBase GetLineFor(KeyPath keyPath);

		public Derivation Derive(uint i)
		{
			return Derive(new KeyPath(i));
		}
		public abstract Derivation Derive(KeyPath keyPath);

		protected abstract string StringValue
		{
			get;
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
}
