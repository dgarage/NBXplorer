using NBitcoin;
using NBitcoin.Crypto;
using System;
using System.Collections.Generic;
using System.Linq;
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
		public Dictionary<string, bool> AdditionalOptions { get; }

		internal DerivationStrategyBase(Dictionary<string,bool> additionalOptions)
		{
			AdditionalOptions = additionalOptions?? new Dictionary<string, bool>();
		}

		public DerivationLine GetLineFor(KeyPathTemplate keyPathTemplate)
		{
			return new DerivationLine(this, keyPathTemplate);
		}
		public abstract DerivationStrategyBase GetChild(KeyPath keyPath);

		public Derivation GetDerivation(uint i)
		{
			return GetChild(new KeyPath(i)).GetDerivation();
		}
		public Derivation GetDerivation(KeyPath keyPath)
		{
			if (keyPath == null || keyPath.Length == 0)
				return GetDerivation();
			return GetChild(keyPath).GetDerivation();
		}
		public abstract Derivation GetDerivation();

		protected abstract string StringValueCore
		{
			get;
		}
		
		public string StringValue => $"{StringValueCore}{GetSuffixOptionsString()}";

		private string GetSuffixOptionsString()
		{
			return string.Join("", AdditionalOptions.Where(pair => pair.Value).Select(pair => $"-[{pair.Key}]"));
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

		Script IHDScriptPubKey.ScriptPubKey => GetDerivation().ScriptPubKey;
		IHDScriptPubKey IHDScriptPubKey.Derive(KeyPath keyPath)
		{
			return GetChild(keyPath);
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

		DerivationStrategyBase _PreLine;

		public Derivation Derive(uint index)
		{
			_PreLine = _PreLine ?? DerivationStrategyBase.GetChild(KeyPathTemplate.PreIndexes);
			return _PreLine.GetDerivation(new KeyPath(index).Derive(KeyPathTemplate.PostIndexes));
		}
	}
}
