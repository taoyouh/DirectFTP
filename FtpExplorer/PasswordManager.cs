using FtpExplorer.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace FtpExplorer
{
    class PasswordManager
    {
        static PasswordManager current;
        public static PasswordManager Current
        {
            get
            {
                if (current == null)
                    current = new PasswordManager();
                return current;
            }
        }


        public async Task SavePasswordAsync(string host, int port, string userName, string password)
        {
            using (PasswordContext dbContext = new PasswordContext())
            {
                byte[] encryptedPassword = await EncryptAsync(password);
                bool createNew = false;

                var oldEntries = dbContext.Passwords.Where(x => x.Host == host && x.Port == port && x.UserName == userName);
                var firstOldEntry = await oldEntries.FirstOrDefaultAsync();
                if (firstOldEntry != null)
                {
                    firstOldEntry.EncryptedPassword = encryptedPassword;
                }
                else
                {
                    createNew = true;
                }
                dbContext.RemoveRange(oldEntries.Skip(1));

                if (createNew)
                {
                    var newEntry = new Password
                    {
                        Host = host,
                        Port = port,
                        UserName = userName,
                        EncryptedPassword = encryptedPassword
                    };
                    await dbContext.AddAsync(newEntry);
                }

                await dbContext.SaveChangesAsync();
            }
        }

        public async Task<string> GetPasswordAsync(string host, int port, string userName)
        {
            using (PasswordContext dbContext = new PasswordContext())
            {
                var entry = await dbContext.Passwords.FirstOrDefaultAsync(
                    x => x.Host == host && x.Port == port && x.UserName == userName);
                if (entry == null)
                    return null;
                string password = await DecryptAsync(entry.EncryptedPassword);
                return password;
            }
        }

        public async Task<IEnumerable<string>> GetUserNamesAsync(string host, int port)
        {
            using (PasswordContext dbContext = new PasswordContext())
            {
                var entries = dbContext.Passwords.Where(x => x.Host == host && x.Port == port);
                var userNames = entries.Select(x => x.UserName);
                return await userNames.ToArrayAsync();
            }
        }

        public async Task<IEnumerable<string>> GetUserNamesAsync(string host, int port, string startsWith)
        {
            using (PasswordContext dbContext = new PasswordContext())
            {
                var entries = from Password item in dbContext.Passwords
                              where item.Host == host
                              where item.Port == port
                              where item.UserName.StartsWith(startsWith)
                              select item.UserName;
                return await entries.ToArrayAsync();
            }
        }

        private async Task<byte[]> EncryptAsync(string plainText)
        {
            DataProtectionProvider provider = new DataProtectionProvider("LOCAL=user");
            var plainBuffer = CryptographicBuffer.ConvertStringToBinary(plainText, BinaryStringEncoding.Utf8);
            var protectedBuffer = await provider.ProtectAsync(plainBuffer);
            CryptographicBuffer.CopyToByteArray(protectedBuffer, out byte[] protectedBytes);
            return protectedBytes;
        }

        private async Task<string> DecryptAsync(byte[] protectedBytes)
        {
            DataProtectionProvider provider = new DataProtectionProvider("LOCAL=user");
            var protectedBuffer = CryptographicBuffer.CreateFromByteArray(protectedBytes);
            var plainBuffer = await provider.UnprotectAsync(protectedBuffer);
            return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, plainBuffer);
        }
    }
}
