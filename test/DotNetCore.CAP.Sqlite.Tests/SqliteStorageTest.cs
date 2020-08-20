using Dapper;
using System.IO;
using Xunit;

namespace DotNetCore.CAP.Sqlite.Tests
{
    [Collection("Sqlite")]
    public class SqliteStorageTest : DatabaseTestHost
    {
        [Fact]
        public void Database_IsExists()
        {
            var databasePath = ConnectionUtil.GetDatabasePath();
            Assert.True(File.Exists(databasePath));
        }

        [Theory]
        [InlineData("cap.Published")]
        [InlineData("cap.Received")]
        public void DatabaseTable_IsExists(string tableName)
        {
            using (var connection = ConnectionUtil.CreateConnection())
            {
                var sql = $"SELECT COUNT(*) FROM `sqlite_master` WHERE `type`='table' AND `name` = '{tableName}'";
                var result = connection.QueryFirstOrDefault<int>(sql);
                Assert.Equal(1, result);
            }
        }

    }
}
