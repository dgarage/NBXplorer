using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class KeyPathTemplate
	{
		private readonly KeyPath preIndexes;
		private readonly KeyPath postIndexes;

		public KeyPath PostIndexes => postIndexes;

		public KeyPath PreIndexes => preIndexes;

		public static KeyPathTemplate Parse(string path)
		{
			if (path == null)
				throw new ArgumentNullException(nameof(path));
			if (!TryParse(path, out var v))
				throw new FormatException("Invalid keypath template");
			return v;
		}
		public static bool TryParse(string path, out KeyPathTemplate keyPathTemplate)
		{
			if (path == null)
				throw new ArgumentNullException(nameof(path));
			bool isValid = true;
			uint[] preIndices = null, postIndices = null;
			bool isPreIndex = true;
			int count = 0;
			List<uint> indices = new List<uint>();
			foreach (var p in path
			.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
			.Where(p => p != "m"))
			{
				if (p == "*")
				{
					if (isPreIndex)
					{
						isPreIndex = false;
						count++;
						preIndices = indices.ToArray();
						indices.Clear();
					}
					else
					{
						isValid = false;
					}
				}
				else
				{
					isValid &= TryParseCore(p, out var i);
					if(isValid)
						indices.Add(i);
					count++;
				}
			}
			if (count > 255)
				isValid = false;
			isValid &= preIndices != null;
			if (!isValid)
			{
				keyPathTemplate = null;
				return false;
			}
			postIndices = indices.ToArray();
			keyPathTemplate = new KeyPathTemplate(preIndices, postIndices);
			return true;
		}

		public bool TryMatchTemplate(KeyPath keyPath, out uint index)
		{
			index = 0;
			if (keyPath.Length != 1 + PreIndexes.Length + PostIndexes.Length)
				return false;
			for (int i = 0; i < PreIndexes.Length; i++)
			{
				if (PreIndexes[i] != keyPath[i])
					return false;
			}
			for (int i = 0; i < PostIndexes.Length; i++)
			{
				if (PostIndexes[i] != keyPath[i + 1 + PreIndexes.Length])
					return false;
			}
			index = keyPath[PreIndexes.Length];
			return true;
		}

		private static bool TryParseCore(string i, out uint index)
		{
			if (i.Length == 0)
			{
				index = 0;
				return false;
			}
			bool hardened = i[i.Length - 1] == '\'' || i[i.Length - 1] == 'h';
			var nonhardened = hardened ? i.Substring(0, i.Length - 1) : i;
			if (!uint.TryParse(nonhardened, out index))
				return false;
			if (hardened)
			{
				if (index >= 0x80000000u)
				{
					index = 0;
					return false;
				}
				index = index | 0x80000000u;
				return true;
			}
			else
			{
				return true;
			}
		}

		public KeyPathTemplate(uint[] preIndexes, uint[] postIndexes) : this(new KeyPath(preIndexes ?? Array.Empty<uint>()),
																			new KeyPath(postIndexes ?? Array.Empty<uint>()))
		{
		}
		public KeyPathTemplate(KeyPath preIndexes, KeyPath postIndexes)
		{
			this.preIndexes = preIndexes ?? new KeyPath();
			this.postIndexes = postIndexes ?? new KeyPath();
		}

		public KeyPath GetKeyPath(uint index)
		{
			return PreIndexes.Derive(index).Derive(PostIndexes);
		}
		public KeyPath GetKeyPath(int index, bool hardened)
		{
			return PreIndexes.Derive(index, hardened).Derive(PostIndexes);
		}

		public override string ToString()
		{
			StringBuilder builder = new StringBuilder(PreIndexes.Length + PostIndexes.Length + 20);
			if (PreIndexes.Length != 0)
				builder.Append($"{PreIndexes}/*");
			else
				builder.Append("*");
			if (PostIndexes.Length != 0)
				builder.Append($"/{PostIndexes}");
			return builder.ToString();
		}
	}
}
