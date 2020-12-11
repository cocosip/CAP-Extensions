﻿// Copyright (c) .NET Core Community. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Monitoring;
using DotNetCore.CAP.Persistence;
using DotNetCore.CAP.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCore.CAP.Sqlite
{
    public class SqliteDataStorage : IDataStorage
    {
        private readonly IOptions<CapOptions> _capOptions;
        private readonly IOptions<SqliteOptions> _options;
        private readonly IStorageInitializer _initializer;
        private readonly ISerializer _serializer;
        private readonly string _pubName;
        private readonly string _recName;

        public SqliteDataStorage(
            IOptions<SqliteOptions> options,
            IOptions<CapOptions> capOptions,
            IStorageInitializer initializer,
            ISerializer serializer)
        {
            _capOptions = capOptions;
            _options = options;
            _initializer = initializer;
            _serializer = serializer;
            _pubName = initializer.GetPublishedTableName();
            _recName = initializer.GetReceivedTableName();
        }

        public async Task ChangePublishStateAsync(MediumMessage message, StatusName state) =>
            await ChangeMessageStateAsync(_pubName, message, state);

        public async Task ChangeReceiveStateAsync(MediumMessage message, StatusName state) =>
            await ChangeMessageStateAsync(_recName, message, state);

        public MediumMessage StoreMessage(string name, Message content, object dbTransaction = null)
        {
            var sql = $"INSERT INTO `{_pubName}` (`Id`,`Version`,`Name`,`Content`,`Retries`,`Added`,`ExpiresAt`,`StatusName`)" +
                $"VALUES(@Id,'{_options.Value.Version}',@Name,@Content,@Retries,@Added,@ExpiresAt,@StatusName)";

            var message = new MediumMessage
            {
                DbId = content.GetId(),
                Origin = content,
                Content = _serializer.Serialize(content),
                Added = DateTime.Now,
                ExpiresAt = null,
                Retries = 0
            };

            object[] sqlParams =
            {
                new SqliteParameter("@Id", long.Parse(message.DbId)),
                new SqliteParameter("@Name", name),
                new SqliteParameter("@Content", message.Content),
                new SqliteParameter("@Retries", message.Retries),
                new SqliteParameter("@Added", message.Added),
                new SqliteParameter("@ExpiresAt", message.ExpiresAt.HasValue ? (object)message.ExpiresAt.Value : DBNull.Value),
                new SqliteParameter("@StatusName", nameof(StatusName.Scheduled))
            };


            if (dbTransaction == null)
            {
                using var connection = SqliteFactory.Instance.CreateConnection();
                connection.ConnectionString = _options.Value.ConnectionString;
                connection.ExecuteNonQuery(sql, sqlParams: sqlParams);
            }
            else
            {
                var dbTrans = dbTransaction as IDbTransaction;
                if (dbTrans == null && dbTransaction is IDbContextTransaction dbContextTrans)
                    dbTrans = dbContextTrans.GetDbTransaction();

                var conn = dbTrans?.Connection;
                conn.ExecuteNonQuery(sql, dbTrans, sqlParams);
            }

            return message;
        }

        public void StoreReceivedExceptionMessage(string name, string group, string content)
        {
            object[] sqlParams =
            {
                new SqliteParameter("@Id", SnowflakeId.Default().NextId()),
                new SqliteParameter("@Name", name),
                new SqliteParameter("@Group", group),
                new SqliteParameter("@Content", content),
                new SqliteParameter("@Retries", _capOptions.Value.FailedRetryCount),
                new SqliteParameter("@Added", DateTime.Now),
                new SqliteParameter("@ExpiresAt", DateTime.Now.AddDays(15)),
                new SqliteParameter("@StatusName", nameof(StatusName.Failed))
            };

            StoreReceivedMessage(sqlParams);
        }

        public MediumMessage StoreReceivedMessage(string name, string group, Message message)
        {
            var mdMessage = new MediumMessage
            {
                DbId = SnowflakeId.Default().NextId().ToString(),
                Origin = message,
                Added = DateTime.Now,
                ExpiresAt = null,
                Retries = 0
            };

            object[] sqlParams =
            {
                new SqliteParameter("@Id", long.Parse(mdMessage.DbId)),
                new SqliteParameter("@Name", name),
                new SqliteParameter("@Group", group),
                new SqliteParameter("@Content",_serializer.Serialize(mdMessage.Origin)),
                new SqliteParameter("@Retries", mdMessage.Retries),
                new SqliteParameter("@Added", mdMessage.Added),
                new SqliteParameter("@ExpiresAt", mdMessage.ExpiresAt.HasValue ? (object) mdMessage.ExpiresAt.Value : DBNull.Value),
                new SqliteParameter("@StatusName", nameof(StatusName.Scheduled))
            };

            StoreReceivedMessage(sqlParams);
            return mdMessage;
        }

        public async Task<int> DeleteExpiresAsync(string table, DateTime timeout, int batchCount = 1000,
            CancellationToken token = default)
        {
            using var connection = SqliteFactory.Instance.CreateConnection();
            connection.ConnectionString = _options.Value.ConnectionString;

            var sql = $"DELETE FROM `{table}` WHERE `ExpiresAt` < @timeout AND `Id` IN (SELECT `Id` FROM `{table}` LIMIT @batchCount)";
            //var sql = $"DELETE FROM `{table}` WHERE `ExpiresAt` < @timeout limit @batchCount";

            var count = connection.ExecuteNonQuery(
               sql, null,
                new SqliteParameter("@timeout", timeout), new SqliteParameter("@batchCount", batchCount));

            return await Task.FromResult(count);
        }

        public async Task<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry() =>
            await GetMessagesOfNeedRetryAsync(_pubName);

        public async Task<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry() =>
            await GetMessagesOfNeedRetryAsync(_recName);

        public IMonitoringApi GetMonitoringApi()
        {
            return new SqliteMonitoringApi(_options, _initializer);
        }

        private async Task ChangeMessageStateAsync(string tableName, MediumMessage message, StatusName state)
        {
            var sql = $"UPDATE `{tableName}` SET `Retries`=@Retries,`ExpiresAt`=@ExpiresAt,`StatusName`=@StatusName WHERE `Id`=@Id";

            object[] sqlParams =
            {
                new SqliteParameter("@Retries", message.Retries),
                new SqliteParameter("@ExpiresAt",message.ExpiresAt.HasValue?(object)message.ExpiresAt:DBNull.Value),
                new SqliteParameter("@StatusName", state.ToString("G")),
                new SqliteParameter("@Id", long.Parse(message.DbId))
            };

            using var connection = SqliteFactory.Instance.CreateConnection();
            connection.ConnectionString = _options.Value.ConnectionString;

            connection.ExecuteNonQuery(sql, sqlParams: sqlParams);

            await Task.CompletedTask;
        }

        private void StoreReceivedMessage(object[] sqlParams)
        {
            var sql = $"INSERT INTO `{_recName}` (`Id`,`Version`,`Name`,`Group`,`Content`,`Retries`,`Added`,`ExpiresAt`,`StatusName`)" +
                $" VALUES (@Id,'{_capOptions.Value.Version}',@Name,@Group,@Content,@Retries,@Added,@ExpiresAt,@StatusName) ";
            using var connection = SqliteFactory.Instance.CreateConnection();
            connection.ConnectionString = _options.Value.ConnectionString;
            connection.ExecuteNonQuery(sql, sqlParams: sqlParams);
        }

        private async Task<IEnumerable<MediumMessage>> GetMessagesOfNeedRetryAsync(string tableName)
        {
            var fourMinAgo = DateTime.Now.AddMinutes(-4).ToString("O");
            var sql = $"SELECT `Id`,`Content`,`Retries`,`Added` FROM `{tableName}` WHERE `Retries`<{_capOptions.Value.FailedRetryCount} " +
                $"AND `Version`='{_capOptions.Value.Version}' AND `Added`<'{fourMinAgo}' " +
                $"AND (`StatusName`='{StatusName.Failed}' OR `StatusName`='{StatusName.Scheduled}') LIMIT 200";

            using var connection = SqliteFactory.Instance.CreateConnection();
            connection.ConnectionString = _options.Value.ConnectionString;
            var result = connection.ExecuteReader(sql, reader =>
            {
                var messages = new List<MediumMessage>();
                while (reader.Read())
                {
                    messages.Add(new MediumMessage
                    {
                        DbId = reader.GetInt64(0).ToString(),
                        Origin = _serializer.Deserialize(reader.GetString(1)),
                        Retries = reader.GetInt32(2),
                        Added = reader.GetDateTime(3)
                    });
                }

                return messages;
            });

            return await Task.FromResult(result);
        }
    }
}
