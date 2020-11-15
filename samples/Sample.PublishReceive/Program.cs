using DotNetCore.CAP;
using DotNetCore.CAP.InMemoryMessageQueue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.PublishReceive
{
    class Program
    {

        static ICapPublisher _capPublisher;
        static ILogger _logger;
        static int _count = 0;

        static void Main(string[] args)
        {
            IServiceCollection services = new ServiceCollection();
            services
                .AddLogging(l =>
                {
                    l.SetMinimumLevel(LogLevel.Information);
                    l.AddConsole();
                })
                .AddCap(options =>
                {
                    options.FailedRetryCount = 10;
                    options.FailedRetryInterval = 240;
                    options.ConsumerThreadCount = 3;
                    options.SucceedMessageExpiredAfter = 3600 * 12;

                    //Memory queue
                    options.UseInMemoryMessageQueue();

                    options.UseSqlite(o =>
                    {
                        o.Schema = "cap";
                        o.ConnectionString = "Data Source=D:\\cap-test.db;";
                    });
                });


            var provider = services.BuildServiceProvider();
            _capPublisher = provider.GetService<ICapPublisher>();
            _logger = provider.GetService<ILogger<Program>>();

            RunPublish();

            Console.WriteLine("cap test run!");

            Console.ReadLine();
        }

        public static void RunPublish()
        {
            for (int i = 0; i < 3; i++)
            {
                Task.Run(() =>
                {
                    while (_count < 10000000)
                    {
                        var stopwatch = new Stopwatch();
                        stopwatch.Start();
                        try
                        {
                            _capPublisher.Publish("cap-sqlite-test", $"cap sqlite test,{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss fff")}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Publish message fail:{0}", ex.Message);
                        }
                        finally
                        {
                            stopwatch.Stop();
                            _logger.LogInformation("Publish message index '{0}' ,cost:'{1}' ms.", _count, stopwatch.Elapsed.TotalMilliseconds);
                        }
                        Interlocked.Increment(ref _count);
                    }

                });
            }
        }
    }
}
