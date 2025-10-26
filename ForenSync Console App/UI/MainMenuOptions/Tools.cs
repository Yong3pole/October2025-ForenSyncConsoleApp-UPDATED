using ForenSync_Console_App.UI.MainMenuOptions.Tools_SubMenu;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ForenSync_Console_App.UI.MainMenuOptions
{
    public static class Tools
    {
        public static void Show(string caseId, string userId, bool isNewCase)
        {
            AnsiConsole.Clear();
            AsciiTitle.Render("ForenSync");

            string caseFolderPath = string.IsNullOrWhiteSpace(caseId)
                ? null
                : Path.Combine(AppContext.BaseDirectory, "Cases", caseId);

            if (isNewCase) // Show summary only for new cases 
            {
                Console.WriteLine("🆕 Starting New Case\n");

                string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
                string connectionString = $"Data Source={dbPath}";

                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                // Query to get case details along with user info
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

                    DateTime createdDate;
                    string formattedDate = DateTime.TryParse(rawDate, out createdDate)
                        ? createdDate.ToString("MMM dd, yyyy")
                        : rawDate;

                    

                    // Display the case summary
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

            AnsiConsole.MarkupLine("[cyan]📂 Main Menu > Tools [/]");
            AnsiConsole.MarkupLine("────────────────────────────────────────────");

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[white]Select a tool:[/]")
                    .PageSize(5)
                    .AddChoices(new[]
                    {
                        "📁 View running processes",
                        "📊 View running processes (WMI)",
                        "🛜 List network connections",
                        "⏏️ List usb devices / removable media",
                        "🔙 Back to Main Menu"
                    }));

            switch (choice)
            {
                case "📁 View running processes":
                    AnsiConsole.MarkupLine("[yellow]→ Listing running processes...[/]");
                    Tools_SubMenu.ViewProcesses.Show(caseFolderPath, userId);
                    Show(caseId, userId, isNewCase);
                    break;

                case "📊 View running processes (WMI)":
                    AnsiConsole.MarkupLine("[yellow]→ Listing WMI processes...[/]");
                    Tools_SubMenu.ViewProcessesWMI.Show(caseFolderPath, userId);
                    Show(caseId, userId, isNewCase);
                    break;

                case "🛜 List network connections":
                    AnsiConsole.MarkupLine("[yellow]→ Listing network connections...[/]");
                    ViewNetworkConnections.Show(caseFolderPath, userId);
                    Show(caseId, userId, isNewCase);
                    break;

                case "⏏️ List usb devices / removable media":
                    ViewUsbDevices.Show();
                    Show(caseId, userId, isNewCase);
                    break;


                case "🔙 Back to Main Menu":
                    // bool isNewCase = true; // for the Main Menu to show the summary if returning from Tools
                    MainMenu.Show(caseId, userId, isNewCase);
                    break;

                default:
                    AnsiConsole.MarkupLine("[red]Invalid choice. Please try again.[/]");
                    break;
            }
        }
    }
}
