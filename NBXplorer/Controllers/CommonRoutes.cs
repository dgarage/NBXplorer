namespace NBXplorer.Controllers;

public static class CommonRoutes
{
	public const string BaseCryptoEndpoint = "cryptos/{cryptoCode}";
	public const string BaseDerivationEndpoint = $"{BaseCryptoEndpoint}/derivations";
	public const string DerivationEndpoint =  $"{BaseCryptoEndpoint}/derivations/{{derivationScheme}}";
	public const string AddressEndpoint =  $"{BaseCryptoEndpoint}/addresses/{{address}}";
	public const string BaseGroupEndpoint = $"groups";
	public const string GroupEndpoint =  $"{BaseGroupEndpoint}/{{groupId}}";
	public const string TransactionsPath = "transactions/{txId?}";
}