using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace FtpExplorer.Data
{
    public class PasswordContext : DbContext
    {
        public DbSet<Password> Passwords { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=passwords.sqlite");
        }
    }

    public class HistoryContext : DbContext
    {
        public DbSet<HistoryEntry> Histories { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=histories.sqlite");
        }
    }

    public class Password
    {
        public int PasswordId { get; set; }
        [Required]
        public string Host { get; set; }
        [Required]
        public int Port { get; set; }
        [Required]
        public string UserName { get; set; }
        [Required]
        public byte[] EncryptedPassword { get; set; }
    }

    public class HistoryEntry
    {
        public int HistoryEntryId { get; set; }
        [Required]
        public DateTimeOffset Time { get; set; }
        [Required]
        public string Url { get; set; }
    }
}
