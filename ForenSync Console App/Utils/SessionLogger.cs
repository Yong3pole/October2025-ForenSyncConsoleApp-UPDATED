using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace ForenSync_Console_App.Data
{
    public static class SessionLogger
    {
        public static string StartSession(string userId)
        {
            string sessionId = $"SESS_{Guid.NewGuid().ToString().Substring(0, 8)}";
            string loginTime = DateTime.Now.ToString("MM/dd/yyyy | HH:mm:ss");
            string terminalEnv = Environment.OSVersion.Platform.ToString().ToLower();

            string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO session_tbl (session_id, user_id, login_time, terminal_env)
                VALUES ($sessionId, $userId, $loginTime, $terminalEnv);";

            command.Parameters.AddWithValue("$sessionId", sessionId);
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$loginTime", loginTime);
            command.Parameters.AddWithValue("$terminalEnv", terminalEnv);

            command.ExecuteNonQuery();
            return sessionId;
        }

        public static void EndSession(string sessionId)
        {
            string logoutTime = DateTime.Now.ToString("MM/dd/yyyy | HH:mm:ss");

            string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE session_tbl
                SET logout_time = $logoutTime
                WHERE session_id = $sessionId;";

            command.Parameters.AddWithValue("$logoutTime", logoutTime);
            command.Parameters.AddWithValue("$sessionId", sessionId);

            int rowsAffected = command.ExecuteNonQuery();
            Console.WriteLine($"🔒 Session {sessionId} ended — rows affected: {rowsAffected}");
        }
    }
}
