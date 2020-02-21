/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;

using DBriize.Transactions;
using DBriize.Exceptions;

namespace DBriize
{
    /// <summary>
    /// 
    /// </summary>
    public class DBriizeRemoteEngine : DBriizeEngine
    {
        DBriizeConfiguration conf = null;
        bool inited = false;
        object lock_init = new object();
        

        /// <summary>
        /// DBriizeRemoteEngine instantiator
        /// </summary>
        /// <param name="dbreezeConfiguration"></param>
        public DBriizeRemoteEngine(DBriizeConfiguration dbreezeConfiguration)            
        {

            if (dbreezeConfiguration == null)
                throw new Exception("DBriize.DbreezeRemoteEngine.DbreezeRemoteEngine:  dbreezeConfiguration is NULL");

            conf = dbreezeConfiguration;
            this.RemoteEngine = true;
                        
        }

        /// <summary>
        /// 
        /// </summary>
        void Init()
        {
            if (!inited)
            {
                lock (lock_init)
                {
                    if (!inited)
                    {
                        this.ConstructFromConfiguration(conf);
                        inited = true;
                    }
                }
            }
        }


        /// <summary>
        /// Returns transaction object.
        /// </summary>
        /// <returns></returns>
        public new Transaction GetTransaction()
        {
            Init();
            return base.GetTransaction();
        }

        /// <summary>
        /// Returns transaction object.
        /// </summary>
        /// <param name="tablesLockType">
        /// <para>SHARED: threads can use listed tables in parallel. Must be used together with tran.SynchronizeTables command, if necessary.</para>
        /// <para>EXCLUSIVE: if other threads use listed tables for reading or writing, current thread will be in a waiting queue.</para>
        /// </param>
        /// <param name="tables"></param>
        /// <returns>Returns transaction object</returns>
        public new Transaction GetTransaction(eTransactionTablesLockTypes tablesLockType, params string[] tables)
        {
            Init();
            return base.GetTransaction(tablesLockType, tables);
        }

        /// <summary>
        /// Returns DBriize schema object
        /// </summary>
        public new Scheme Scheme
        {
            get
            {
                Init();
                return this.DBriizeSchema;
            }
        }
        
    }


}
