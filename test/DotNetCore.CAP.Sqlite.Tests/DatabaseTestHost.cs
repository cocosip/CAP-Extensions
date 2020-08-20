using DotNetCore.CAP.Persistence;
using System.Threading;

namespace DotNetCore.CAP.Sqlite.Tests
{
    public abstract class DatabaseTestHost : TestHost
    {
        private static bool _sqlObjectInstalled;
        public static object _lock = new object();

        protected override void PostBuildServices()
        {
            base.PostBuildServices();
            lock (_lock)
            {
                if (!_sqlObjectInstalled)
                {
                    InitializeDatabase();
                }
            }
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        private void InitializeDatabase()
        {
            using (CreateScope())
            {
                var storage = GetService<IStorageInitializer>();
                var token = new CancellationTokenSource().Token;
                storage.InitializeAsync(token).GetAwaiter().GetResult();
                _sqlObjectInstalled = true;
            }
        }
    }
}
