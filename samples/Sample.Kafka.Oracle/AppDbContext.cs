﻿using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Sample.Kafka.Oracle
{
    public class Person
    {
        [Key]
        public int Id { get; set; }

        public string Name { get; set; }

        public override string ToString()
        {
            return $"Name:{Name}, Id:{Id}";
        }
    }


    public class AppDbContext : DbContext
    {
        public const string ConnectionString = "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=192.168.0.5)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=ORCL)));User Id=KPACS;Password=123456";
        public DbSet<Person> Persons { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseOracle(ConnectionString, c =>
            {
                //Oracle version
                c.UseOracleSQLCompatibility("11");
            });
        }
    }
}
