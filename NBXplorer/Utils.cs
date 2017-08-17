using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ElementsExplorer
{
    public static class Utils
    {
		public static IEnumerable<T> TopologicalSort<T>(this IEnumerable<T> nodes,
												Func<T, IEnumerable<T>> dependsOn)
		{
			List<T> result = new List<T>();
			var elems = nodes.ToDictionary(node => node,
										   node => new HashSet<T>(dependsOn(node)));
			while(elems.Count > 0)
			{
				var elem = elems.FirstOrDefault(x => x.Value.Count == 0);
				if(elem.Key == null)
				{
					//cycle detected can't order
					return nodes;
				}
				elems.Remove(elem.Key);
				foreach(var selem in elems)
				{
					selem.Value.Remove(elem.Key);
				}
				result.Add(elem.Key);
			}
			return result;
		}
	}
}
