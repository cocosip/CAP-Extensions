using Dapper;
using DotNetCore.CAP;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System;
using System.Data;
using System.Threading.Tasks;

namespace Sample.Kafka.Sqlite.Controllers
{
    [Route("api/[controller]")]
    public class ValuesController : Controller
    {
        private readonly ICapPublisher _capBus;

        public ValuesController(ICapPublisher capPublisher)
        {
            _capBus = capPublisher;
        }

        [Route("~/without/transaction")]
        public async Task<IActionResult> WithoutTransaction()
        {
            await _capBus.PublishAsync("sample.kafka.sqlite", DateTime.Now);
            return Ok();
        }

        [Route("~/adonet/transaction")]
        public IActionResult AdonetWithTransaction()
        {
            using (var connection = new SqliteConnection(AppDbContext.ConnectionString))
            {
                using (var transaction = connection.BeginTransaction(_capBus, true))
                {
                    //your business code

                    connection.Execute("INSERT INTO \"Persons\" (\"Name\") VALUES('test')", transaction: (IDbTransaction)transaction.DbTransaction);

                    _capBus.Publish("sample.kafka.sqlite", DateTime.Now);
                }
            }

            return Ok();
        }

        [Route("~/ef/transaction")]
        public IActionResult EntityFrameworkWithTransaction([FromServices] AppDbContext dbContext)
        {
            using (var trans = dbContext.Database.BeginTransaction(_capBus, autoCommit: false))
            {
                dbContext.Persons.Add(new Person() { Name = "ef.transaction" });

                for (int i = 0; i < 1; i++)
                {
                    _capBus.Publish("sample.kafka.sqlite", DateTime.Now);
                }

                dbContext.SaveChanges();

                trans.Commit();
            }

            //using (dbContext.Database.BeginTransaction(_capBus, autoCommit: true))
            //{
            //    dbContext.Persons.Add(new Person() { Name = "ef.transaction" });

            //    _capBus.Publish("sample.kafka.oracle", DateTime.Now);
            //}

            return Ok();
        }

        [NonAction]
        [CapSubscribe("sample.kafka.sqlite")]
        public void Subscriber(DateTime p)
        {
            Console.WriteLine($@"{DateTime.Now} Subscriber invoked, Info: {p}");
        }

        [NonAction]
        [CapSubscribe("sample.kafka.sqlite", Group = "group.test2")]
        public void Subscriber2(DateTime p, [FromCap] CapHeader header)
        {
            Console.WriteLine($@"{DateTime.Now} Subscriber invoked, Info: {p}");
        }
    }
}
