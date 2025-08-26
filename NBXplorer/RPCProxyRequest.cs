#nullable enable
using NBitcoin.RPC;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using static NBXplorer.RPCProxyRequest;

namespace NBXplorer
{
	public class RPCProxyRequest
	{
		public class RPCProxyBatchedRequest : RPCProxyRequest
		{
			RPCProxyBatchedRequest(List<RPCRequest> requests)
			{
				Requests = requests;
			}

			internal static RPCProxyBatchedRequest? TryLoad(JArray arr)
			{
				List<RPCRequest> requests = new(arr.Count);
				foreach (var el in arr)
				{
					if (el is JObject obj && RPCProxySingleRequest.TryLoad(obj) is { } r)
						requests.Add(r.Request);
					else
						return null;
				}
				return new RPCProxyBatchedRequest(requests);
			}
			public List<RPCRequest> Requests { get; }
		}
		public class RPCProxySingleRequest : RPCProxyRequest
		{

			RPCProxySingleRequest(RPCRequest r)
			{
				this.Request = r;
			}

			internal static RPCProxySingleRequest? TryLoad(JObject jobj)
			{
				var r = new RPCRequest();
				r.ThrowIfRPCError = false;
				if (jobj["jsonrpc"] is { Type: JTokenType.String } s)
					r.JsonRpc = s.Value<string>();
				if (jobj["id"] is { Type: JTokenType.Integer } i)
					r.Id = i.Value<int>();
				else if (jobj["id"] is { Type: JTokenType.String } i2 && int.TryParse(i2.Value<string>(), out var i22))
					r.Id = i22;
				if (jobj["method"] is { Type: JTokenType.String } m)
					r.Method = m.Value<string>();
				else
					return null;
				if (jobj["params"] is JArray arr)
				{
					r.Params = arr.Select(a => a.DeepClone()).ToArray();
				}
				return new RPCProxySingleRequest(r);
			}

			public RPCRequest Request { get; }
		}
		public static RPCProxyRequest? TryParse(string? str)
		{
			if (string.IsNullOrEmpty(str))
				return null;
			JToken token;
			try
			{
				token = JToken.Parse(str);
			}
			catch { return null; }

			if (token is JArray arr)
				return RPCProxyBatchedRequest.TryLoad(arr);
			else if (token is JObject jobj)
				return RPCProxySingleRequest.TryLoad(jobj);
			else
				return null;
		}
	}
}
