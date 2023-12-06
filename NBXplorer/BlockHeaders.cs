#nullable enable
using NBitcoin;
using System;
using System.Collections;
using System.Collections.Generic;

namespace NBXplorer;

public record RPCBlockHeader(uint256 Hash, uint256? Previous, int Height, DateTimeOffset Time)
{
	public SlimChainedBlock ToSlimChainedBlock() => new(Hash, Previous, Height);
}
public class BlockHeaders : IEnumerable<RPCBlockHeader>
{
	public readonly Dictionary<uint256, RPCBlockHeader> ByHashes;
	public readonly Dictionary<int, RPCBlockHeader> ByHeight;
	public BlockHeaders(IList<RPCBlockHeader> headers)
	{
		ByHashes = new Dictionary<uint256, RPCBlockHeader>(headers.Count);
		ByHeight = new Dictionary<int, RPCBlockHeader>(headers.Count);
		foreach (var header in headers)
		{
			ByHashes.TryAdd(header.Hash, header);
			ByHeight.TryAdd(header.Height, header);
		}
	}
	public IEnumerator<RPCBlockHeader> GetEnumerator()
	{
		return ByHeight.Values.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}
