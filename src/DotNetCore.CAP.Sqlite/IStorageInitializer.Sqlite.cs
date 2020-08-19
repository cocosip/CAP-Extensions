// Copyright (c) .NET Core Community. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using DotNetCore.CAP.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCore.CAP.Sqlite
{
    public class SqliteStorageInitializer : IStorageInitializer
    {
        private readonly ILogger _logger;
        private readonly IOptions<SqliteOptions> _options;


        public SqliteStorageInitializer(
            ILogger<SqliteStorageInitializer> logger,
            IOptions<SqliteOptions> options)
        {
            _options = options;
            _logger = logger;
        }

        public virtual string GetPublishedTableName()
        {
            return $"{_options.Value.Schema}.Published";
        }

        public virtual string GetReceivedTableName()
        {
            return $"{_options.Value.Schema}.Received";
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return;

            var sql = CreateDbTablesScript(_options.Value.Schema);
            using (var connection = new SqliteConnection(_options.Value.ConnectionString))
                connection.ExecuteNonQuery(sql);

            await Task.CompletedTask;

            _logger.LogDebug("Ensuring all create database tables script are applied.");
        }


        protected virtual string CreateDbTablesScript(string schema)
        {
            var batchSql = $"CREATE TABLE IF NOT EXISTS `{GetReceivedTableName()}` (" +
                $"`Id` bigint NOT NULL," +
                $"`Version` varchar(20) DEFAULT NULL COLLATE NOCASE," +
                $"`Name` varchar(400) NOT NULL COLLATE NOCASE," +
                $"`Group` varchar(200) DEFAULT NULL COLLATE NOCASE," +
                $"`Content` longtext," +
                $"`Retries` int(11) DEFAULT NULL," +
                $"`Added` datetime NOT NULL," +
                $"`ExpiresAt` datetime DEFAULT NULL," +
                $"`StatusName` varchar(50) NOT NULL COLLATE NOCASE," +
                $"PRIMARY KEY (`Id`));  " +
                $"CREATE TABLE IF NOT EXISTS `{GetPublishedTableName()}` (" +
                $"`Id` bigint NOT NULL," +
                $"`Version` varchar(20) DEFAULT NULL COLLATE NOCASE," +
                $"`Name` varchar(200) NOT NULL COLLATE NOCASE," +
                $"`Content` longtext," +
                $"`Retries` int(11) DEFAULT NULL," +
                $"`Added` datetime NOT NULL," +
                $"`ExpiresAt` datetime DEFAULT NULL," +
                $"`StatusName` varchar(40) NOT NULL COLLATE NOCASE," +
                $"PRIMARY KEY (`Id`))";
            return batchSql;
        }
    }
}
