using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace DotNetCore.CAP.Sqlite.Tests
{
    public class ConnectionUtil
    {
        private const string ConnectionStringTemplateVariable = "Cap_Sqlite_ConnectionString";
        public static string GetDatabasePath()
        {
            return $"{Path.Combine(AppContext.BaseDirectory, "captest.db")}";
        }

        public static string GetConnectionString()
        {
            return
                Environment.GetEnvironmentVariable(ConnectionStringTemplateVariable) ??
                $"Data Source={Path.Combine(AppContext.BaseDirectory, "captest.db")};";
        }

        public static SqliteConnection CreateConnection(string connectionString = null)
        {
            connectionString ??= GetConnectionString();
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }
    }
}
