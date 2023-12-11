using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace NBXplorer.Models;

public class ImportUTXORequest
{
	[JsonProperty("UTXOs")]
	public OutPoint[] Utxos { get; set; }
}

public class AssociateScriptRequest
{
	public Script ScriptPubKey { get; set; }
	public IDestination Destination { get; set; }

	public BitcoinAddress GetAddress(Network network)
	{
		return GetScriptPubKey().GetDestinationAddress(network);
	}

	public Script GetScriptPubKey() => ScriptPubKey ?? Destination.ScriptPubKey;
}