using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class FixedSizeCache<TValue, TKey>
	{
		readonly TValue[] _Elements;
		readonly Func<TValue, TKey> _CalculateKey;
		public FixedSizeCache(int maxElements, Func<TValue, TKey> calculateKey)
		{
			if (calculateKey == null)
				throw new ArgumentNullException(nameof(calculateKey));
			if (maxElements <= 0)
				throw new ArgumentOutOfRangeException(nameof(maxElements));
			_Elements = new TValue[maxElements];
			_CalculateKey = calculateKey;
		}

		public int MaxElementsCount => _Elements.Length;

		public bool Contains(TValue obj)
		{
			if (object.ReferenceEquals(null, obj))
				throw new ArgumentNullException(paramName: nameof(obj));
			var objKey = _CalculateKey(obj);
			TValue existingValue = _Elements[GetIndex(objKey)];
			if (object.ReferenceEquals(null, existingValue))
				return false;
			return _CalculateKey(existingValue).Equals(objKey);
		}

		public void Add(TValue obj)
		{
			_Elements[GetIndex(obj)] = obj;
		}

		private int GetIndex(TValue obj)
		{
			return GetIndex(_CalculateKey(obj));
		}

		private int GetIndex(TKey key)
		{
			if (object.ReferenceEquals(null, key))
				throw new ArgumentNullException(paramName: nameof(key));
			return Math.Abs(key.GetHashCode()) % _Elements.Length;
		}
	}
}
