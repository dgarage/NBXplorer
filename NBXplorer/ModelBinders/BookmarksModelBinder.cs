using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Reflection;
using NBitcoin;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.ModelBinders
{
	public class BookmarksModelBinding : IModelBinder
	{
		#region IModelBinder Members

		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			if(!typeof(HashSet<Bookmark>).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
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

			var values = key
				.Split(',', StringSplitOptions.RemoveEmptyEntries)
				.Select(c => new Bookmark(uint160.Parse(c)))
				.ToHashSet();
			bindingContext.Result = ModelBindingResult.Success(values);
			return Task.CompletedTask;
		}

		#endregion
	}

	public class BookmarkModelBinding : IModelBinder
	{
		#region IModelBinder Members

		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			if(!typeof(Bookmark).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
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
			bindingContext.Result = ModelBindingResult.Success(new Bookmark(uint160.Parse(key)));
			return Task.CompletedTask;
		}

		#endregion
	}
}
