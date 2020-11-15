// Copyright (c) .NET Core Community. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Monitoring;
using DotNetCore.CAP.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DotNetCore.CAP.Sqlite
{
    public class SqliteMonitoringApi : IMonitoringApi
    {
        private readonly SqliteOptions _options;
        private readonly string _pubName;
        private readonly string _recName;

        public SqliteMonitoringApi(IOptions<SqliteOptions> options, IStorageInitializer initializer)
        {
            _options = options.Value ?? throw new ArgumentNullException(nameof(options));
            _pubName = initializer.GetPublishedTableName();
            _recName = initializer.GetReceivedTableName();
        }

        public async Task<MediumMessage> GetPublishedMessageAsync(long id) => await GetMessageAsync(_pubName, id);

        public async Task<MediumMessage> GetReceivedMessageAsync(long id) => await GetMessageAsync(_recName, id);

        public StatisticsDto GetStatistics()
        {
            var sql = $"SELECT (SELECT COUNT(Id) FROM `{_pubName}` WHERE `StatusName` = 'Succeeded') AS PublishedSucceeded," +
                $" (SELECT COUNT(Id) FROM `{_recName}` WHERE `StatusName` = 'Succeeded') AS ReceivedSucceeded," +
                $" (SELECT COUNT(Id) FROM `{_pubName}` WHERE `StatusName` = 'Failed') AS PublishedFailed," +
                $" (SELECT COUNT(Id) FROM `{_recName}` WHERE `StatusName` = 'Failed') AS ReceivedFailed;";
            StatisticsDto statistics;

            using (var connection = SqliteFactory.Instance.CreateConnection())
            {
                connection.ConnectionString = _options.ConnectionString;
                statistics = connection.ExecuteReader(sql, reader =>
                {
                    var statisticsDto = new StatisticsDto();

                    while (reader.Read())
                    {
                        statisticsDto.PublishedSucceeded = reader.GetInt32(0);
                        statisticsDto.ReceivedSucceeded = reader.GetInt32(1);
                        statisticsDto.PublishedFailed = reader.GetInt32(2);
                        statisticsDto.ReceivedFailed = reader.GetInt32(3);
                    }

                    return statisticsDto;
                });
            }

            return statistics;
        }

        public IList<MessageDto> Messages(MessageQueryDto queryDto)
        {
            var tableName = queryDto.MessageType == MessageType.Publish ? _pubName : _recName;
            var where = string.Empty;

            if (!string.IsNullOrEmpty(queryDto.StatusName))
            {
                where += " AND `StatusName` = @StatusName";
            }
            if (!string.IsNullOrEmpty(queryDto.Name))
            {
                where += " AND `Name` = @Name";
            }
            if (!string.IsNullOrEmpty(queryDto.Group))
            {
                where += " AND `Group` = @Group";
            }
            if (!string.IsNullOrEmpty(queryDto.Content))
            {
                where += " AND `Content` LIKE @Content";
            }
            var sqlQuery = $"SELECT * FROM `{tableName}` WHERE 1=1 {where} ORDER BY `Added` DESC LIMIT @Offset,@Limit";

            object[] sqlParams = new SqliteParameter[]
            {
                new SqliteParameter("@StatusName", queryDto.StatusName ?? string.Empty),
                new SqliteParameter("@Group", queryDto.Group ?? string.Empty),
                new SqliteParameter("@Name", queryDto.Name ?? string.Empty),
                new SqliteParameter("@Content", $"%{queryDto.Content}%"),
                new SqliteParameter("@Offset", queryDto.CurrentPage * queryDto.PageSize),
                new SqliteParameter("@Limit", queryDto.PageSize)
            };

            using var connection = SqliteFactory.Instance.CreateConnection();
            connection.ConnectionString = _options.ConnectionString;
            return connection.ExecuteReader(sqlQuery, reader =>
            {
                var messages = new List<MessageDto>();

                while (reader.Read())
                {
                    var index = 0;
                    messages.Add(new MessageDto
                    {
                        Id = reader.GetInt64(index++),
                        Version = reader.GetString(index++),
                        Name = reader.GetString(index++),
                        Group = queryDto.MessageType == MessageType.Subscribe ? reader.GetString(index++) : default,
                        Content = reader.GetString(index++),
                        Retries = reader.GetInt32(index++),
                        Added = reader.GetDateTime(index++),
                        ExpiresAt = reader.GetDateTime(index++),
                        StatusName = reader.GetString(index)
                    });
                }

                return messages;
            }, sqlParams);
        }

        public int PublishedFailedCount()
        {
            return GetNumberOfMessage(_pubName, nameof(StatusName.Failed));
        }

        public int PublishedSucceededCount()
        {
            return GetNumberOfMessage(_pubName, nameof(StatusName.Succeeded));
        }

        public int ReceivedFailedCount()
        {
            return GetNumberOfMessage(_recName, nameof(StatusName.Failed));
        }

        public int ReceivedSucceededCount()
        {
            return GetNumberOfMessage(_recName, nameof(StatusName.Succeeded));
        }

        public IDictionary<DateTime, int> HourlySucceededJobs(MessageType type)
        {
            var tableName = type == MessageType.Publish ? _pubName : _recName;
            return GetHourlyTimelineStats(tableName, nameof(StatusName.Succeeded));
        }

        public IDictionary<DateTime, int> HourlyFailedJobs(MessageType type)
        {
            var tableName = type == MessageType.Publish ? _pubName : _recName;
            return GetHourlyTimelineStats(tableName, nameof(StatusName.Failed));
        }

        private int GetNumberOfMessage(string tableName, string statusName)
        {
            var sqlQuery = $"SELECT COUNT(`Id`) FROM `{tableName}` WHERE `StatusName` = @state";

            using var connection = SqliteFactory.Instance.CreateConnection();
            connection.ConnectionString = _options.ConnectionString;
            var count = connection.ExecuteScalar<int>(sqlQuery, new SqliteParameter("@state", statusName));
            return count;
        }

        private Dictionary<DateTime, int> GetHourlyTimelineStats(string tableName, string statusName)
        {
            var endDate = DateTime.Now;
            var dates = new List<DateTime>();
            for (var i = 0; i < 24; i++)
            {
                dates.Add(endDate);
                endDate = endDate.AddHours(-1);
            }

            var keyMaps = dates.ToDictionary(x => x.ToString("yyyy-MM-dd-HH"), x => x);

            return GetTimelineStats(tableName, statusName, keyMaps);
        }

        private Dictionary<DateTime, int> GetTimelineStats(
            string tableName,
            string statusName,
            IDictionary<string, DateTime> keyMaps)
        {
            var sqlQuery = $"SELECT aggr.* FROM " +
                $"(SELECT strftime('%Y-%m-%d-%H', `Added`) AS Key, COUNT(`Id`) AS Count FROM `{tableName}` " +
                $"WHERE `StatusName` = @statusName GROUP BY strftime('%Y-%m-%d-%H', `Added`) ) " +
                $"AS aggr WHERE aggr.`Key`>=@minKey AND `Key`<=@maxKey;";

            object[] sqlParams =
            {
                new SqliteParameter("@statusName", statusName),
                new SqliteParameter("@minKey", keyMaps.Keys.Min()),
                new SqliteParameter("@maxKey", keyMaps.Keys.Max())
            };

            Dictionary<string, int> valuesMap;

            using (var connection = SqliteFactory.Instance.CreateConnection())
            {
                connection.ConnectionString = _options.ConnectionString;
                valuesMap = connection.ExecuteReader(sqlQuery, reader =>
                {
                    var dictionary = new Dictionary<string, int>();

                    while (reader.Read())
                    {
                        dictionary.Add(reader.GetString(0), reader.GetInt32(1));
                    }

                    return dictionary;
                }, sqlParams);
            }

            foreach (var key in keyMaps.Keys)
            {
                if (!valuesMap.ContainsKey(key))
                    valuesMap.Add(key, 0);
            }

            var result = new Dictionary<DateTime, int>();
            for (var i = 0; i < keyMaps.Count; i++)
            {
                var value = valuesMap[keyMaps.ElementAt(i).Key];
                result.Add(keyMaps.ElementAt(i).Value, value);
            }

            return result;
        }

        private async Task<MediumMessage> GetMessageAsync(string tableName, long id)
        {
            var sql = $"SELECT `Id` AS `DbId`, `Content`, `Added`, `ExpiresAt`, `Retries` FROM `{tableName}` WHERE `Id`={id}";

            using var connection = SqliteFactory.Instance.CreateConnection();
            connection.ConnectionString = _options.ConnectionString;
            var mediumMessage = connection.ExecuteReader(sql, reader =>
            {
                MediumMessage message = null;

                while (reader.Read())
                {
                    message = new MediumMessage
                    {
                        DbId = reader.GetInt64(0).ToString(),
                        Content = reader.GetString(1),
                        Added = reader.GetDateTime(2),
                        ExpiresAt = reader.GetDateTime(3),
                        Retries = reader.GetInt32(4)
                    };
                }

                return message;
            });

            return await Task.FromResult(mediumMessage);
        }
    }
}
