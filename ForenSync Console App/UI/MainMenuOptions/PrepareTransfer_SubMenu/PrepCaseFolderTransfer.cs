using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using ForenSync.Utils;

namespace ForenSync_Console_App.UI.MainMenuOptions.PrepareTransfer_SubMenu
{
    public static class PrepCaseFolderTransfer
    {
        public static void Render(string caseId, string userId)
        {
            Console.Clear();
            AsciiTitle.Render("Prepare Case for Transfer");

            if (string.IsNullOrWhiteSpace(caseId))
            {
                AnsiConsole.MarkupLine("[red]❌ No active case. Please select a case before preparing for transfer.[/]");
                Console.ReadLine();
                MainMenu.Show(caseId, userId, false);
                return;
            }

            string caseFolderPath = Path.Combine(AppContext.BaseDirectory, "cases", caseId);
            string outboxPath = Path.Combine(AppContext.BaseDirectory, "transfer_outbox");
            string manifestPath = Path.Combine(outboxPath, $"{caseId}_manifest.txt");
            string zipPath = Path.Combine(outboxPath, $"{caseId}_package.zip");

            Directory.CreateDirectory(outboxPath);

            try
            {
                // Step 1: Collect files
                var files = Directory.GetFiles(caseFolderPath, "*", SearchOption.AllDirectories);
                var manifestBuilder = new StringBuilder();
                manifestBuilder.AppendLine($"Case ID: {caseId}");
                manifestBuilder.AppendLine($"Operator ID: {userId}");
                manifestBuilder.AppendLine($"Timestamp: {DateTime.UtcNow:o}");
                manifestBuilder.AppendLine("──────────────────────────────────────────────");
                manifestBuilder.AppendLine("Files and SHA-256 Hashes:");

                using var sha256 = SHA256.Create();
                foreach (var file in files)
                {
                    byte[] content = File.ReadAllBytes(file);
                    byte[] hash = sha256.ComputeHash(content);
                    string hashHex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                    manifestBuilder.AppendLine($"{Path.GetFileName(file)} → {hashHex}");
                }

                // Step 2: Chain of custody summary
                manifestBuilder.AppendLine("\nChain of Custody Summary:");
                string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                var custodyCommand = connection.CreateCommand();
                custodyCommand.CommandText = @"
                    SELECT timestamp, actor, action, details 
                    FROM chain_of_custody 
                    WHERE case_id = $caseId 
                    ORDER BY timestamp ASC;";
                custodyCommand.Parameters.AddWithValue("$caseId", caseId);

                using var reader = custodyCommand.ExecuteReader();
                while (reader.Read())
                {
                    string ts = reader.GetString(0);
                    string actor = reader.GetString(1);
                    string action = reader.GetString(2);
                    string details = reader.GetString(3);
                    manifestBuilder.AppendLine($"[{ts}] {actor} → {action}: {details}");
                }

                // Step 3: Write manifest
                File.WriteAllText(manifestPath, manifestBuilder.ToString());

                // Step 4: Zip case folder + manifest
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(caseFolderPath, zipPath);
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
                {
                    zip.CreateEntryFromFile(manifestPath, Path.GetFileName(manifestPath));
                }

                // Step 5: Log action
                byte[] manifestBytes = File.ReadAllBytes(manifestPath);
                string manifestHash = BitConverter.ToString(sha256.ComputeHash(manifestBytes)).Replace("-", "").ToLowerInvariant();
                AuditLogger.Log(userId, AuditAction.ExportedSnapshot, $"Manifest Hash: {manifestHash}");

                // Step 6: Prompt success
                AnsiConsole.MarkupLine("\n[green]✅ Case packaged successfully.[/]");
                AnsiConsole.MarkupLine("[grey]You may now transfer the folder to the browser app for analysis.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]❌ Packaging failed:[/] {ex.Message}");
                AnsiConsole.MarkupLine("[grey]Press Enter to try again...[/]");
                Console.ReadLine();
                Render(caseId, userId); // Retry
                return;
            }

            AnsiConsole.MarkupLine("\n[grey]Press Enter to return to main menu.[/]");
            Console.ReadLine();
            MainMenu.Show(caseId, userId, false);
        }
    }
}
