using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.Models
{
	public abstract class TrackedSource
	{
		public static bool TryParse(string str, out TrackedSource trackedSource, Network network)
		{
			if (str == null)
				throw new ArgumentNullException(nameof(str));
			if (network == null)
				throw new ArgumentNullException(nameof(network));
			trackedSource = null;
			var strSpan = str.AsSpan();
			if (strSpan.StartsWith("DERIVATIONSCHEME:".AsSpan(), StringComparison.Ordinal))
			{
				if (!DerivationSchemeTrackedSource.TryParse(strSpan, out var derivationSchemeTrackedSource, network))
					return false;
				trackedSource = derivationSchemeTrackedSource;
			}
			else if (strSpan.StartsWith("ADDRESS:".AsSpan(), StringComparison.Ordinal))
			{
				if (!AddressTrackedSource.TryParse(strSpan, out var addressTrackedSource, network))
					return false;
				trackedSource = addressTrackedSource;
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

		public static bool TryParse(ReadOnlySpan<char> strSpan, out DerivationSchemeTrackedSource derivationSchemeTrackedSource, Network network)
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
				var factory = new DerivationStrategy.DerivationStrategyFactory(network);
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
	}
}
