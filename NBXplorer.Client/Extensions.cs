﻿using NBitcoin;
using NBitcoin.RPC;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer
{
    public static class ExtensionsClient
    {
		public static IEnumerable<IList<T>> Batch<T>(this IEnumerable<T> values, int size)
		{
			if (size <= 0)
				throw new ArgumentOutOfRangeException(nameof(size));
			if (values == null)
				throw new ArgumentNullException(nameof(values));
			if (values is IList<T> l && l.Count <= size)
			{
				yield return l;
				yield break;
			}
			var batch = new List<T>();
			foreach(var v in values)
			{
				batch.Add(v);
				if(size == batch.Count)
				{
					yield return batch;
					batch = new List<T>();
				}
			}
			if(batch.Count != 0)
			{
				yield return batch;
			}
		}

		public static async Task CloseSocket(this WebSocket socket, WebSocketCloseStatus status, string statusDescription, CancellationToken cancellation = default)
		{
			try
			{
				if(socket.State == WebSocketState.Open)
				{
					using(CancellationTokenSource cts = new CancellationTokenSource())
					{
						cts.CancelAfter(5000);
						using(var cts2 = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellation))
						{
							try
							{
								await socket.CloseAsync(status, statusDescription, cts2.Token).ConfigureAwait(false);
							}
							catch(ObjectDisposedException) { }
						}
					}
				}
			}
			catch { }
			finally { socket.Dispose(); }
		}

		public static async Task<uint256[]> EnsureGenerateAsync(this RPCClient client, int blockCount)
		{
			uint256[] blockIds = new uint256[blockCount];
			int generated = 0;
			while(generated < blockCount)
			{
				foreach(var id in await client.GenerateAsync(blockCount - generated).ConfigureAwait(false))
				{
					blockIds[generated++] = id;
				}
			}
			return blockIds;
		}
	}
}
