using NBitcoin;
using NBitcoin.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class BlockDownloader : IDisposable
	{
		private SlimChain chain;
		private Node node;

		public BlockDownloader(SlimChain chain, Node node)
		{
			this.chain = chain;
			this.node = node;
			node.StateChanged += Node_StateChanged;
			node.MessageReceived += Node_MessageReceived;
		}
		Channel<Block> blocks = Channel.CreateUnbounded<Block>();
		private void Node_MessageReceived(Node node, IncomingMessage message)
		{
			if (message.Message.Payload is BlockPayload b)
				blocks.Writer.TryWrite(b.Object);
		}

		private void Node_StateChanged(Node node, NodeState oldState)
		{
			if (node.State != NodeState.HandShaked)
				blocks.Writer.TryComplete();
		}

		public void Dispose()
		{
			node.StateChanged -= Node_StateChanged;
			node.MessageReceived -= Node_MessageReceived;
			blocks.Writer.TryComplete();
		}

		const int maxinflight = 5;
		internal async IAsyncEnumerable<Block> DownloadBlocks(BlockLocator fork, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			foreach (var hashes in EnumerateToTip(fork, chain).Select(c => c.Hash).Batch(maxinflight))
			{
				Dictionary<uint256, Block> outoforder = new Dictionary<uint256, Block>();
				var hashesEnum = hashes.GetEnumerator();
				if (!hashesEnum.MoveNext())
					yield break;
				await node.SendMessageAsync(new GetDataPayload(hashes.Select(h => new InventoryVector(node.AddSupportedOptions(InventoryType.MSG_BLOCK), h)).ToArray()));

				
				await foreach (var block in blocks.Reader.ReadAllAsync(cancellationToken))
				{
					var blockHash = block.Header.GetHash();
					if (blockHash == hashesEnum.Current)
					{
						yield return block;
						if (!hashesEnum.MoveNext())
							break;
						while (outoforder.TryGetValue(hashesEnum.Current, out var block2))
						{
							yield return block2;
							outoforder.Remove(hashesEnum.Current);
							if (!hashesEnum.MoveNext())
								break;
						}
					}
					else
					{
						outoforder.TryAdd(blockHash, block);
					}
				}
			}

			
		}

		private IEnumerable<SlimChainedBlock> EnumerateToTip(BlockLocator fork, SlimChain chain)
		{
			var bh = chain.FindFork(fork);
			if (bh is null)
				throw new InvalidOperationException("No fork found with the chain");
			int height = bh.Height + 1;
			var prev = bh.Hash;
			while (true)
			{
				bh = chain.GetBlock(height);
				if (bh is null)
					yield break;
				if (bh.Previous != prev)
					yield break;

				yield return bh;

				height = bh.Height + 1;
				prev = bh.Hash;
			}
		}
	}
}
