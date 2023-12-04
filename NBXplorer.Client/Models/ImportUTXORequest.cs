using NBitcoin;
using Newtonsoft.Json.Linq;

namespace NBXplorer.Models;

public class ImportUTXORequest
{
	public OutPoint[] Utxos { get; set; }

	public MerkleBlock[] Proofs { get; set; }
}

public class AssociateScriptRequest
{
	public IDestination Destination { get; set; }
	public bool Used { get; set; }
	public JObject Metadata { get; set; }
}