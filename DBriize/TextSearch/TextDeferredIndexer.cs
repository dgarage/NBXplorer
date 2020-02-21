/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using DBriize.LianaTrie;
using DBriize.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;

using DBriize.Utils;

namespace DBriize.TextSearch
{
    /// <summary>
    /// TextDeferredIndexer
    /// </summary>
    internal class TextDeferredIndexer:IDisposable
    {
        DBriizeEngine DBriizeEngine = null;
        TrieSettings LTrieSettings = null;
        IStorage Storage = null;
        LTrie LTrie = null;
        static string TableFileName = "_DBriizeTextIndexer";
        object lock_operation = new object();
        long init = DateTime.UtcNow.Ticks;
        int inDeferredIndexer = 0;
        int disposed = 0;

        public TextDeferredIndexer(DBriizeEngine engine)
        {
            this.DBriizeEngine = engine;
            LTrieSettings = new TrieSettings()
            {
                InternalTable = true
            };
            Storage = new StorageLayer(Path.Combine(engine.MainFolder, TableFileName), LTrieSettings, engine.Configuration);
            LTrie = new LTrie(Storage);
            LTrie.TableName = "DBriize.TextIndexer";

            if (LTrie.Storage.Length > 100000)  //Recreating file if its size more then 100KB and it is empty
            {
                if (LTrie.Count(true) == 0)
                {
                    LTrie.Storage.RecreateFiles();
                    LTrie.Dispose();

                    Storage = new StorageLayer(Path.Combine(engine.MainFolder, TableFileName), LTrieSettings, engine.Configuration);
                    LTrie = new LTrie(Storage);
                    LTrie.TableName = "DBriize.TextIndexer";
                }
            }

            if (LTrie.Count(true) > 0)
                this.StartDefferedIndexing();
        }

        /// <summary>
        /// Disposing
        /// </summary>
        public void Dispose()
        {
            if (System.Threading.Interlocked.CompareExchange(ref disposed, 1, 0) != 0)
                return;

            if (LTrie != null)
            {
                LTrie.Dispose();
            }
        }

        /// <summary>
        /// Add tables and their InternalDocumentIDs for paraller indexing
        /// </summary>
        /// <param name="defferedDocIds"></param>
        public void Add(Dictionary<string, HashSet<uint>> defferedDocIds)
        {
            if (defferedDocIds == null || defferedDocIds.Count == 0)
                return;

            lock (lock_operation)
            {
                init++;
                var bt = DBriize.Utils.Biser.Encode_DICT_PROTO_STRING_UINTHASHSET(defferedDocIds, Compression.eCompressionMethod.NoCompression);                
                LTrie.Add(init.To_8_bytes_array_BigEndian(), bt);
                LTrie.Commit();
            }
        }

        /// <summary>
        /// Runs Indexer. Only one instance is allowed
        /// </summary>
        public void StartDefferedIndexing()
        {
            if (System.Threading.Interlocked.CompareExchange(ref inDeferredIndexer, 1, 0) != 0)
                return;

            //new System.Threading.Thread(new System.Threading.ThreadStart(() =>
            //{
            //    Indexer();
            //})).Start();         

#if NET35 || NETr40   //The same must be use for .NET 4.0

            new System.Threading.Thread(new System.Threading.ThreadStart(() =>
            {
                this.DBriizeEngine.BackgroundNotify("TextDefferedIndexingHasStarted", null);
                Indexer();
                this.DBriizeEngine.BackgroundNotify("TextDefferedIndexingHasFinished", null);
            })).Start(); 
#else
            System.Threading.Tasks.Task.Run(() => {
                this.DBriizeEngine.BackgroundNotify("TextDefferedIndexingHasStarted", null);
                Indexer();
                this.DBriizeEngine.BackgroundNotify("TextDefferedIndexingHasFinished", null);
            });
#endif
        }




        /// <summary>
        /// Indexer self
        /// </summary>
        void Indexer()
        {

            int maximalIterations = 10; //Iterations then a breath
            int currentItter = 0;

            Dictionary<byte[], Dictionary<string, HashSet<uint>>> defTasks = new Dictionary<byte[], Dictionary<string, HashSet<uint>>>();
            Dictionary<string, HashSet<uint>> defTask = null;
            Dictionary<string, TextSearchHandler.ITS> itbls = new Dictionary<string, TextSearchHandler.ITS>();

            while (true)
            {
                currentItter = 0;
                defTasks.Clear();
                itbls.Clear();

                lock (lock_operation)
                {                    
                    foreach (var row in LTrie.IterateForward(true, false).Take(maximalIterations))
                    {
                        
                        currentItter++;
                        defTask = new Dictionary<string, HashSet<uint>>();
                        DBriize.Utils.Biser.Decode_DICT_PROTO_STRING_UINTHASHSET(row.GetFullValue(true), defTask, Compression.eCompressionMethod.NoCompression);                        
                        defTasks.Add(row.Key, defTask);

                        foreach (var el in defTask)
                        {
                            if (!itbls.ContainsKey(el.Key))
                                itbls[el.Key] = new TextSearchHandler.ITS();

                            foreach (var el1 in el.Value)
                                itbls[el.Key].ChangedDocIds.Add((int)el1);

                        }
                    }

                    if (currentItter == 0)
                    {
                        inDeferredIndexer = 0;  //going out
                        return;
                    }
                }

                //Indexing defTasks
                using (var tran = this.DBriizeEngine.GetTransaction())
                {
                    tran.tsh = new TextSearchHandler(tran);
                    tran.SynchronizeTables(itbls.Keys.ToList());
                    tran.tsh.DoIndexing(tran, itbls);
                    tran.Commit();

                    itbls.Clear();
                }

                //Removing indexed docs from LTrie
                lock (lock_operation)
                {
                    byte[] key = null;

                    foreach (var el in defTasks.OrderBy(r=>r.Key.ToBytesString()))
                    {
                        key = el.Key;
                        LTrie.Remove(ref key);
                    }

                    LTrie.Commit();
                }


            }//eo while



        }//eof

    }//eoc
}
