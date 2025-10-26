using System;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ForenSync.Utils
{
    public static class ErrorLogger
    {
        /// <summary>
        /// Logs an error to the error_logs table with full context and timestamp.
        /// </summary>
        public static void Log(string userId, string action, string message, string context = "")
        {
            try
            {
                string createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string canonical = $"{userId}|{action}|{createdAt}|{message}|{context}";
                string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO error_logs (created_at, user_id, action, message, context)
                    VALUES (@created_at, @user_id, @action, @message, @context);";

                command.Parameters.AddWithValue("@created_at", createdAt);
                command.Parameters.AddWithValue("@user_id", userId);
                command.Parameters.AddWithValue("@action", action);
                command.Parameters.AddWithValue("@message", message);
                command.Parameters.AddWithValue("@context", context ?? "");

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ErrorLogger] Failed to log error: {ex.Message}");
            }
        }
    }
}
