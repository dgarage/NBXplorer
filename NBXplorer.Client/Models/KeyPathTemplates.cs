using NBitcoin;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;

namespace NBXplorer
{
	public class KeyPathTemplates
	{
		private static readonly KeyPathTemplate depositKeyPathTemplate = KeyPathTemplate.Parse("0/*");
		private static readonly KeyPathTemplate changeKeyPathTemplate = KeyPathTemplate.Parse("1/*");
		private static readonly KeyPathTemplate directKeyPathTemplate = KeyPathTemplate.Parse("*");
		private readonly KeyPathTemplate customKeyPathTemplate;
		private static readonly KeyPathTemplates _Default = new KeyPathTemplates();
		private readonly DerivationFeature[] derivationFeatures;

		public static KeyPathTemplates Default
		{
			get
			{
				return _Default;
			}
		}

		private KeyPathTemplates() : this(null)
		{

		}
		public KeyPathTemplates(KeyPathTemplate customKeyPathTemplate)
		{
			this.customKeyPathTemplate = customKeyPathTemplate;
			List<DerivationFeature> derivationFeatures = new List<DerivationFeature>();
			derivationFeatures.Add(DerivationFeature.Deposit);
			derivationFeatures.Add(DerivationFeature.Change);
			derivationFeatures.Add(DerivationFeature.Direct);
			if (customKeyPathTemplate != null)
				derivationFeatures.Add(DerivationFeature.Custom);
			this.derivationFeatures = derivationFeatures.ToArray();
		}

		public KeyPathTemplate GetKeyPathTemplate(DerivationFeature derivationFeature)
		{
			switch (derivationFeature)
			{
				case DerivationFeature.Deposit:
					return depositKeyPathTemplate;
				case DerivationFeature.Change:
					return changeKeyPathTemplate;
				case DerivationFeature.Direct:
					return directKeyPathTemplate;
				case DerivationFeature.Custom when customKeyPathTemplate != null:
					return customKeyPathTemplate;
				default:
					throw new NotSupportedException(derivationFeature.ToString());
			}
		}

		public IEnumerable<DerivationFeature> GetSupportedDerivationFeatures()
		{
			return derivationFeatures;
		}
	}
}
