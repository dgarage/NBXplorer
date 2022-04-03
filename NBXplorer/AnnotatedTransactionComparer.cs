using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class AnnotatedTransactionComparer : IComparer<AnnotatedTransaction>
	{
		bool youngToOld;
		AnnotatedTransactionComparer(bool youngToOld)
		{
			this.youngToOld = youngToOld;
		}
		private static readonly AnnotatedTransactionComparer _Youngness = new AnnotatedTransactionComparer(true);
		public static AnnotatedTransactionComparer YoungToOld
		{
			get
			{
				return _Youngness;
			}
		}
		private static readonly AnnotatedTransactionComparer _Oldness = new AnnotatedTransactionComparer(false);
		public static AnnotatedTransactionComparer OldToYoung
		{
			get
			{
				return _Oldness;
			}
		}
		public AnnotatedTransactionComparer Inverse()
		{
			return this == YoungToOld ? OldToYoung : YoungToOld;
		}
		public int Compare(AnnotatedTransaction a, AnnotatedTransaction b)
		{
			var result = CompareCore(a, b);
			if (!youngToOld)
				result = result * -1;
			return result;
		}
		int CompareCore(AnnotatedTransaction a, AnnotatedTransaction b)
		{
			var txIdCompare = a.Record.TransactionHash < b.Record.TransactionHash ? -1 :
							  a.Record.TransactionHash > b.Record.TransactionHash ? 1 : 0;
			var seenCompare = (a.Record.FirstSeen < b.Record.FirstSeen ? 1 :
							a.Record.FirstSeen > b.Record.FirstSeen ? -1 : txIdCompare);
			if (a.Height is long ah)
			{
				// Both confirmed, tie on height then firstSeen
				if (b.Height is long bh)
				{
					var heightCompare = (ah < bh ? 1 :
						   ah > bh ? -1 : txIdCompare);
					return ah == bh ?
						   // same height? use firstSeen on firstSeen
						   seenCompare :
						   // else tie on the height
						   heightCompare;
				}
				else
				{
					return 1;
				}
			}
			else if (b.Height is long bh)
			{
				return -1;
			}
			// Both unconfirmed, tie on firstSeen
			else
			{
				return seenCompare;
			}
		}
	}
}
