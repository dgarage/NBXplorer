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
		Direct = 2,
		Custom = 3,
	}
	public abstract class DerivationStrategyBase : IHDScriptPubKey
	{
		internal DerivationStrategyBase()
		{

		}

		public DerivationLine GetLineFor(KeyPathTemplate keyPathTemplate)
		{
			return new DerivationLine(this, keyPathTemplate);
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

		public abstract IEnumerable<ExtPubKey> GetExtPubKeys();

		public override int GetHashCode()
		{
			return StringValue.GetHashCode();
		}

		public override string ToString()
		{
			return StringValue;
		}

		Script IHDScriptPubKey.ScriptPubKey => Derive(new KeyPath()).ScriptPubKey;
		IHDScriptPubKey IHDScriptPubKey.Derive(KeyPath keyPath)
		{
			return GetLineFor(keyPath);
		}

		public bool CanDeriveHardenedPath()
		{
			return false;
		}
	}

	public class DerivationLine
	{
		public DerivationLine(DerivationStrategyBase derivationStrategyBase, KeyPathTemplate keyPathTemplate)
		{
			if (derivationStrategyBase == null)
				throw new ArgumentNullException(nameof(derivationStrategyBase));
			if (keyPathTemplate == null)
				throw new ArgumentNullException(nameof(keyPathTemplate));
			DerivationStrategyBase = derivationStrategyBase;
			KeyPathTemplate = keyPathTemplate;
		}

		public DerivationStrategyBase DerivationStrategyBase { get; }
		public KeyPathTemplate KeyPathTemplate { get; }

		public Derivation Derive(uint index)
		{
			return DerivationStrategyBase.Derive(KeyPathTemplate.GetKeyPath(index));
		}
	}
}
