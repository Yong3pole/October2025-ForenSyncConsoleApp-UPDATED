using ForenSync_Console_App.UI.MainMenuOptions;
using ForenSync_Console_App.UI.MainMenuOptions.PrepareTransfer_SubMenu;
using ForenSync_Console_App.UI.MainMenuOptions.UserManagement_SubMenu;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace ForenSync_Console_App.UI
{
    public static class MainMenu
    {
        public static void Show(string caseId, string userId, bool isNewCase)
        {
            string role = ""; // Declare role
            Console.Clear();
            AsciiTitle.Render("ForenSync");

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
                    role = reader.GetString(2);
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

           

            try
            {
                string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                var roleCommand = connection.CreateCommand();
                roleCommand.CommandText = "SELECT role FROM users_tbl WHERE user_id = $id LIMIT 1;";
                roleCommand.Parameters.AddWithValue("$id", userId);

                var result = roleCommand.ExecuteScalar();
                role = result?.ToString()?.Trim().ToLower() ?? "";
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]⚠️ Failed to retrieve user role:[/] {ex.Message}");
            }


            // Menu logic goes here...

            Console.ForegroundColor = ConsoleColor.Cyan;

            Console.WriteLine("📂 Main Menu");
            Console.WriteLine("────────────────────────────────────────────");
            Console.ResetColor();

            AnsiConsole.MarkupLine("[green]Use the [bold]↑[/] and [bold]↓[/] arrow keys to navigate. Press [bold]Enter[/] to select an option.[/]\n");


            var menuOptions = new List<string>
            {
                "🧭 Case Operations",
                "🛠️ Tools",
                "💻 Device Info",
                role == "admin" ? "👤 Update User Access" : "[grey]👤 Update User Access (disabled)[/]",
                "🔑 Change My Password",
                "❓ Help",
                "🚪 Exit"
            };


            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[white]Choose an option to continue:[/]")
                    .PageSize(8)
                    .AddChoices(menuOptions)
            );

            switch (choice)
            {
                case "🧭 Case Operations":
                    CaseOperations.Show(caseId, userId, isNewCase);
                    break;

                case "🛠️ Tools":
                    Tools.Show(caseId, userId, isNewCase);
                    break;

                case "💻 Device Info":
                    DeviceInfo.Show(caseId, userId, isNewCase);
                    break;

                case "👤 Update User Access":
                    if (role != "admin")
                    {
                        AnsiConsole.MarkupLine("[red]❌ Access denied. Admins only.[/]");
                        Console.ReadLine();
                        Show(caseId, userId, false); // ✅ Return with context
                        return;
                    }
                    SyncUser.Render(caseId, userId, isNewCase); // ✅ Pass full context
                    break;


                case "[grey]👤 Update User Access (disabled)[/]":
                    AnsiConsole.MarkupLine("[red]❌ This option is disabled for non-admin users.[/]");
                    Console.ReadLine();
                    Show(caseId, userId, false);
                    return;

                case "🔑 Change My Password":
                    ChangePassword.Render(caseId, userId, isNewCase);
                    break;

                case "❓ Help":
                    Help.Show(caseId, userId, isNewCase);
                    break;

                case "🚪 Exit":
                    AnsiConsole.Clear();
                    AsciiTitle.Render("ForenSync");

                    AnsiConsole.MarkupLine("[yellow]⚠️ Are you sure you want to exit this case session?[/]");
                    AnsiConsole.MarkupLine("[grey]Press [[1]] to confirm, [[2]] to cancel.[/]");

                    var confirm = Console.ReadKey(true).Key;

                    if (confirm == ConsoleKey.D1)
                    {
                        AnsiConsole.Clear();
                        AnsiConsole.MarkupLine("[green]🔄 Returning to login screen...[/]");
                        System.Threading.Thread.Sleep(1500); // Optional UX pause
                        LoginPage.PromptCredentials(); // ✅ Return to login
                    }
                    else
                    {
                        Console.Clear();
                        Show(caseId, userId, false); // ✅ Reload main menu without summary
                    }
                    break;


                // Add other cases later
                default:
                    AnsiConsole.MarkupLine($"[red]→ Option not yet implemented: {choice}[/]");
                    break;
            }

        }
    }

}
