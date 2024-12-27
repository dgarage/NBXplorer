using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace NBXplorer.Models
{
	public abstract class TrackedSource
	{
		public static bool TryParse(string str, out TrackedSource trackedSource, NBXplorerNetwork network)
		{
			if (str == null)
				throw new ArgumentNullException(nameof(str));
			trackedSource = null;
			var strSpan = str.AsSpan();
			if (strSpan.StartsWith("DERIVATIONSCHEME:".AsSpan(), StringComparison.Ordinal))
			{
				if (network is null)
					return false;
				if (!DerivationSchemeTrackedSource.TryParse(strSpan, out var derivationSchemeTrackedSource, network))
					return false;
				trackedSource = derivationSchemeTrackedSource;
			}
			else if (strSpan.StartsWith("ADDRESS:".AsSpan(), StringComparison.Ordinal))
			{
				if (network is null)
					return false;
				if (!AddressTrackedSource.TryParse(strSpan, out var addressTrackedSource, network.NBitcoinNetwork))
					return false;
				trackedSource = addressTrackedSource;
			}
			else if (strSpan.StartsWith("GROUP:".AsSpan(), StringComparison.Ordinal))
			{
				if (!GroupTrackedSource.TryParse(strSpan, out var walletTrackedSource))
					return false;
				trackedSource = walletTrackedSource;
			}
			else
			{
				return false;
			}
			return true;
		}

		public override bool Equals(object obj)
		{
			TrackedSource item = obj as TrackedSource;
			if (item == null)
				return false;
			return ToString().Equals(item.ToString());
		}
		public static bool operator ==(TrackedSource a, TrackedSource b)
		{
			if (System.Object.ReferenceEquals(a, b))
				return true;
			if (((object)a == null) || ((object)b == null))
				return false;
			return a.ToString() == b.ToString();
		}

		public static bool operator !=(TrackedSource a, TrackedSource b)
		{
			return !(a == b);
		}

		public override int GetHashCode()
		{
			return ToString().GetHashCode();
		}

		public static DerivationSchemeTrackedSource Create(DerivationStrategyBase strategy)
		{
			if (strategy == null)
				throw new ArgumentNullException(nameof(strategy));
			return new DerivationSchemeTrackedSource(strategy);
		}
		public static AddressTrackedSource Create(BitcoinAddress address)
		{
			if (address == null)
				throw new ArgumentNullException(nameof(address));
			return new AddressTrackedSource(address);
		}
		public static AddressTrackedSource Create(Script scriptPubKey, Network network)
		{
			if (scriptPubKey == null)
				throw new ArgumentNullException(nameof(scriptPubKey));
			if (network == null)
				throw new ArgumentNullException(nameof(network));

			var address = scriptPubKey.GetDestinationAddress(network);
			if (address == null)
				throw new ArgumentException(paramName: nameof(scriptPubKey), message: $"{nameof(scriptPubKey)} can't be translated on an address on {network.Name}");
			return new AddressTrackedSource(address);
		}

		public virtual string ToPrettyString()
		{
			return ToString();
		}

		public static TrackedSource Parse(string str, NBXplorerNetwork network)
		{
			if (!TryParse(str, out var trackedSource, network))
				throw new FormatException("Invalid TrackedSource");
			return trackedSource;
		}
	}

	public class GroupTrackedSource : TrackedSource
	{
		public string GroupId { get; }

		public static GroupTrackedSource Generate()
		{
			Span<byte> r = stackalloc byte[13];
			// 13 is most consistent on number of chars and more than we need to avoid generating twice same id
			RandomNumberGenerator.Fill(r);
			return new GroupTrackedSource(Encoders.Base58.EncodeData(r));
		}

		public GroupTrackedSource(string groupId)
		{
			GroupId = groupId;
		}

		public static bool TryParse(ReadOnlySpan<char> trackedSource, out GroupTrackedSource walletTrackedSource)
		{
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			walletTrackedSource = null;
			if (!trackedSource.StartsWith("GROUP:".AsSpan(), StringComparison.Ordinal))
				return false;
			try
			{
				walletTrackedSource = new GroupTrackedSource(trackedSource.Slice("GROUP:".Length).ToString());
				return true;
			}
			catch { return false; }
		}

		public override string ToString()
		{
			return "GROUP:" + GroupId;
		}
		public override string ToPrettyString()
		{
			return "G:" + GroupId;
		}

		public static GroupTrackedSource Parse(string trackedSource)
		{
			return TryParse(trackedSource, out var g) ? g : throw new FormatException("Invalid group tracked source format");
		}
	}

	public class AddressTrackedSource : TrackedSource, IDestination
	{
		// Note that we should in theory access BitcoinAddress. But parsing BitcoinAddress is very expensive, so we keep storing plain strings
		public AddressTrackedSource(BitcoinAddress address)
		{
			if (address == null)
				throw new ArgumentNullException(nameof(address));
			_FullAddressString = "ADDRESS:" + address;
			Address = address;
		}

		string _FullAddressString;

		public BitcoinAddress Address
		{
			get;
		}

		public Script ScriptPubKey => Address.ScriptPubKey;

		public static bool TryParse(ReadOnlySpan<char> strSpan, out TrackedSource addressTrackedSource, Network network)
		{
			if (strSpan == null)
				throw new ArgumentNullException(nameof(strSpan));
			if (network == null)
				throw new ArgumentNullException(nameof(network));
			addressTrackedSource = null;
			if (!strSpan.StartsWith("ADDRESS:".AsSpan(), StringComparison.Ordinal))
				return false;
			try
			{
				addressTrackedSource = new AddressTrackedSource(BitcoinAddress.Create(strSpan.Slice("ADDRESS:".Length).ToString(), network));
				return true;
			}
			catch { return false; }
		}

		public override string ToString()
		{
			return _FullAddressString;
		}

		public override string ToPrettyString()
		{
			return Address.ToString();
		}
	}

	public class DerivationSchemeTrackedSource : TrackedSource
	{
		public DerivationSchemeTrackedSource(DerivationStrategy.DerivationStrategyBase derivationStrategy)
		{
			if (derivationStrategy == null)
				throw new ArgumentNullException(nameof(derivationStrategy));
			DerivationStrategy = derivationStrategy;
		}

		public DerivationStrategy.DerivationStrategyBase DerivationStrategy { get; }

		public static bool TryParse(ReadOnlySpan<char> strSpan, out DerivationSchemeTrackedSource derivationSchemeTrackedSource, NBXplorerNetwork network)
		{
			if (strSpan == null)
				throw new ArgumentNullException(nameof(strSpan));
			if (network == null)
				throw new ArgumentNullException(nameof(network));
			derivationSchemeTrackedSource = null;
			if (!strSpan.StartsWith("DERIVATIONSCHEME:".AsSpan(), StringComparison.Ordinal))
				return false;
			try
			{
				var factory = network.DerivationStrategyFactory;
				var derivationScheme = factory.Parse(strSpan.Slice("DERIVATIONSCHEME:".Length).ToString());
				derivationSchemeTrackedSource = new DerivationSchemeTrackedSource(derivationScheme);
				return true;
			}
			catch { return false; }
		}

		public override string ToString()
		{
			return "DERIVATIONSCHEME:" + DerivationStrategy.ToString();
		}
		public override string ToPrettyString()
		{
			var strategy = DerivationStrategy.ToString();
			if (strategy.Length > 35)
			{
				strategy = strategy.Substring(0, 10) + "..." + strategy.Substring(strategy.Length - 20);
			}
			return strategy;
		}

		public IEnumerable<DerivationFeature> GetDerivationFeatures(KeyPathTemplates keyPathTemplates)
		=> keyPathTemplates.GetSupportedDerivationFeatures();
	}
}
