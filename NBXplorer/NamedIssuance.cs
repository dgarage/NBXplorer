using NBitcoin;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin.DataEncoders;

namespace ElementsExplorer
{
    public class NamedIssuance
    {
		public string Name
		{
			get;
			set;
		}
		public uint256 AssetId
		{
			get;
			set;
		}

		public static NamedIssuance Extract(Transaction tx)
		{
			var assetId = tx.Inputs
				.Select(txin => txin.GetIssuedAssetId())
				.FirstOrDefault(asset => asset != null);
			if(assetId == null)
				return null;

			try
			{

				var name = tx.Outputs
								.Select(txout => TxNullDataTemplate.Instance.ExtractScriptPubKeyParameters(txout.ScriptPubKey))
								.Where(data => data != null && data.Length == 1)
								.Select(data => Encoding.UTF8.GetString(data.First()))
								.FirstOrDefault();
                if(name == null)
                    return null;
				return new NamedIssuance() { Name = name, AssetId = assetId };
			}
			catch { return null; }
		}
    }
}
