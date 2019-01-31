using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using NBXplorer.Models;
using System.Collections.Concurrent;

namespace NBXplorer
{
	public class LongPollingNotificationSession : NotificationSessionBase
	{
		public LongPollingNotificationSession(long lastEventId, ExplorerClient client)
		{
			LastEventId = lastEventId;
			Client = client;
		}

		public long LastEventId { get; private set; }
		public ExplorerClient Client { get; }
		Queue<NewEventBase> _EventsToProcess = new Queue<NewEventBase>();

		public override async Task<NewEventBase> NextEventAsync(CancellationToken cancellation = default)
		{
		retry:
			long evtId = 0;
			lock (_EventsToProcess)
			{
				evtId = LastEventId;
				if (_EventsToProcess.Count > 0)
				{
					var evt = _EventsToProcess.Dequeue();
					LastEventId = evt.EventId;
					return evt;
				}
			}
			NewEventBase[] evts = null;
			try
			{
				evts = await GetEventsAsync(evtId, 30, true, cancellation);
			}
			catch(HttpRequestException ex) when (ex.InnerException is TimeoutException)
			{
				goto retry;
			}
			catch(OperationCanceledException) when (!cancellation.IsCancellationRequested)
			{
				goto retry;
			}
			lock (_EventsToProcess)
			{
				if (_EventsToProcess.Count != 0)
					goto retry;
				foreach (var item in evts)
				{
					_EventsToProcess.Enqueue(item);
				}
			}
			goto retry;
		}

		public NewEventBase[] GetEvents(long lastEventId = 0, int? limit = null, bool longPolling = false, CancellationToken cancellation = default)
		{
			return GetEventsAsync(lastEventId, limit, longPolling, cancellation).GetAwaiter().GetResult();
		}

		public async Task<NewEventBase[]> GetEventsAsync(long lastEventId = 0, int? limit = null, bool longPolling = false, CancellationToken cancellation = default)
		{
			List<string> parameters = new List<string>();
			if (lastEventId != 0)
				parameters.Add($"lastEventId={lastEventId}");
			if (limit != null)
				parameters.Add($"limit={limit.Value}");
			if (longPolling)
				parameters.Add($"longPolling={longPolling}");
			var parametersString = parameters.Count == 0 ? string.Empty : $"?{String.Join("&", parameters.ToArray<object>())}";
			var evts = await Client.SendAsync<JArray>(HttpMethod.Get, null, $"v1/cryptos/{Client.CryptoCode}/events{parametersString}", null, cancellation);

			var evtsObj = evts.Select(ev => NewEventBase.ParseEvent((JObject)ev, Client.Serializer.Settings))
					  .OfType<NewEventBase>()
					  .ToArray();
			return evtsObj;
		}
	}
}
