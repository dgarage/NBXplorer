using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NBXplorer
{
    public class NBXplorerDefaultSettings
    {
		static NBXplorerDefaultSettings()
		{
			_Settings = new Dictionary<NetworkType, NBXplorerDefaultSettings>();
			foreach(var networkType in new[] { NetworkType.Mainnet, NetworkType.Testnet, NetworkType.Regtest })
			{
				var settings = new NBXplorerDefaultSettings();
				_Settings.Add(networkType, settings);
				settings.DefaultDataDirectory = StandardConfiguration.DefaultDataDirectory.GetDirectory("NBXplorer", GetFolderName(networkType), false);
				settings.DefaultConfigurationFile = Path.Combine(settings.DefaultDataDirectory, "settings.config");
				settings.DefaultCookieFile = Path.Combine(settings.DefaultDataDirectory, ".cookie");
				settings.DefaultPort = (networkType == NetworkType.Mainnet ? 24444 :
													  networkType == NetworkType.Regtest ? 24446 :
													  networkType == NetworkType.Testnet ? 24445 : throw new NotSupportedException(networkType.ToString()));
				settings.DefaultUrl = new Uri($"http://127.0.0.1:{settings.DefaultPort}/", UriKind.Absolute);
			}
		}

		public static string GetFolderName(NetworkType networkType)
		{
			switch(networkType)
			{
				case NetworkType.Mainnet:
					return "Main";
				case NetworkType.Regtest:
					return "RegTest";
				case NetworkType.Testnet:
					return "TestNet";
			}
			throw new NotSupportedException();
		}

		static Dictionary<NetworkType, NBXplorerDefaultSettings> _Settings;
		public string DefaultDataDirectory
		{
			get;
			set;
		}
		public string DefaultConfigurationFile
		{
			get;
			set;
		}
		public string DefaultCookieFile
		{
			get;
			private set;
		}
		public int DefaultPort
		{
			get;
			set;
		}
		public Uri DefaultUrl
		{
			get;
			set;
		}

		public static NBXplorerDefaultSettings GetDefaultSettings(NetworkType networkType)
		{
			return _Settings[networkType];
		}
	}
}
