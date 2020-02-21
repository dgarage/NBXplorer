/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBriize.Utils;
using DBriize.DataTypes;

namespace DBriize.Objects
{   
    /// <summary>
    /// Concept of the objects storage (read docu from 20170321)
    /// </summary>
    public class DBriizeIndex
    {
        internal List<byte[]> bts = new List<byte[]>();
        /// <summary>
        /// Must be unique for every index, will be used for searches
        /// </summary>
        internal byte IndexNumber = 1;
        /// <summary>
        /// Only for not primary index
        /// </summary>
        public bool AddPrimaryToTheEnd { get; set; }
        /// <summary>
        /// Set a flag that this index is a primary key
        /// </summary>
        public bool PrimaryIndex { get; set; }

        internal byte[] IndexFull = null;
        internal byte[] IndexNoPrefix = null;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="indexNumber">Must be unique for every index, will be used for searches</param>
        /// <param name="props">chain of data types forming byte[] index. if null is supplied existing index entry will be removed</param>
        public DBriizeIndex(byte indexNumber, params object[] props)
        {  
            AddPrimaryToTheEnd = true;
            PrimaryIndex = false;
            
            if (IndexNumber == 0)
                throw new Exception("DBriizeIndex: zero as index number is not allowed!");

            //if(props.Count() == 0)
            //    throw new Exception($"DBriize.Transaction.InsertObject: index { indexNumber } is incorrectly defined");

            IndexNumber = indexNumber;

            if (props != null)
            {
                foreach (var prop in props)
                {
                    if (prop == null)
                    {
                        AddPrimaryToTheEnd = false;
                        bts.Clear();
                        break;
                    }

                    //throw new Exception($"DBriize.Transaction.InsertObject: index { indexNumber } is incorrectly defined");

                    bts.Add(DataTypesConvertor.ConvertValue(prop, prop.GetType()));
                }
            }
            else
            {
                AddPrimaryToTheEnd = false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="primaryIdx"></param>
        /// <returns></returns>
        internal void FormIndex(byte[] primaryIdx)
        {
            if (bts.Count == 0) //Support of nullable index (must be removed if exists)
                return;
            
            if (!PrimaryIndex && AddPrimaryToTheEnd && primaryIdx != null)
                bts.Add(primaryIdx);

            IndexNoPrefix = bts.Concat();
            IndexFull = new byte[] { IndexNumber }.ConcatMany(bts);
            
        }
    }

}
