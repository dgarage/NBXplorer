using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace NBXplorer.Filters
{
	public class RPCAuthorization
	{
		private readonly List<string> authorized;
		private readonly List<IPAddress> allowIp;

		public RPCAuthorization()
		{
			this.allowIp = new List<IPAddress>();
			this.authorized = new List<string>();
		}

		public List<string> Authorized
		{
			get
			{
				return this.authorized;
			}
		}

		public List<IPAddress> AllowIp
		{
			get
			{
				return this.allowIp;
			}
		}

		public bool IsAuthorized(string user)
		{
			if(this.Authorized.Count == 0)
				return true;
			if(user == null)
				return false;
			return this.Authorized.Any(a => a.Equals(user, StringComparison.OrdinalIgnoreCase));
		}
		public bool IsAuthorized(IPAddress ip)
		{
			if(ip == null)
				throw new ArgumentNullException(nameof(ip));
			
			return this.AllowIp.Count == 0 || this.AllowIp.Any(i => i.AddressFamily == ip.AddressFamily && i.Equals(ip));
		}
	}
}
