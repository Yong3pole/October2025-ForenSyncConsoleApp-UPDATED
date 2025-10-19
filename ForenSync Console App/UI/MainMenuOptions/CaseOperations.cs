using Microsoft.Data.Sqlite;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;

namespace ForenSync_Console_App.UI.MainMenuOptions
{
    public static class CaseOperations
    {
        public static void Show(string caseId, string userId, bool isNewCase)
        {
            AnsiConsole.Clear();
            AsciiTitle.Render("ForenSync");

            bool isLinux = OperatingSystem.IsLinux();
            bool hasActiveCase = !string.IsNullOrEmpty(caseId);

            if (isNewCase)
            {
                Console.WriteLine("🆕 Active Case\n");

                string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                string query = @"
                    SELECT 
                        c.case_id,
                        u.firstname || ' ' || u.lastname AS full_name,
                        u.role,
                        c.date
                    FROM case_logs c
                    JOIN users_tbl u ON c.user_id = u.user_id
                    WHERE c.case_id = @caseId
                    LIMIT 1";

                using var command = new SqliteCommand(query, connection);
                command.Parameters.AddWithValue("@caseId", caseId);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    string id = reader.GetString(0);
                    string user = reader.GetString(1);
                    string role = reader.GetString(2);
                    string rawDate = reader.GetString(3);

                    string formattedDate = DateTime.TryParse(rawDate, out var createdDate)
                        ? createdDate.ToString("MMM dd, yyyy")
                        : rawDate;

                    Console.WriteLine("📋 Case Summary:");
                    Console.WriteLine("───────────────────────────────────────────────────────────────────────────");
                    Console.WriteLine($"{id} | {user} ({role}) | Created: {formattedDate}");
                    Console.WriteLine("───────────────────────────────────────────────────────────────────────────\n");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("⚠️ Case not found in database.");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.WriteLine("📂 Welcome Back\n");
            }

            AnsiConsole.MarkupLine("[cyan]📂 Main Menu > Case Operations [/]");

            var choices = new List<string>
            {
                "📁 View Cases",
                "💽 View Mounted Drives",
                // Disable forensic operations based on OS AND case availability
                isLinux || !hasActiveCase ? "🧠 Capture Memory (disabled)" : "🧠 Capture Memory",
                isLinux || !hasActiveCase ? "🧲 Image/Clone Drive or Partition (disabled)" : "🧲 Image/Clone Drive or Partition",
                isLinux || !hasActiveCase ? "📱 Android Acquisition (disabled)" : "📱 Android Acquisition",
                "🔙 Back to Main Menu"
            };

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[white]Select an operation:[/]")
                    .PageSize(6)
                    .UseConverter(choice =>
                        choice.Contains("(disabled)") ? $"[grey]{choice}[/]" : choice)
                    .AddChoices(choices));

            if (selected.Contains("(disabled)"))
            {
                AnsiConsole.MarkupLine("[red]That option is disabled on this operating system.[/]");
                Show(caseId, userId, isNewCase);
                return;
            }

            switch (selected)
            {
                case "📁 View Cases":
                    CaseOperations_SubMenu.CaseViewer.Show(caseId, userId, isNewCase);
                    Show(caseId, userId, isNewCase);
                    break;

                case "💽 View Mounted Drives":
                    DeviceInfo_SubMenu.ViewDiskLayout.Show();
                    Show(caseId, userId, isNewCase);
                    break;

                case "🧠 Capture Memory":
                    CaseOperations_SubMenu.CaptureMemory.Run(caseId, userId, isNewCase);
                    Show(caseId, userId, isNewCase);
                    break;

                case "🧲 Image/Clone Drive or Partition":
                    CaseOperations_SubMenu.DriveImager.Show(caseId, userId, isNewCase);
                    Show(caseId, userId, isNewCase);
                    break;

                case "📱 Android Acquisition":
                    CaseOperations_SubMenu.AndroidAcquisition.Run(caseId, userId, isNewCase);
                    Show(caseId, userId, isNewCase);
                    break;

                case "🔙 Back to Main Menu":
                    MainMenu.Show(caseId, userId, isNewCase);
                    break;
            }
        }
    }
}