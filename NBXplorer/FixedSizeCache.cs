using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBXplorer.Models;

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
			if (maxElements < 0)
				throw new ArgumentOutOfRangeException(nameof(maxElements));
			_Elements = new TValue[maxElements];
			_CalculateKey = calculateKey;
		}

		public int MaxElementsCount => _Elements.Length;

		public bool Contains(TValue obj)
		{
			if (_Elements.Length == 0)
				return false;
			if (obj is null)
				throw new ArgumentNullException(paramName: nameof(obj));
			var objKey = _CalculateKey(obj);
			TValue existingValue = _Elements[GetIndex(objKey)];
			if (existingValue is null)
				return false;
			return _CalculateKey(existingValue).Equals(objKey);
		}

		public void Remove(TValue obj)
		{
			if (_Elements.Length == 0)
				return;
			if (obj is null)
				throw new ArgumentNullException(paramName: nameof(obj));
			var objKey = _CalculateKey(obj);
			_Elements[GetIndex(objKey)] = default;
		}

		public void Add(TValue obj)
		{
			if (_Elements.Length == 0)
				return;
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
