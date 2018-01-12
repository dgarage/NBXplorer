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
			_Settings = new Dictionary<ChainType, NBXplorerDefaultSettings>();
			foreach(var chainType in new[] { ChainType.Main, ChainType.Test, ChainType.Regtest })
			{
				var settings = new NBXplorerDefaultSettings();
				_Settings.Add(chainType, settings);
				settings.ChainType = chainType;
				settings.DefaultDataDirectory = StandardConfiguration.DefaultDataDirectory.GetDirectory("NBXplorer", chainType.ToNetwork().Name);
				settings.DefaultConfigurationFile = Path.Combine(settings.DefaultDataDirectory, "settings.config");
				settings.DefaultCookieFile = Path.Combine(settings.DefaultDataDirectory, ".cookie");
				settings.DefaultPort = (chainType == ChainType.Main ? 24444 :
													  chainType == ChainType.Regtest ? 24446 :
													  chainType == ChainType.Test ? 24445 : throw new NotSupportedException(chainType.ToString()));
				settings.DefaultUrl = new Uri($"http://127.0.0.1:{settings.DefaultPort}/", UriKind.Absolute);
			}
		}

		static Dictionary<ChainType, NBXplorerDefaultSettings> _Settings;

		public ChainType ChainType
		{
			get;
			set;
		}
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

		public static NBXplorerDefaultSettings GetDefaultSettings(ChainType chainType)
		{
			return _Settings[chainType];
		}
	}
}
