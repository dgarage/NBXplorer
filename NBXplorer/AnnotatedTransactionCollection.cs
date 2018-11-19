using NBitcoin;
using Microsoft.Extensions.Logging;
using NBXplorer.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBXplorer.Models;

namespace NBXplorer
{
    public enum AnnotatedTransactionType
    {
        Confirmed,
        Unconfirmed,
        Orphan
    }
    public class AnnotatedTransaction
    {
        public AnnotatedTransaction(TrackedTransaction tracked, SlimChain chain)
        {
            if (tracked == null)
                throw new ArgumentNullException(nameof(tracked));
            Record = tracked;
            if (tracked.BlockHash == null)
            {
                Type = AnnotatedTransactionType.Unconfirmed;
            }
            else
            {
                var block = chain?.GetBlock(tracked.BlockHash);
                Type = block == null ? AnnotatedTransactionType.Orphan : AnnotatedTransactionType.Confirmed;
                Height = block?.Height;
            }
        }
        public AnnotatedTransactionType Type
        {
            get; set;
        }
        public int? Height
        {
            get;
            internal set;
        }
        public TrackedTransaction Record
        {
            get;
        }

        public override string ToString()
        {
            return Record.TransactionHash.ToString();
        }
    }

    public class AnnotatedTransactionCollection : List<AnnotatedTransaction>
    {
        public AnnotatedTransactionCollection(IEnumerable<AnnotatedTransaction> transactions, Models.TrackedSource trackedSource) : base(transactions)
        {
            foreach (var tx in transactions)
            {
                foreach (var keyPathInfo in tx.Record.KnownKeyPathMapping)
                {
                    _KeyPaths.TryAdd(keyPathInfo.Key, keyPathInfo.Value);
                }
            }

            UTXOState state = new UTXOState();
            foreach (var confirmed in transactions
                                        .Where(tx => tx.Type == AnnotatedTransactionType.Confirmed).ToList()
                                        .TopologicalSort())
            {
                if (state.Apply(confirmed.Record) == ApplyTransactionResult.Conflict)
                {
                    Logs.Explorer.LogError("A conflict among confirmed transaction happened, this should be impossible");
                    throw new InvalidOperationException("The impossible happened");
                }
                ConfirmedTransactions.Add(confirmed);
                _TxById.Add(confirmed.Record.TransactionHash, confirmed);
            }

            foreach (var unconfirmed in transactions
                                        .Where(tx => tx.Type == AnnotatedTransactionType.Unconfirmed || tx.Type == AnnotatedTransactionType.Orphan)
                                        .OrderByDescending(t => t.Record.Inserted) // OrderByDescending so that the last received is least likely to be conflicted
                                        .ToList()
                                        .TopologicalSort())
            {
                if (_TxById.ContainsKey(unconfirmed.Record.TransactionHash))
                {
                    _TxById.Add(unconfirmed.Record.TransactionHash, unconfirmed);
                    DuplicatedTransactions.Add(unconfirmed);
                }
                else
                {
                    _TxById.Add(unconfirmed.Record.TransactionHash, unconfirmed);
                    if (state.Apply(unconfirmed.Record) == ApplyTransactionResult.Conflict)
                    {
                        ReplacedTransactions.Add(unconfirmed);
                    }
                    else
                    {
                        UnconfirmedTransactions.Add(unconfirmed);
                    }
                }
            }

            TrackedSource = trackedSource;
        }

        public MatchedOutput GetUTXO(OutPoint outpoint)
        {
            if (_TxById.TryGetValue(outpoint.Hash, out var txs))
            {
                return txs.SelectMany(t => t.Record.GetReceivedOutputs().Where(c => c.Index == outpoint.N))
                          .FirstOrDefault();
            }
            return null;
        }

        Dictionary<Script, KeyPath> _KeyPaths = new Dictionary<Script, KeyPath>();
        public KeyPath GetKeyPath(Script scriptPubkey)
        {
            return _KeyPaths.TryGet(scriptPubkey);
        }

        MultiValueDictionary<uint256, AnnotatedTransaction> _TxById = new MultiValueDictionary<uint256, AnnotatedTransaction>();
        public IReadOnlyCollection<AnnotatedTransaction> GetByTxId(uint256 txId)
        {
            if (txId == null)
                throw new ArgumentNullException(nameof(txId));
            IReadOnlyCollection<AnnotatedTransaction> value;
            if (_TxById.TryGetValue(txId, out value))
                return value;
            return new List<AnnotatedTransaction>();
        }

        public List<AnnotatedTransaction> ReplacedTransactions
        {
            get; set;
        } = new List<AnnotatedTransaction>();

        public List<AnnotatedTransaction> ConfirmedTransactions
        {
            get; set;
        } = new List<AnnotatedTransaction>();

        public List<AnnotatedTransaction> UnconfirmedTransactions
        {
            get; set;
        } = new List<AnnotatedTransaction>();

        public List<AnnotatedTransaction> DuplicatedTransactions
        {
            get; set;
        } = new List<AnnotatedTransaction>();
        public Models.TrackedSource TrackedSource { get; }
    }

}
