using NBitcoin;

namespace NBXplorer.Models;

public class ImportUTXORequest
{
	public Coin Coin { get; set; }
	public MerkleBlock Proof { get; set; }
}