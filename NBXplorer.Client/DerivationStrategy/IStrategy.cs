#nullable enable
using NBitcoin;
#if !NO_RECORD
using NBitcoin.WalletPolicies;
#endif
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NBXplorer.DerivationStrategy
{
	public enum DerivationFeature
	{
		Change =  1,
		Deposit = 0,
		Direct = 2,
		Custom = 3,
	}

	public abstract class StandardDerivationStrategyBase : DerivationStrategyBase, IHDScriptPubKey
	{
		internal StandardDerivationStrategyBase(ReadOnlyDictionary<string, string> additionalOptions) : base(additionalOptions)
		{
		}
		public abstract Derivation GetDerivation(KeyPath keyPath);
		public override DerivationLine GetLineFor(KeyPathTemplates keyPathTemplates, DerivationFeature feature)
		=> new KeyPathTemplateDerivationLine(this, keyPathTemplates, feature);
		Script IHDScriptPubKey.ScriptPubKey => GetDerivation(KeyPath.Empty).ScriptPubKey;
		IHDScriptPubKey? IHDScriptPubKey.Derive(KeyPath keyPath) => keyPath.IsHardenedPath ? null : new HDScriptPubKey(this, keyPath);
		class HDScriptPubKey(StandardDerivationStrategyBase Parent, KeyPath KeyPath) : IHDScriptPubKey
		{
			public Script ScriptPubKey => Parent.GetDerivation(KeyPath).ScriptPubKey;
			public IHDScriptPubKey? Derive(KeyPath keyPath) => KeyPath.IsHardenedPath ? null : new HDScriptPubKey(Parent, KeyPath.Derive(keyPath));
		}
	}
	public abstract class DerivationStrategyBase
	{
		readonly ReadOnlyDictionary<string, string> Empty = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(0));
		public ReadOnlyDictionary<string, string> AdditionalOptions { get; }

		internal DerivationStrategyBase(ReadOnlyDictionary<string,string>? additionalOptions)
		{
			AdditionalOptions = additionalOptions ?? Empty;
		}

		public DerivationLine GetLineFor(DerivationFeature feature) => GetLineFor(KeyPathTemplates.Default, feature);
		public abstract DerivationLine GetLineFor(KeyPathTemplates keyPathTemplates, DerivationFeature feature);

		protected internal abstract string StringValueCore
		{
			get;
		}

		string? _StringValue;
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
			return string.Join("", new SortedDictionary<string, string>(AdditionalOptions).Select(pair => $"-[{pair.Key}{(string.IsNullOrEmpty(pair.Value)?string.Empty: $"={pair.Value}")}]"));
		}
#nullable enable
		public override bool Equals(object? obj) => obj is DerivationStrategyBase o && StringValue.Equals(o.StringValue);
		public static bool operator ==(DerivationStrategyBase? a, DerivationStrategyBase? b) => a is null ? b is null : a.Equals(b);
		public static bool operator !=(DerivationStrategyBase? a, DerivationStrategyBase? b) => !(a == b);
		public override int GetHashCode() => StringValue.GetHashCode();
#nullable restore

		public abstract IEnumerable<ExtPubKey> GetExtPubKeys();

		public override string ToString()
		{
			return StringValue;
		}
	}

#if !NO_RECORD
	public class MiniscriptDerivationLine : DerivationLine
	{
		public MiniscriptDerivationLine(PolicyDerivationStrategy derivationStrategy, DerivationFeature derivationFeature) : base(derivationFeature)
		{
			DerivationStrategy = derivationStrategy;
			Intent = ToAddressIntent(derivationFeature);
		}

		public static AddressIntent ToAddressIntent(DerivationFeature derivationFeature)
		{
			return derivationFeature switch
			{
				DerivationFeature.Change => AddressIntent.Change,
				DerivationFeature.Deposit => AddressIntent.Deposit,
				_ => throw new NotSupportedException("MiniscriptDerivationStrategy only support deposit and change features")
			};
		}

		public PolicyDerivationStrategy DerivationStrategy { get; }
		public AddressIntent Intent { get; }

		public override Derivation Derive(uint index) => DerivationStrategy.GetDerivation(Intent, index);
	}
#endif
	public abstract class DerivationLine
	{
		protected DerivationLine(DerivationFeature feature)
		{
			Feature = feature;
		}
		public DerivationFeature Feature { get; }
		public abstract Derivation Derive(uint index);
	}
	public class KeyPathTemplateDerivationLine : DerivationLine
	{
		public KeyPathTemplateDerivationLine(StandardDerivationStrategyBase derivationStrategyBase, KeyPathTemplates keyPathTemplates, DerivationFeature derivationFeature) : base(derivationFeature)
		{
			if (derivationStrategyBase == null)
				throw new ArgumentNullException(nameof(derivationStrategyBase));
			if (keyPathTemplates == null)
				throw new ArgumentNullException(nameof(keyPathTemplates));
			DerivationStrategyBase = derivationStrategyBase;
			KeyPathTemplate = keyPathTemplates.GetKeyPathTemplate(derivationFeature);
		}

		public StandardDerivationStrategyBase DerivationStrategyBase { get; }
		public KeyPathTemplate KeyPathTemplate { get; }

		public override Derivation Derive(uint index)
		{
			var kp = KeyPathTemplate.GetKeyPath(index);
			return DerivationStrategyBase.GetDerivation(kp);
		}
	}
}
