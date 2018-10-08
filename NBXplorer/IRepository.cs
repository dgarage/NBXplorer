using System.Linq;
using NBitcoin;
using NBitcoin.JsonConverters;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using static NBXplorer.Repository;

namespace NBXplorer
{
    public interface IRepository
    {
        Serializer Serializer
        {
            get;
        }

        int MinPoolSize
        {
            get; set;
        }
        int MaxPoolSize
        {
            get; set;
        }
        string _Suffix { get; set; }

        int BatchSize
        {
            get; set;
        }
        Task Ping();

        Task CancelReservation(DerivationStrategyBase strategy, KeyPath[] keyPaths);

        NBXplorerNetwork Network
        {
            get;
        }

        Task<BlockLocator> GetIndexProgress();

        Task SetIndexProgress(BlockLocator locator);
        Task<KeyPathInformation> GetUnused(DerivationStrategyBase strategy, DerivationFeature derivationFeature, int n, bool reserve);

        Task Track(BitcoinAddress address);

        Task<int> RefillAddressPoolIfNeeded(DerivationStrategyBase strategy, DerivationFeature derivationFeature, int maxAddreses = int.MaxValue);

        Task<List<SavedTransaction>> SaveTransactions(DateTimeOffset now, NBitcoin.Transaction[] transactions, uint256 blockHash);

        Task<SavedTransaction[]> GetSavedTransactions(uint256 txid);

        Task<MultiValueDictionary<Script, KeyPathInformation>> GetKeyInformations(Script[] scripts);

        Task<TrackedTransaction[]> GetTransactions(TrackedSource trackedSource);

        Task SaveMatches(DateTimeOffset now, MatchedTransaction[] transactions);

        Task CleanTransactions(TrackedSource trackedSource, List<TrackedTransaction> cleanList);

    }


}
