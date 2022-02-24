using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using System.Text;

namespace NBXplorer.Client
{
	public static class DBUtils
	{
		public static string nbxv1_get_wallet_id(string cryptoCode, string addressOrDerivation)
		{
			return Encoders.Base64.EncodeData(Hashes.SHA256(new UTF8Encoding(false).GetBytes($"{cryptoCode}|{addressOrDerivation}")), 0, 21);
		}
		public static string nbxv1_get_descriptor_id(string cryptoCode, string strategy, string feature)
		{
			return Encoders.Base64.EncodeData(Hashes.SHA256(new UTF8Encoding(false).GetBytes($"{cryptoCode}|{strategy}|{feature}")), 0, 21);
		}
	}
}
