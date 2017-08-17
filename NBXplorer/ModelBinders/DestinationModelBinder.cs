using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBitcoin;
using System.Reflection;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Internal;

namespace ElementsExplorer.ModelBinders
{
	public class DestinationModelBinder : IModelBinder
	{
		public DestinationModelBinder()
		{

		}

		#region IModelBinder Members

		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			if(!
				(typeof(Base58Data).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType) ||
				typeof(IDestination).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType)))
			{
				return TaskCache.CompletedTask;
			}

			ValueProviderResult val = bindingContext.ValueProvider.GetValue(
				bindingContext.ModelName);
			if(val == null)
			{
				return TaskCache.CompletedTask;
			}

			string key = val.FirstValue as string;
			if(key == null)
			{
				return TaskCache.CompletedTask;
			}

			var network = (Network)bindingContext.HttpContext.RequestServices.GetService(typeof(Network));
			var data = Network.Parse(key, network);
			if(!bindingContext.ModelType.IsInstanceOfType(data))
			{
				throw new FormatException("Invalid destination type");
			}
			bindingContext.Result = ModelBindingResult.Success(data);
			return TaskCache.CompletedTask;
		}

		#endregion
	}
}
