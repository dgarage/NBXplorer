using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBitcoin;
using System.Reflection;
using System;
using System.Threading.Tasks;
using NBXplorer.DerivationStrategy;

namespace NBXplorer.ModelBinders
{
	public class DerivationStrategyModelBinder : IModelBinder
	{
		public DerivationStrategyModelBinder()
		{

		}

		#region IModelBinder Members

		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			if(!typeof(DerivationStrategyBase).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
			{
				return Task.CompletedTask;
			}

			ValueProviderResult val = bindingContext.ValueProvider.GetValue(
				bindingContext.ModelName);

			string key = val.FirstValue as string;
			if(key == null)
			{
				return Task.CompletedTask;
			}

			var networkProvider = (NBXplorer.NBXplorerNetworkProvider)bindingContext.HttpContext.RequestServices.GetService(typeof(NBXplorer.NBXplorerNetworkProvider));
			var cryptoCode = bindingContext.ValueProvider.GetValue("cryptoCode").FirstValue;
			cryptoCode = cryptoCode ?? bindingContext.ValueProvider.GetValue("network").FirstValue;
			var network = networkProvider.GetFromCryptoCode((cryptoCode ?? "BTC"));
			try
			{
				var data = network.DerivationStrategyFactory.Parse(key);
				if(!bindingContext.ModelType.IsInstanceOfType(data))
				{
					throw new FormatException("Invalid destination type");
				}
				bindingContext.Result = ModelBindingResult.Success(data);
			}
			catch { throw new FormatException("Invalid derivation scheme"); }
			return Task.CompletedTask;
		}

		#endregion
	}
}
