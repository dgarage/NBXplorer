using NBitcoin;
using NBXplorer.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class CookieRepository
	{
		public CookieRepository(ExplorerConfiguration config)
		{
			_Config = config;
		}

		ExplorerConfiguration _Config;
		bool _Initialized;
		public void Initialize()
		{
			if(!_Config.NoAuthentication && !_Initialized)
			{
				var cookieFile = Path.Combine(_Config.DataDir, ".cookie");
				var pass = new uint256(RandomUtils.GetBytes(32));
				var user = "__cookie__";
				var cookieStr = user + ":" + pass;
				File.WriteAllText(cookieFile, cookieStr);
				_Creds = new NetworkCredential(user, pass.ToString());
			}
			_Initialized = true;
		}

		NetworkCredential _Creds;
		public NetworkCredential GetCredentials()
		{
			if(!_Initialized)
				Initialize();
			return _Creds;
		}
	}
}
