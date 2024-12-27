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
		public static KeyPathTemplates Default => _Default;

		private KeyPathTemplates() : this(null)
		{

		}
		public KeyPathTemplates(KeyPathTemplate customKeyPathTemplate)
		{
			this.customKeyPathTemplate = customKeyPathTemplate;
			List<DerivationFeature> derivationFeatures = new List<DerivationFeature>
			{
				DerivationFeature.Deposit,
				DerivationFeature.Change,
				DerivationFeature.Direct
			};
			if (customKeyPathTemplate != null)
				derivationFeatures.Add(DerivationFeature.Custom);
			this.derivationFeatures = derivationFeatures.ToArray();
		}

		public KeyPathTemplate GetKeyPathTemplate(DerivationFeature derivationFeature)
			=> derivationFeature switch
			{
				DerivationFeature.Deposit => depositKeyPathTemplate,
				DerivationFeature.Change => changeKeyPathTemplate,
				DerivationFeature.Direct => directKeyPathTemplate,
				DerivationFeature.Custom when customKeyPathTemplate != null => customKeyPathTemplate,
				_ => throw new NotSupportedException(derivationFeature.ToString())
			};

		public IEnumerable<DerivationFeature> GetSupportedDerivationFeatures() => derivationFeatures;
	}
}
