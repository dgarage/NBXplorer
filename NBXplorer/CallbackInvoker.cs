using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NBXplorer.Models;
using NBitcoin;
using System.Text;
using System.Net;

namespace NBXplorer
{
    public class CallbackInvoker
    {
		public CallbackInvoker(Serializer serializer, Repository repo)
		{
			Repository = repo;
			_Serializer = serializer;
			ServicePointManager.DefaultConnectionLimit = 100;
		}
		Serializer _Serializer;
		public Repository Repository
		{
			get; set;
		}

		public async Task SendCallbacks(TransactionMatch[] matches)
		{
			List<HttpRequestMessage> requests = new List<HttpRequestMessage>();
			foreach(var match in matches)
			{
				var callbacks = await Repository.GetCallbacks(match.DerivationStrategy);
				foreach(var callback in callbacks)
				{
					requests.Add(PrepareRequest(match, callback));
				}
			}

			await SendRequests(requests.ToArray());
		}

		public async Task SendCallbacks(uint256 blockHash)
		{
			var callbacks = await Repository.GetBlockCallbacks();
			await SendRequests(callbacks.Select(PrepareRequest).ToArray());
		}


		private async Task SendRequests(HttpRequestMessage[] requests)
		{
			if(requests.Length == 0)
				return;
			using(var client = new HttpClient()) //Not performance friendly, but is OK not path critical
			{
				client.Timeout = TimeSpan.FromSeconds(10);
				try
				{
					await Task.WhenAll(requests.Select(r => client.SendAsync(r)));
				}
				catch { } //No fuck given to errors here, fire and forget
			}
		}

		static Encoding Encoding = new UTF8Encoding(false);
		HttpRequestMessage PrepareRequest(TransactionMatch match, Uri callback)
		{
			HttpRequestMessage message = new HttpRequestMessage();
			message.Method = HttpMethod.Post;
			message.RequestUri = callback;
			message.Content = new StringContent(_Serializer.ToString(match), Encoding, "application/json");
			return message;
		}

		HttpRequestMessage PrepareRequest(Uri callback)
		{
			HttpRequestMessage message = new HttpRequestMessage();
			message.Method = HttpMethod.Post;
			message.RequestUri = callback;
			return message;
		}
	}
}
