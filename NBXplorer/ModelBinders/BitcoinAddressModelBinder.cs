﻿using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBitcoin;
using System.Reflection;
using System;
using System.Threading.Tasks;

namespace NBXplorer.ModelBinders
{
	public class BitcoinAddressModelBinder : IModelBinder
	{
		public BitcoinAddressModelBinder()
		{

		}

		#region IModelBinder Members

		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			if (!typeof(BitcoinAddress).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
			{
				return Task.CompletedTask;
			}

			ValueProviderResult val = bindingContext.ValueProvider.GetValue(
				bindingContext.ModelName);

			string key = val.FirstValue as string;
			if (key == null)
			{
				return Task.CompletedTask;
			}

			var networkProvider = (NBXplorer.NBXplorerNetworkProvider)bindingContext.HttpContext.RequestServices.GetService(typeof(NBXplorer.NBXplorerNetworkProvider));
			var cryptoCode = bindingContext.ValueProvider.GetValue("cryptoCode").FirstValue;
			var network = networkProvider.GetFromCryptoCode(cryptoCode ?? "BTC");
			try
			{
				var data = BitcoinAddress.Create(key, network.NBitcoinNetwork);
				if (!bindingContext.ModelType.IsInstanceOfType(data))
				{
					throw new FormatException("Invalid address");
				}
				bindingContext.Result = ModelBindingResult.Success(data);
			}
			catch { throw new FormatException("Invalid address"); }
			return Task.CompletedTask;
		}

		#endregion
	}
}
