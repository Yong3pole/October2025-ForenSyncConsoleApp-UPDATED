using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ForenSync_Console_App.Data
{
    public static class UserAuthenticator
    {
        public static bool ValidateUser(string userId, string password)
        {
            string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) FROM users_tbl
                WHERE user_id = $userId AND password = $password AND active = 1;";

            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$password", password);

            long count = (long)command.ExecuteScalar();
            return count == 1;
        }

        public static string AuthenticateAndStartSession(string userId, string password)
        {
            if (!ValidateUser(userId, password))
            {
                return null; // ❌ Invalid credentials — no session started
            }

            string sessionId = SessionLogger.StartSession(userId); // ✅ Track login
            return sessionId;
        }
    }
}
