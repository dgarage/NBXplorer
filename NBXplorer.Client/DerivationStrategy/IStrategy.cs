using NBitcoin;
using NBitcoin.Crypto;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
		ReadOnlyDictionary<string, bool> Empty = new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>(0));
		public ReadOnlyDictionary<string, bool> AdditionalOptions { get; }

		internal DerivationStrategyBase(ReadOnlyDictionary<string,bool> additionalOptions)
		{
			AdditionalOptions = additionalOptions ?? Empty;
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

		protected internal abstract string StringValueCore
		{
			get;
		}

		string _StringValue;
		string StringValue
		{
			get
			{
				if (_StringValue == null)
				{
					if (AdditionalOptions.Count == 0)
						_StringValue = StringValueCore;
					else
						_StringValue = $"{StringValueCore}{GetSuffixOptionsString()}";
				}
				return _StringValue;
			}
		}

		private string GetSuffixOptionsString()
		{
			return string.Join("", new SortedDictionary<string, bool>(AdditionalOptions).Where(pair => pair.Value).Select(pair => $"-[{pair.Key}]"));
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

		class HDRedeemScriptPubKey : IHDScriptPubKey
		{
			private readonly DerivationStrategyBase strategyBase;
			public HDRedeemScriptPubKey(DerivationStrategyBase strategyBase)
			{
				this.strategyBase = strategyBase;
			}
			public Script ScriptPubKey => strategyBase.GetDerivation().Redeem;

			public bool CanDeriveHardenedPath()
			{
				return strategyBase.CanDeriveHardenedPath();
			}
			public IHDScriptPubKey Derive(KeyPath keyPath)
			{
				return strategyBase.GetChild(keyPath).AsHDRedeemScriptPubKey();
			}
		}
		public IHDScriptPubKey AsHDRedeemScriptPubKey()
		{
			return new HDRedeemScriptPubKey(this);
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
