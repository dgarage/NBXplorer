#nullable enable
namespace NBXplorer
{
	public record ImportRPCMode
	{
		private readonly string mode;

		public static readonly ImportRPCMode? DescriptorsReadOnly = new ImportRPCMode("Descriptors (Read-Only)");
		public static readonly ImportRPCMode? Descriptors = new ImportRPCMode("Descriptors");
		public static readonly ImportRPCMode? Legacy = new ImportRPCMode("Legacy");

		public static ImportRPCMode? Parse(string? mode)
		{
			if (mode is null)
				return null;
			if (mode.Equals("true", System.StringComparison.OrdinalIgnoreCase) ||
				mode.Equals("Legacy", System.StringComparison.OrdinalIgnoreCase))
				return ImportRPCMode.Legacy;
			if (mode.Equals("Descriptors", System.StringComparison.OrdinalIgnoreCase))
				return ImportRPCMode.Descriptors;
			if (mode.Equals("Descriptors (Read-Only)", System.StringComparison.OrdinalIgnoreCase))
				return ImportRPCMode.DescriptorsReadOnly;
			return null;
		}
		ImportRPCMode(string mode)
		{
			this.mode = mode;
		}
		public override string ToString()
		{
			return mode;
		}
	}
}
