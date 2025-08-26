using NBitcoin;
using Newtonsoft.Json;

namespace NBXplorer.Models;

public class ImportUTXORequest
{
	[JsonProperty("UTXOs")]
	public OutPoint[] Utxos { get; set; }
}