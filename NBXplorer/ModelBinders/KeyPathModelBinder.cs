using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBitcoin;
using System.Reflection;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Internal;
using NBXplorer.DerivationStrategy;

namespace NBXplorer.ModelBinders
{
	public class KeyPathModelBinder : IModelBinder
	{
		public KeyPathModelBinder()
		{

		}

		#region IModelBinder Members

		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			if(!typeof(KeyPath).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
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
			try
			{
				var data = KeyPath.Parse(key);
				bindingContext.Result = ModelBindingResult.Success(data);
			}
			catch { throw new FormatException("Invalid key path"); }
			return Task.CompletedTask;
		}

		#endregion
	}
}
