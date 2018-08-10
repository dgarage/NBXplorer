using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Reflection;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.ModelBinders
{
    public class UInt256ModelBinding : IModelBinder
    {
		#region IModelBinder Members

		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
            if(!typeof(uint256).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
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
				bindingContext.Result = ModelBindingResult.Success(null);
				return Task.CompletedTask;
			}
            var value = uint256.Parse(key);
			bindingContext.Result = ModelBindingResult.Success(value);
			return Task.CompletedTask;
		}

        #endregion
    }

	public class UInts256ModelBinding : IModelBinder
	{
		#region IModelBinder Members

		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			if (!typeof(HashSet<uint256>).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
			{
				return Task.CompletedTask;
			}

			ValueProviderResult val = bindingContext.ValueProvider.GetValue(
				bindingContext.ModelName);
			if (val == null)
			{
				return Task.CompletedTask;
			}

			string key = val.FirstValue as string;
			if (key == null)
			{
				bindingContext.Result = ModelBindingResult.Success(null);
				return Task.CompletedTask;
			}

			var values = key
				.Split(',', StringSplitOptions.RemoveEmptyEntries)
				.Select(c => uint256.Parse(c))
				.ToHashSet();

			bindingContext.Result = ModelBindingResult.Success(values);			
			return Task.CompletedTask;
		}

		#endregion
	}

	public class UInt160ModelBinding : IModelBinder
    {
		#region IModelBinder Members

		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
            if(!typeof(uint160).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
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
                bindingContext.Model = null;
				return Task.CompletedTask;
			}
            var value = uint160.Parse(key);
            if(value.ToString().StartsWith(uint160.Zero.ToString()))
                throw new FormatException("Invalid hash format");
			bindingContext.Result = ModelBindingResult.Success(value);
			return Task.CompletedTask;
		}

        #endregion
    }
}
