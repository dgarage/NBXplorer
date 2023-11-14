// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// From https://github.com/dotnet/corefxlab/blob/master/src/System.Collections.Generic.MultiValueDictionary/System/Collections/Generic/MultiValueDictionary.cs
using System;
using System.Collections;
using System.Collections.Generic;

namespace NBXplorer
{
	/// <summary>
	/// A MultiValueDictionary can be viewed as a <see cref="IDictionary" /> that allows multiple 
	/// values for any given unique key. While the MultiValueDictionary API is 
	/// mostly the same as that of a regular <see cref="IDictionary" />, there is a distinction
	/// in that getting the value for a key returns a <see cref="IReadOnlyCollection{TValue}" /> of values
	/// rather than a single value associated with that key. Additionally, 
	/// there is functionality to allow adding or removing more than a single
	/// value at once. 
	/// 
	/// The MultiValueDictionary can also be viewed as a IReadOnlyDictionary&lt;TKey,IReadOnlyCollection&lt;TValue&gt;t&gt;
	/// where the <see cref="IReadOnlyCollection{TValue}" /> is abstracted from the view of the programmer.
	/// 
	/// For a read-only MultiValueDictionary.
	/// </summary>
	/// <typeparam name="TKey">The type of the key.</typeparam>
	/// <typeparam name="TValue">The type of the value.</typeparam>
	public class MultiValueDictionary<TKey, TValue> :
		IReadOnlyDictionary<TKey, IReadOnlyCollection<TValue>>
	{
		#region Variables
		/*======================================================================
        ** Variables
        ======================================================================*/

		/// <summary>
		/// The private dictionary that this class effectively wraps around
		/// </summary>
		private Dictionary<TKey, InnerCollectionView> dictionary;

		/// <summary>
		/// The function to construct a new <see cref="ICollection{TValue}"/>
		/// </summary>
		/// <returns></returns>
		private Func<ICollection<TValue>> NewCollectionFactory = () => new List<TValue>();

		/// <summary>
		/// The current version of this MultiValueDictionary used to determine MultiValueDictionary modification
		/// during enumeration
		/// </summary>
		private int version;

		#endregion

		#region Constructors
		/*======================================================================
        ** Constructors
        ======================================================================*/

		/// <summary>
		/// Initializes a new instance of the <see cref="MultiValueDictionary{TKey, TValue}" /> 
		/// class that is empty, has the default initial capacity, and uses the default
		/// <see cref="IEqualityComparer{TKey}" /> for <typeparamref name="TKey"/>.
		/// </summary>
		public MultiValueDictionary()
		{
			dictionary = new Dictionary<TKey, InnerCollectionView>();
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="MultiValueDictionary{TKey, TValue}" /> class that is 
		/// empty, has the specified initial capacity, and uses the default <see cref="IEqualityComparer{TKey}"/>
		/// for <typeparamref name="TKey"/>.
		/// </summary>
		/// <param name="capacity">Initial number of keys that the <see cref="MultiValueDictionary{TKey, TValue}" /> will allocate space for</param>
		/// <exception cref="ArgumentOutOfRangeException">capacity must be >= 0</exception>
		public MultiValueDictionary(int capacity)
		{
			if(capacity < 0)
				throw new ArgumentOutOfRangeException("capacity", Properties.Resources.ArgumentOutOfRange_NeedNonNegNum);
			dictionary = new Dictionary<TKey, InnerCollectionView>(capacity);
		}

		#endregion

		#region Static Factories
		/*======================================================================
        ** Static Factories
        ======================================================================*/

		#endregion

		#region Static Factories with Func parameters
		/*======================================================================
        ** Static Factories with Func parameters
        ======================================================================*/

		#endregion

		#region Concrete Methods
		/*======================================================================
        ** Concrete Methods
        ======================================================================*/

		/// <summary>
		/// Adds the specified <typeparamref name="TKey"/> and <typeparamref name="TValue"/> to the <see cref="MultiValueDictionary{TKey,TValue}"/>.
		/// </summary>
		/// <param name="key">The <typeparamref name="TKey"/> of the element to add.</param>
		/// <param name="value">The <typeparamref name="TValue"/> of the element to add.</param>
		/// <exception cref="ArgumentNullException"><paramref name="key"/> is <c>null</c>.</exception>
		/// <remarks>
		/// Unlike the Add for <see cref="IDictionary" />, the <see cref="MultiValueDictionary{TKey,TValue}"/> Add will not
		/// throw any exceptions. If the given <typeparamref name="TKey"/> is already in the <see cref="MultiValueDictionary{TKey,TValue}"/>,
		/// then <typeparamref name="TValue"/> will be added to <see cref="IReadOnlyCollection{TValue}"/> associated with <paramref name="key"/>
		/// </remarks>
		/// <remarks>
		/// A call to this Add method will always invalidate any currently running enumeration regardless
		/// of whether the Add method actually modified the <see cref="MultiValueDictionary{TKey, TValue}" />.
		/// </remarks>
		public void Add(TKey key, TValue value)
		{
			if(key == null)
				throw new ArgumentNullException("key");
			InnerCollectionView collection;
			if(!dictionary.TryGetValue(key, out collection))
			{
				collection = new InnerCollectionView(key, NewCollectionFactory());
				dictionary.Add(key, collection);
			}
			collection.AddValue(value);
			version++;
		}

		/// <summary>
		/// Adds a number of key-value pairs to this <see cref="MultiValueDictionary{TKey,TValue}"/>, where
		/// the key for each value is <paramref name="key"/>, and the value for a pair
		/// is an element from <paramref name="values"/>
		/// </summary>
		/// <param name="key">The <typeparamref name="TKey"/> of all entries to add</param>
		/// <param name="values">An <see cref="IEnumerable{TValue}"/> of values to add</param>
		/// <exception cref="ArgumentNullException"><paramref name="key"/> and <paramref name="values"/> must be non-null</exception>
		/// <remarks>
		/// A call to this AddRange method will always invalidate any currently running enumeration regardless
		/// of whether the AddRange method actually modified the <see cref="MultiValueDictionary{TKey,TValue}"/>.
		/// </remarks>
		public void AddRange(TKey key, IEnumerable<TValue> values)
		{
			if(key == null)
				throw new ArgumentNullException("key");
			if(values == null)
				throw new ArgumentNullException("values");

			InnerCollectionView collection;
			if(!dictionary.TryGetValue(key, out collection))
			{
				collection = new InnerCollectionView(key, NewCollectionFactory());
				dictionary.Add(key, collection);
			}
			foreach(TValue value in values)
			{
				collection.AddValue(value);
			}
			version++;
		}

		#endregion

		#region Members implemented from IReadOnlyDictionary<TKey, IReadOnlyCollection<TValue>>
		/*======================================================================
        ** Members implemented from IReadOnlyDictionary<TKey, IReadOnlyCollection<TValue>>
        ======================================================================*/

		/// <summary>
		/// Determines if the given <typeparamref name="TKey"/> exists within this <see cref="MultiValueDictionary{TKey,TValue}"/> and has
		/// at least one <typeparamref name="TValue"/> associated with it.
		/// </summary>
		/// <param name="key">The <typeparamref name="TKey"/> to search the <see cref="MultiValueDictionary{TKey,TValue}"/> for</param>
		/// <returns><c>true</c> if the <see cref="MultiValueDictionary{TKey,TValue}"/> contains the requested <typeparamref name="TKey"/>;
		/// otherwise <c>false</c>.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="key"/> must be non-null</exception>
		public bool ContainsKey(TKey key)
		{
			if(key == null)
				throw new ArgumentNullException("key");
			// Since modification to the MultiValueDictionary is only allowed through its own API, we
			// can ensure that if a collection is in the internal dictionary then it must have at least one
			// associated TValue, or else it would have been removed whenever its final TValue was removed.
			return dictionary.ContainsKey(key);
		}

		/// <summary>
		/// Gets each <typeparamref name="TKey"/> in this <see cref="MultiValueDictionary{TKey,TValue}"/> that
		/// has one or more associated <typeparamref name="TValue"/>.
		/// </summary>
		/// <value>
		/// An <see cref="IEnumerable{TKey}"/> containing each <typeparamref name="TKey"/> 
		/// in this <see cref="MultiValueDictionary{TKey,TValue}"/> that has one or more associated 
		/// <typeparamref name="TValue"/>.
		/// </value>
		public IEnumerable<TKey> Keys
		{
			get
			{
				return dictionary.Keys;
			}
		}

		/// <summary>
		/// Attempts to get the <typeparamref name="TValue"/> associated with the given
		/// <typeparamref name="TKey"/> and place it into <paramref name="value"/>.
		/// </summary>
		/// <param name="key">The <typeparamref name="TKey"/> of the element to retrieve</param>
		/// <param name="value">
		/// When this method returns, contains the <typeparamref name="TValue"/> associated with the specified
		/// <typeparamref name="TKey"/> if it is found; otherwise contains the default value of <typeparamref name="TValue"/>.
		/// </param>
		/// <returns>
		/// <c>true</c> if the <see cref="MultiValueDictionary{TKey,TValue}"/> contains an element with the specified 
		/// <typeparamref name="TKey"/>; otherwise, <c>false</c>.
		/// </returns>
		/// <exception cref="ArgumentNullException"><paramref name="key"/> must be non-null</exception>
		public bool TryGetValue(TKey key, out IReadOnlyCollection<TValue> value)
		{
			if(key == null)
				throw new ArgumentNullException("key");

			InnerCollectionView collection;
			var success = dictionary.TryGetValue(key, out collection);
			value = collection;
			return success;
		}

		/// <summary>
		/// Gets an enumerable of <see cref="IReadOnlyCollection{TValue}"/> from this <see cref="MultiValueDictionary{TKey,TValue}"/>,
		/// where each <see cref="IReadOnlyCollection{TValue}" /> is the collection of every <typeparamref name="TValue"/> associated
		/// with a <typeparamref name="TKey"/> present in the <see cref="MultiValueDictionary{TKey,TValue}"/>. 
		/// </summary>
		/// <value>An IEnumerable of each <see cref="IReadOnlyCollection{TValue}"/> in this 
		/// <see cref="MultiValueDictionary{TKey,TValue}"/></value>
		public IEnumerable<IReadOnlyCollection<TValue>> Values
		{
			get
			{
				return dictionary.Values;
			}
		}

		/// <summary>
		/// Get every <typeparamref name="TValue"/> associated with the given <typeparamref name="TKey"/>. If 
		/// <paramref name="key"/> is not found in this <see cref="MultiValueDictionary{TKey,TValue}"/>, will 
		/// throw a <see cref="KeyNotFoundException"/>.
		/// </summary>
		/// <param name="key">The <typeparamref name="TKey"/> of the elements to retrieve.</param>
		/// <exception cref="ArgumentNullException"><paramref name="key"/> must be non-null</exception>
		/// <exception cref="KeyNotFoundException"><paramref name="key"/> does not have any associated 
		/// <typeparamref name="TValue"/>s in this <see cref="MultiValueDictionary{TKey,TValue}"/>.</exception>
		/// <value>
		/// An <see cref="IReadOnlyCollection{TValue}"/> containing every <typeparamref name="TValue"/>
		/// associated with <paramref name="key"/>.
		/// </value>
		/// <remarks>
		/// Note that the <see cref="IReadOnlyCollection{TValue}"/> returned will change alongside any changes 
		/// to the <see cref="MultiValueDictionary{TKey,TValue}"/> 
		/// </remarks>
		public IReadOnlyCollection<TValue> this[TKey key]
		{
			get
			{
				if(key == null)
					throw new ArgumentNullException("key");

				InnerCollectionView collection;
				if(dictionary.TryGetValue(key, out collection))
					return collection;
				else
					throw new KeyNotFoundException();
			}
		}

		/// <summary>
		/// Returns the number of <typeparamref name="TKey"/>s with one or more associated <typeparamref name="TValue"/>
		/// in this <see cref="MultiValueDictionary{TKey,TValue}"/>.
		/// </summary>
		/// <value>The number of <typeparamref name="TKey"/>s in this <see cref="MultiValueDictionary{TKey,TValue}"/>.</value>
		public int Count
		{
			get
			{
				return dictionary.Count;
			}
		}

		/// <summary>
		/// Get an Enumerator over the <typeparamref name="TKey"/>-<see cref="IReadOnlyCollection{TValue}"/>
		/// pairs in this <see cref="MultiValueDictionary{TKey,TValue}"/>.
		/// </summary>
		/// <returns>an Enumerator over the <typeparamref name="TKey"/>-<see cref="IReadOnlyCollection{TValue}"/>
		/// pairs in this <see cref="MultiValueDictionary{TKey,TValue}"/>.</returns>
		public IEnumerator<KeyValuePair<TKey, IReadOnlyCollection<TValue>>> GetEnumerator()
		{
			return new Enumerator(this);
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return new Enumerator(this);
		}

		#endregion

		/// <summary>
		/// The Enumerator class for a <see cref="MultiValueDictionary{TKey, TValue}"/>
		/// that iterates over <typeparamref name="TKey"/>-<see cref="IReadOnlyCollection{TValue}"/>
		/// pairs.
		/// </summary>
		private class Enumerator :
			IEnumerator<KeyValuePair<TKey, IReadOnlyCollection<TValue>>>
		{
			private MultiValueDictionary<TKey, TValue> multiValueDictionary;
			private int version;
			private KeyValuePair<TKey, IReadOnlyCollection<TValue>> current;
			private Dictionary<TKey, InnerCollectionView>.Enumerator enumerator;
			private enum EnumerationState { BeforeFirst, During, AfterLast };
			private EnumerationState state;

			/// <summary>
			/// Constructor for the enumerator
			/// </summary>
			/// <param name="multiValueDictionary">A MultiValueDictionary to iterate over</param>
			internal Enumerator(MultiValueDictionary<TKey, TValue> multiValueDictionary)
			{
				this.multiValueDictionary = multiValueDictionary;
				this.version = multiValueDictionary.version;
				this.current = default(KeyValuePair<TKey, IReadOnlyCollection<TValue>>);
				this.enumerator = multiValueDictionary.dictionary.GetEnumerator();
				this.state = EnumerationState.BeforeFirst;
				;
			}

			public KeyValuePair<TKey, IReadOnlyCollection<TValue>> Current
			{
				get
				{
					return current;
				}
			}

			object IEnumerator.Current
			{
				get
				{
					switch(state)
					{
						case EnumerationState.BeforeFirst:
							throw new InvalidOperationException((Properties.Resources.InvalidOperation_EnumNotStarted));
						case EnumerationState.AfterLast:
							throw new InvalidOperationException((Properties.Resources.InvalidOperation_EnumEnded));
						default:
							return current;
					}
				}
			}

			/// <summary>
			/// Advances the enumerator to the next element of the collection.
			/// </summary>
			/// <returns>
			/// true if the enumerator was successfully advanced to the next element; false if the enumerator has passed the end of the collection.
			/// </returns>
			/// <exception cref="T:System.InvalidOperationException">The collection was modified after the enumerator was created. </exception>
			public bool MoveNext()
			{
				if(version != multiValueDictionary.version)
				{
					throw new InvalidOperationException(Properties.Resources.InvalidOperation_EnumFailedVersion);
				}
				else if(enumerator.MoveNext())
				{
					current = new KeyValuePair<TKey, IReadOnlyCollection<TValue>>(enumerator.Current.Key, enumerator.Current.Value);
					state = EnumerationState.During;
					return true;
				}
				else
				{
					current = default(KeyValuePair<TKey, IReadOnlyCollection<TValue>>);
					state = EnumerationState.AfterLast;
					return false;
				}
			}

			/// <summary>
			/// Sets the enumerator to its initial position, which is before the first element in the collection.
			/// </summary>
			/// <exception cref="T:System.InvalidOperationException">The collection was modified after the enumerator was created. </exception>
			public void Reset()
			{
				if(version != multiValueDictionary.version)
					throw new InvalidOperationException(Properties.Resources.InvalidOperation_EnumFailedVersion);
				enumerator.Dispose();
				enumerator = multiValueDictionary.dictionary.GetEnumerator();
				current = default(KeyValuePair<TKey, IReadOnlyCollection<TValue>>);
				state = EnumerationState.BeforeFirst;
			}

			/// <summary>
			/// Frees resources associated with this Enumerator
			/// </summary>
			public void Dispose()
			{
				enumerator.Dispose();
			}
		}

		/// <summary>
		/// An inner class that functions as a view of an ICollection within a MultiValueDictionary
		/// </summary>
		private class InnerCollectionView :
			ICollection<TValue>,
			IReadOnlyCollection<TValue>
		{
			private ICollection<TValue> collection;

			#region Private Concrete API
			/*======================================================================
            ** Private Concrete API
            ======================================================================*/

			public InnerCollectionView(TKey key, ICollection<TValue> collection)
			{
				this.collection = collection;
			}

			public void AddValue(TValue item)
			{
				collection.Add(item);
			}

			#endregion

			#region Shared API
			/*======================================================================
            ** Shared API
            ======================================================================*/

			public bool Contains(TValue item)
			{
				return collection.Contains(item);
			}

			public void CopyTo(TValue[] array, int arrayIndex)
			{
				if(array == null)
					throw new ArgumentNullException("array");
				if(arrayIndex < 0)
					throw new ArgumentOutOfRangeException("arrayIndex", Properties.Resources.ArgumentOutOfRange_NeedNonNegNum);
				if(arrayIndex > array.Length)
					throw new ArgumentOutOfRangeException("arrayIndex", Properties.Resources.ArgumentOutOfRange_Index);
				if(array.Length - arrayIndex < collection.Count)
					throw new ArgumentException(Properties.Resources.CopyTo_ArgumentsTooSmall, "arrayIndex");

				collection.CopyTo(array, arrayIndex);
			}

			public int Count
			{
				get
				{
					return collection.Count;
				}
			}

			public bool IsReadOnly
			{
				get
				{
					return true;
				}
			}

			public IEnumerator<TValue> GetEnumerator()
			{
				return collection.GetEnumerator();
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return this.GetEnumerator();
			}

			#endregion

			#region Public-Facing API
			/*======================================================================
            ** Public-Facing API
            ======================================================================*/

			void ICollection<TValue>.Add(TValue item)
			{
				throw new NotSupportedException(Properties.Resources.ReadOnly_Modification);
			}

			void ICollection<TValue>.Clear()
			{
				throw new NotSupportedException(Properties.Resources.ReadOnly_Modification);
			}

			bool ICollection<TValue>.Remove(TValue item)
			{
				throw new NotSupportedException(Properties.Resources.ReadOnly_Modification);
			}

			#endregion
		}
	}
}