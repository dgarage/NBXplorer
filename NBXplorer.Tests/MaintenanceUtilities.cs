using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace NBXplorer.Tests
{
	public class MaintenanceUtilities
	{
		public MaintenanceUtilities(ITestOutputHelper helper)
		{
			Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
			Logs.LogProvider = new XUnitLoggerProvider(helper);
		}
		[Fact]
		[Trait("Maintenance", "Maintenance")]
		public async Task GenerateFullSchema()
		{
			using var t = ServerTester.Create(Backend.Postgres);
			var script = await GenerateDbScript(t);
			File.WriteAllText(GetFullSchemaFile(), script);
		}

		private static string GetFullSchemaFile()
		{
			return Path.Combine(TryGetSolutionDirectoryInfo().FullName, "NBXplorer", "DBScripts", "FullSchema.sql");
		}

		public static DirectoryInfo TryGetSolutionDirectoryInfo(string currentPath = null)
		{
			var directory = new DirectoryInfo(
				currentPath ?? Directory.GetCurrentDirectory());
			while (directory != null && !directory.GetFiles("*.sln").Any())
			{
				directory = directory.Parent;
			}
			return directory;
		}


		private async Task<string> GenerateDbScript(ServerTester t)
		{
			var dbName = new Npgsql.NpgsqlConnectionStringBuilder(t.PostgresConnectionString).Database;
			var output = await Run("docker", "exec", "-ti", "nbxplorertests_postgres_1", "pg_dump", "-U", "postgres", "-d", dbName,
				"--schema-only",
				"--no-privileges",
				"--no-owner",
				"--exclude-table=nbxv1_migrations");
			var script = String.Join("\n", output.Select(o => IsComment(o) ? "\n" : o).Where(o => !IsSet(o) && !SetSearchPath(o)));


			var output2 = await Run("docker", "exec", "-ti", "nbxplorertests_postgres_1", "pg_dump", "-U", "postgres", "-d", dbName,
				"--no-privileges",
				"--no-owner",
				"--inserts",
				"--table=nbxv1_migrations");

			script += String.Join("\n",
				output2
				.Select(o => IsComment(o) ? "\n" : o)
				.Where(o => !IsSet(o) && !SetSearchPath(o))
				.Select(o => Regex.Replace(o, "(.*) VALUES \\((.*),(.*)\\)", "$1 VALUES ($2)")));
			script = Regex.Replace(script, "(public\\.)(.*)", "$2");
			script = TrimWhiteSpaces(script);
			script = "-- Generated with MaintenanceUtilities.GeneratedFullSchema\n\n" + script;
			return script;
		}

		private bool SetSearchPath(string o)
		{
			return o.Contains("pg_catalog.set_config");
		}

		string TrimWhiteSpaces(string result)
		{
			while (true)
			{
				var newResult = result.Replace("\n\n\n", "\n\n");
				if (result == newResult)
					break;
				result = newResult;
			}
			return result.Trim();
		}

		private async Task<string[]> Run(string filename, params string[] args)
		{
			var runner = new ProcessRunner();
			var spec = new ProcessSpec()
			{
				Executable = filename,
				Arguments = args,
				OutputCapture = new OutputCapture()
			};
			await runner.RunAsync(spec, default);
			return spec.OutputCapture.Lines.ToArray();
		}
		static bool IsComment(string v)
		{
			return (v.Length >= 2 && v[0] == '-' && v[1] == '-');
		}
		static bool IsSet(string v)
		{
			return (v.Length >= 3 && v[0..3] == "SET");
		}
	}
}
