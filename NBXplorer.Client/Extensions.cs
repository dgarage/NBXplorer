using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer
{
    public static class ExtensionsClient
    {
		public static IEnumerable<T[]> Batch<T>(this IEnumerable<T> values, int size)
		{
			var batch = new T[size];
			int index = 0;
			foreach(var v in values)
			{
				batch[index++] = v;
				if(index == batch.Length)
				{
					yield return batch;
					batch = new T[size];
					index = 0;
				}
			}
			if(index != 0)
			{
				Array.Resize(ref batch, index);
				yield return batch;
			}
		}

		public static ArraySegment<T> Slice<T>(this ArraySegment<T> array, int index)
		{
			
			if((uint)index > (uint)array.Count)
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}

			return new ArraySegment<T>(array.Array, array.Offset + index, array.Count - index);
		}

		public static ArraySegment<T> Slice<T>(this ArraySegment<T> array, int index, int count)
		{
			if((uint)index > (uint)array.Count || (uint)count > (uint)(array.Count - index))
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}

			return new ArraySegment<T>(array.Array, array.Offset + index, count);
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

		public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
		{
			using (var delayCTS = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
			{
				var waiting = Task.Delay(-1, delayCTS.Token);
				var doing = task;
				await Task.WhenAny(waiting, doing);
				delayCTS.Cancel();
				cancellationToken.ThrowIfCancellationRequested();
				return await doing;
			}
		}
		public static async Task WithCancellation(this Task task, CancellationToken cancellationToken)
		{
			using (var delayCTS = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
			{
				var waiting = Task.Delay(-1, delayCTS.Token);
				var doing = task;
				await Task.WhenAny(waiting, doing);
				delayCTS.Cancel();
				cancellationToken.ThrowIfCancellationRequested();
			}
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
