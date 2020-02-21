/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBriize.Exceptions
{
    /// <summary>
    /// Unified class for Debreeze exceptions
    /// </summary>
    public class DBriizeException : Exception
    {

        public DBriizeException()
        {
        }

        public DBriizeException(string message)
            : base(message)
        {
            
        }

        public DBriizeException(string message,Exception innerException)
            : base(message,innerException)
        {

        }

        /*   USAGE  
         try
            {
                int i = 0;
                int b = 12;
                int c = b / i;               
            }
            catch(System.Exception ex)
            {
                throw DBriizeException.Throw(DBriizeException.eDBriizeExceptions.GET_TABLE_WRITE_FAILED, "myTable", ex);  
         *      //or just 
         *      throw DBriizeException.Throw(DBriizeException.eDBriizeExceptions.DB_IS_NOT_OPERATABLE,ex);  
         *      //or just     
         *      throw new DBriizeException("my extra info", ex);
            }
         
         * Result Exception will be
         * 
         * DBriize.Exceptions.DBriizeException: Getting table "myTable" from the schema failed! --> DIVIDE BY ZERO....
               bei DBriize.Transactions.Transaction.Insert(String tableName, Byte[] key, Byte[] value) in C:\Users\blaze\Documents\Visual Studio 2010\Projects\DBriize\DBriize\Transactions\Transaction.cs:Zeile 67.
               bei VisualTester.FastTests.TA1_Thread1() in C:\Users\blaze\Documents\Visual Studio 2010\Projects\DBriize\VisualTester\FastTests.cs:Zeile 646.
         * 
         */


        public enum eDBriizeExceptions
        {
            UNKNOWN, //Fake one

            //General, internal exception
            GENERAL_EXCEPTION_DB_NOT_OPERABLE,
            GENERAL_EXCEPTION_DB_OPERABLE,

            //Enging
            DB_IS_NOT_OPERABLE,
            CREATE_DB_FOLDER_FAILED,

            //Schema
            SCHEME_GET_TABLE_WRITE_FAILED,
            SCHEME_FILE_PROTOCOL_IS_UNKNOWN,
            SCHEME_TABLE_DELETE_FAILED,
            SCHEME_TABLE_RENAME_FAILED,

            //SchemaInternal.UserTable name patterns
            TABLE_NAMES_TABLENAMECANTBEEMPTY,
            TABLE_NAMES_TABLENAMECANT_CONTAINRESERVEDSYMBOLS,
            TABLE_PATTERN_CANTBEEMPTY,
            TABLE_PATTERN_SYMBOLS_AFTER_SHARP,

            //LTrie
            TABLE_IS_NOT_OPEARABLE,
            COMMIT_FAILED,
            TRANSACTIONAL_COMMIT_FAILED,
            ROLLBACK_FAILED,
            TRANSACTIONAL_ROLLBACK_FAILED,
            ROLLBACK_NOT_OPERABLE,
            PREPARE_ROLLBACK_FILE_FAILED,
            KEY_IS_TOO_LONG,
            RECREATE_TABLE_FAILED,
            RESTORE_ROLLBACK_DATA_FAILED,

            //Transaction Journal
            CLEAN_ROLLBACK_FILES_FOR_FINISHED_TRANSACTIONS_FAILED,

            //Transactions Coordinator
            TRANSACTION_DOESNT_EXIST,
            TRANSACTION_CANBEUSED_FROM_ONE_THREAD,
            TRANSACTION_IN_DEADLOCK,
            TRANSACTION_TABLE_WRITE_REGISTRATION_FAILED,
            TRANSACTION_GETTING_TRANSACTION_FAILED,


            //Transaction
            TRANSACTION_TABLES_RESERVATION_FAILED,
            TRANSACTION_TABLES_RESERVATION_CANBEDONE_ONCE,
            TRANSACTION_TABLES_RESERVATION_LIST_MUSTBEFILLED,

            //DataTypes
            UNSUPPORTED_DATATYPE,
            UNSUPPORTED_DATATYPE_VALUE,
            KEY_CANT_BE_NULL,
            PARTIAL_VALUE_CANT_BE_NULL,

            //XML serializer
            XML_SERIALIZATION_ERROR,
            XML_DESERIALIZATION_ERROR,

            //MICROSOFT JSON serializer
            MJSON_SERIALIZATION_ERROR,
            MJSON_DESERIALIZATION_ERROR,

            //Custom serializer
            CUSTOM_SERIALIZATION_ERROR,
            CUSTOM_DESERIALIZATION_ERROR,

            //DBINTABLE
            DBINTABLE_CHANGEDATA_FROMSELECTVIEW,

            DYNAMIC_DATA_BLOCK_VALUE_IS_BIG,

            BACKUP_FOLDER_CREATE_FAILED,

            TABLE_WAS_CHANGED_LINKS_ARE_NOT_ACTUAL,
            /// <summary>
            /// The rest must be supplied via extra params
            /// </summary>
            DBREEZE_RESOURCES_CONCERNING
        }

        public static Exception Throw(Exception innerException)
        {
            return GenerateException(eDBriizeExceptions.GENERAL_EXCEPTION_DB_OPERABLE, String.Empty, innerException);
        }

        public static Exception Throw(eDBriizeExceptions exceptionType,Exception innerException)
        {
            return GenerateException(exceptionType, String.Empty, innerException);
        }

        public static Exception Throw(eDBriizeExceptions exceptionType)
        {
            return GenerateException(exceptionType, String.Empty, null);
        }

        public static Exception Throw(eDBriizeExceptions exceptionType, string message, Exception innerException)
        {
            return GenerateException(exceptionType, message, innerException);
        }

        /*  USAGE EXAMPLES
         
         throw new TableNotOperableException(this.TableName);
         throw DBriizeException.Throw(DBriizeException.eDBriizeExceptions.DB_IS_NOT_OPERABLE);
         throw DBriizeException.Throw(DBriizeException.eDBriizeExceptions.ROLLBACK_FAILED, _rollbackFileName,ex);
         throw DBriizeException.Throw(DBriizeException.eDBriizeExceptions.KEY_IS_TOO_LONG);  
         */

        /// <summary>
        /// Internal
        /// </summary>
        /// <param name="exceptionType"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        private static Exception GenerateException(eDBriizeExceptions exceptionType, string message, Exception innerException)
        {
            switch (exceptionType)
            {
                //General
                case eDBriizeExceptions.GENERAL_EXCEPTION_DB_NOT_OPERABLE:
                    return new DBriizeException(String.Format("Database is not operable, please find out the problem and restart the engine! {0}",message), innerException);
                    
                //Enging
                case eDBriizeExceptions.DB_IS_NOT_OPERABLE:
                    return new DBriizeException(String.Format("Database is not operable, please find out the problem and restart the engine! {0}", message), innerException);
                case eDBriizeExceptions.CREATE_DB_FOLDER_FAILED:
                    return new DBriizeException("Creation of the database folder failed!", innerException);
                // return new DBriizeException(String.Format("{0}creation of the database folder failed: {1}", ExceptionHeader, originalException.ToString()));

                //Schema
                case eDBriizeExceptions.SCHEME_GET_TABLE_WRITE_FAILED:
                    return new DBriizeException(String.Format("Getting table \"{0}\" from the schema failed!", message), innerException);
                case eDBriizeExceptions.SCHEME_FILE_PROTOCOL_IS_UNKNOWN:
                    return new DBriizeException(String.Format("Scheme file protocol is unknown from the schema failed!"), innerException);
                case eDBriizeExceptions.SCHEME_TABLE_DELETE_FAILED:
                    return new DBriizeException(String.Format("User table \"{0}\" delete failed!",message), innerException);
                case eDBriizeExceptions.SCHEME_TABLE_RENAME_FAILED:
                    return new DBriizeException(String.Format("User table \"{0}\" rename failed!", message), innerException);


                //SchemaInternal.UserTable name patterns
                case eDBriizeExceptions.TABLE_NAMES_TABLENAMECANTBEEMPTY:
                    return new DBriizeException(String.Format("Table name can't be empty!"), innerException);
                case eDBriizeExceptions.TABLE_NAMES_TABLENAMECANT_CONTAINRESERVEDSYMBOLS:
                    return new DBriizeException(String.Format("Table name can not contain reserved symbols like * # @ \\ ^ $ ~ ´"), innerException);
                case eDBriizeExceptions.TABLE_PATTERN_CANTBEEMPTY:
                    return new DBriizeException(String.Format("Table pattern can't be empty!"), innerException);
                case eDBriizeExceptions.TABLE_PATTERN_SYMBOLS_AFTER_SHARP:
                    return new DBriizeException(String.Format("After # must follow / and any other symbol!"), innerException);
  
                    
                //LTrie
                //case eDBriizeExceptions.TABLE_IS_NOT_OPEARABLE:
                //    return new DBriizeException(String.Format("Table \"{0}\" is not operable!", message), innerException);
                case eDBriizeExceptions.COMMIT_FAILED:
                    return new DBriizeException(String.Format("Table \"{0}\" commit failed!", message), innerException);     //ADD TABLE NAME!!!
                case eDBriizeExceptions.TRANSACTIONAL_COMMIT_FAILED:
                    return new DBriizeException(String.Format("Transaction commit failed on table \"{0}\"!",message), innerException);
                case eDBriizeExceptions.RESTORE_ROLLBACK_DATA_FAILED:
                    return new DBriizeException(String.Format("Restore rollback file \"{0}\" failed!", message), innerException);
                case eDBriizeExceptions.ROLLBACK_NOT_OPERABLE:                                            //WTF ?????????????????
                    //return new DBriizeException(String.Format("{0}rollback of the file \"{1}\" is not operatable: {2}", ExceptionHeader, description, originalException.ToString()));
                    return new DBriizeException(String.Format("Rollback of the file \"{0}\" is not operable!", message), innerException);
                case eDBriizeExceptions.ROLLBACK_FAILED:                                                 
                    return new DBriizeException(String.Format("Rollback of the table \"{0}\" failed!", message), innerException);
                case eDBriizeExceptions.TRANSACTIONAL_ROLLBACK_FAILED:                                                 
                    return new DBriizeException(String.Format("Transaction rollback failed on the table \"{0}\"!", message), innerException);
                case eDBriizeExceptions.RECREATE_TABLE_FAILED:
                    return new DBriizeException(String.Format("Table \"{0}\" re-creation failed!", message), innerException);
                case eDBriizeExceptions.PREPARE_ROLLBACK_FILE_FAILED:
                    return new DBriizeException(String.Format("Rollback file \"{0}\" preparation failed!", message), innerException);
                case eDBriizeExceptions.KEY_IS_TOO_LONG:             
                    return new DBriizeException(String.Format("Key is too long, maximal key size is: {0}!", UInt16.MaxValue.ToString()), innerException);
                case eDBriizeExceptions.TABLE_WAS_CHANGED_LINKS_ARE_NOT_ACTUAL:
                    {
                        //It can happen when we have read LTrieRow with link to value, then table was re-created or restored from other table,
                        //and then we want to get value from an "old" link
                        return new DBriizeException(String.Format("Table was changed (Table Recrete, Table RestoreTableFromTheOtherTable), links are not actual, repeat reading operation!"), innerException);
                    }

                //Transaction Journal
                case eDBriizeExceptions.CLEAN_ROLLBACK_FILES_FOR_FINISHED_TRANSACTIONS_FAILED:
                    return new DBriizeException(String.Format("Transaction journal couldn't clean rollback files of the finished transactions!"), innerException);


                //Transactions Coordinator
                case eDBriizeExceptions.TRANSACTION_DOESNT_EXIST:
                    return new DBriizeException(String.Format("Transaction doesn't exist anymore!"), innerException);
                case eDBriizeExceptions.TRANSACTION_CANBEUSED_FROM_ONE_THREAD:
                    return new DBriizeException(String.Format("One transaction can be used from one thread only!"), innerException);
                case eDBriizeExceptions.TRANSACTION_IN_DEADLOCK:
                    return new DBriizeException(String.Format("Transaction is in a deadlock state and will be terminated. To avoid such case use Transaction.SynchronizeTables!"), innerException);
                case eDBriizeExceptions.TRANSACTION_TABLE_WRITE_REGISTRATION_FAILED:
                    return new DBriizeException(String.Format("Transaction registration table for Write failed!"), innerException);
                case eDBriizeExceptions.TRANSACTION_GETTING_TRANSACTION_FAILED:
                    return new DBriizeException(String.Format("getting transaction failed!"), innerException);


                //Transaction
                case eDBriizeExceptions.TRANSACTION_TABLES_RESERVATION_FAILED:
                    return new DBriizeException(String.Format("Reservation tables for modification or synchronized read failed! Use SynchronizeTables before any modification!"), innerException);
                case eDBriizeExceptions.TRANSACTION_TABLES_RESERVATION_CANBEDONE_ONCE:
                    return new DBriizeException(String.Format("Reservation tables for modification or synchronized read failed! Only one synchronization call permitted per transaction!"), innerException);
                case eDBriizeExceptions.TRANSACTION_TABLES_RESERVATION_LIST_MUSTBEFILLED:
                    return new DBriizeException(String.Format("Reservation tables for modification or synchronized read failed! Synchronization list must be filled!"), innerException);
                    

                //DataTypes
                case eDBriizeExceptions.UNSUPPORTED_DATATYPE:
                    return new DBriizeException(String.Format("Unsupported data type \"{0}\"!", message), innerException);
                case eDBriizeExceptions.UNSUPPORTED_DATATYPE_VALUE:
                    return new DBriizeException(String.Format("Unsupported data type value \"{0}\"!", message), innerException);                    
                case eDBriizeExceptions.KEY_CANT_BE_NULL:
                    return new DBriizeException(String.Format("Key can't be NULL!"), innerException);
                case eDBriizeExceptions.PARTIAL_VALUE_CANT_BE_NULL:
                    return new DBriizeException(String.Format("Partial value can't be NULL!"), innerException);


                //XML serializer
                case eDBriizeExceptions.XML_SERIALIZATION_ERROR:
                    return new DBriizeException(String.Format("XML serialization error!"), innerException);
                case eDBriizeExceptions.XML_DESERIALIZATION_ERROR:
                    return new DBriizeException(String.Format("XML deserialization error!"), innerException);


                //MICROSOFT JSON serializer
                case eDBriizeExceptions.MJSON_SERIALIZATION_ERROR:
                    return new DBriizeException(String.Format("Microsoft JSON serialization error!"), innerException);
                case eDBriizeExceptions.MJSON_DESERIALIZATION_ERROR:
                    return new DBriizeException(String.Format("Microsoft JSON deserialization error!"), innerException);

                //Custom serializer
                case eDBriizeExceptions.CUSTOM_SERIALIZATION_ERROR:
                    return new DBriizeException(String.Format("Custom serialization error!"), innerException);
                case eDBriizeExceptions.CUSTOM_DESERIALIZATION_ERROR:
                    return new DBriizeException(String.Format("Custom deserialization error!"), innerException);

                //DBINTABLE
                case eDBriizeExceptions.DBINTABLE_CHANGEDATA_FROMSELECTVIEW:
                    return new DBriizeException(String.Format("Changing data after SelectTable is not permitted, use InsertTable instead!"), innerException);

                //Dynamic data blocks
                case eDBriizeExceptions.DYNAMIC_DATA_BLOCK_VALUE_IS_BIG:
                    return new DBriizeException(String.Format("Value is too big, more then Int32.MaxValue!"), innerException);

                //Backup
                case eDBriizeExceptions.BACKUP_FOLDER_CREATE_FAILED:
                    return new DBriizeException(String.Format("Backup folder creation has failed"), innerException);

                case eDBriizeExceptions.DBREEZE_RESOURCES_CONCERNING:
                    return new DBriizeException(String.Format("DBriize.DbreezeResources err: \"{0}\"!", message), innerException);
            }

            //Fake
            return new DBriizeException("Unknown mistake occured");
        }

    }
}
