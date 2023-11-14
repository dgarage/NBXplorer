using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Reflection;
using NBitcoin;
using System.Threading.Tasks;
using NBitcoin.DataEncoders;

namespace NBXplorer.ModelBinders
{
	public class ScriptModelBinder : IModelBinder
	{
		#region IModelBinder Members

		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			if (!typeof(Script).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
			{
				return Task.CompletedTask;
			}

			ValueProviderResult val = bindingContext.ValueProvider.GetValue(
				bindingContext.ModelName);

			string key = val.FirstValue as string;
			if (key == null)
			{
				bindingContext.Model = null;
				return Task.CompletedTask;
			}
			
			var value = new Script(Encoders.Hex.DecodeData(key));

			bindingContext.Result = ModelBindingResult.Success(value);
			return Task.CompletedTask;
		}

		#endregion
	}	
}
