namespace NBXplorer.Controllers;

public static class CommonRoutes
{
	public const string BaseCryptoEndpoint = "cryptos/{cryptoCode}";
	public const string BaseDerivationEndpoint = $"{BaseCryptoEndpoint}/derivations";
	public const string DerivationEndpoint =  $"{BaseCryptoEndpoint}/derivations/{{derivationScheme}}";
	public const string AddressEndpoint =  $"{BaseCryptoEndpoint}/addresses/{{address}}";
	public const string GroupEndpoint =  $"{BaseCryptoEndpoint}/groups/{{groupId}}";
	public const string TrackedSourceEndpoint =  $"{BaseCryptoEndpoint}/tracked-sources/{{trackedSource}}";
	public const string TransactionsPath = "transactions/{txId?}";
}