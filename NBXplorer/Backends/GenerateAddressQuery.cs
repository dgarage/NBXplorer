namespace NBXplorer.Backends
{
	public class GenerateAddressQuery
	{
		public GenerateAddressQuery()
		{

		}
		public GenerateAddressQuery(int? minAddresses, int? maxAddresses)
		{
			MinAddresses = minAddresses;
			MaxAddresses = maxAddresses;
		}
		public int? MinAddresses { get; set; }
		public int? MaxAddresses { get; set; }
	}
}
