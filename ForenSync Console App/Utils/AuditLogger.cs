using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ForenSync.Utils
{
    public enum AuditAction
    {
        Image,
        MemCapture,
        AndroidAcquisition,
        ExportedSnapshot,
        AddUser,
        ChangePassword,
        AccessCase,
        CreateCase,
        Login,
        LoginFailed,
        ViewCase,
        ViewUserConfig,
        ViewHistory,
        ContactSupport,
        ViewDocs
    }

    public static class AuditLogger
    {
        public static void Log(string userId, AuditAction action, string context = null)
        {
            try
            {
                string createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string canonical = $"{userId}|{action}|{createdAt}|{context ?? ""}";
                string auditHash = ComputeSha256(canonical);

                // Generate GUID for acquisition_id
                string eventId = Guid.NewGuid().ToString();

                string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO audit_trail (event_id, user_id, action, created_at, context, audit_hash)
                    VALUES ($eventId, $userId, $action, $createdAt, $context, $auditHash);";

                command.Parameters.AddWithValue("$eventId", eventId);
                command.Parameters.AddWithValue("$userId", userId);
                command.Parameters.AddWithValue("$action", action.ToString());
                command.Parameters.AddWithValue("$createdAt", createdAt);
                command.Parameters.AddWithValue("$context", context ?? "");
                command.Parameters.AddWithValue("$auditHash", auditHash);

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AuditLogger] Failed to log action: {ex.Message}");
            }
        }

        private static string ComputeSha256(string input)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            byte[] hash = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
