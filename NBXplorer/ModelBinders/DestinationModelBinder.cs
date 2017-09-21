using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBitcoin;
using System.Reflection;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Internal;
using NBXplorer.DerivationStrategy;

namespace NBXplorer.ModelBinders
{
	public class DestinationModelBinder : IModelBinder
	{
		public DestinationModelBinder()
		{

		}

		#region IModelBinder Members

		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			if(!typeof(IDerivationStrategy).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
			{
				return Task.CompletedTask;
			}

			ValueProviderResult val = bindingContext.ValueProvider.GetValue(
				bindingContext.ModelName);
			if(val == null)
			{
				return Task.CompletedTask;
			}

			string key = val.FirstValue as string;
			if(key == null)
			{
				return Task.CompletedTask;
			}

			var network = (Network)bindingContext.HttpContext.RequestServices.GetService(typeof(Network));
			var data = new DerivationStrategy.DerivationStrategyFactory(network).Parse(key);
			if(!bindingContext.ModelType.IsInstanceOfType(data))
			{
				throw new FormatException("Invalid destination type");
			}
			bindingContext.Result = ModelBindingResult.Success(data);
			return Task.CompletedTask;
		}

		#endregion
	}
}
