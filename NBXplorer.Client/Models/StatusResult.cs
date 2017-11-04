using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class StatusResult
    {
		public int ChainHeight
		{
			get; set;
		}

		public bool Connected
		{
			get; set;
		}
		public double RepositoryPingTime
		{
			get;
			set;
		}
	}
}
