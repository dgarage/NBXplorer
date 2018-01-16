using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class Bookmark
    {
		public Bookmark(uint160 value)
		{
			if(value == null)
				throw new ArgumentNullException(nameof(value));
			_Value = value;
		}

		private uint160 _Value;

		private static readonly Bookmark _Start = new Bookmark(uint160.Zero);
		public static Bookmark Start
		{
			get
			{
				return _Start;
			}
		}


		public override bool Equals(object obj)
		{
			Bookmark item = obj as Bookmark;
			if(item == null)
				return false;
			return _Value.Equals(item._Value);
		}
		public static bool operator ==(Bookmark a, Bookmark b)
		{
			if(System.Object.ReferenceEquals(a, b))
				return true;
			if(((object)a == null) || ((object)b == null))
				return false;
			return a._Value == b._Value;
		}

		public static bool operator !=(Bookmark a, Bookmark b)
		{
			return !(a == b);
		}

		public override int GetHashCode()
		{
			return _Value.GetHashCode();
		}

		public override string ToString()
		{
			return _Value.ToString();
		}
	}
}
