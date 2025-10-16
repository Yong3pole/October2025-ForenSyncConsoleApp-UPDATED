using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using ForenSync.Utils;

namespace ForenSync_Console_App.UI.MainMenuOptions.UserManagement_SubMenu
{
    public static class SyncUser
    {
        public static void Render(string caseId, string userId, bool isNewCase)
        {
            AnsiConsole.Clear();
            AsciiTitle.Render("Update User Access");

            AnsiConsole.MarkupLine("[yellow]This will refresh local user access permissions from the central system.[/]");
            AnsiConsole.MarkupLine("[grey]Press [[1]] to proceed, [[2]] to cancel.[/]\n");

            var confirm = Console.ReadKey(true).Key;
            if (confirm != ConsoleKey.D1)
            {
                AnsiConsole.Clear();
                MainMenu.Show(caseId, userId, isNewCase);
                return;
            }

            string sourceDbPath = PromptForDbPath();
            if (sourceDbPath == null)
            {
                MainMenu.Show(caseId, userId, isNewCase);
                return;
            }

            try
            {
                using var sourceConnection = new SqliteConnection($"Data Source={sourceDbPath}");
                sourceConnection.Open();

                var fetchCommand = sourceConnection.CreateCommand();
                fetchCommand.CommandText = @"
                    SELECT user_id, lastname, firstname, badge_num, department, role, password, created_at, created_by, active
                    FROM users_tbl
                    WHERE active = 1;
                ";

                using var reader = fetchCommand.ExecuteReader();
                int count = 0;

                string localDbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
                using var localConnection = new SqliteConnection($"Data Source={localDbPath}");
                localConnection.Open();

                while (reader.Read())
                {
                    // Check if user already exists in local users_tbl
                    var checkCommand = localConnection.CreateCommand();
                    checkCommand.CommandText = "SELECT COUNT(*) FROM users_tbl WHERE user_id = $uid;";
                    checkCommand.Parameters.AddWithValue("$uid", reader.GetString(0));

                    bool exists = Convert.ToInt32(checkCommand.ExecuteScalar()) > 0;
                    if (exists)
                        continue; // Skip existing users

                    // Insert new active user
                    var insertCommand = localConnection.CreateCommand();
                    insertCommand.CommandText = @"
                    INSERT INTO users_tbl (
                        user_id, lastname, firstname, badge_num, department, role, password, created_at, created_by, active
                    ) VALUES (
                        $uid, $last, $first, $badge, $dept, $role, $pass, $createdAt, $createdBy, $active
                    );
                ";

                    insertCommand.Parameters.AddWithValue("$uid", reader.GetString(0));
                    insertCommand.Parameters.AddWithValue("$last", reader.GetString(1));
                    insertCommand.Parameters.AddWithValue("$first", reader.GetString(2));
                    insertCommand.Parameters.AddWithValue("$badge", reader.GetString(3));
                    insertCommand.Parameters.AddWithValue("$dept", reader.GetString(4));
                    insertCommand.Parameters.AddWithValue("$role", reader.GetString(5));
                    insertCommand.Parameters.AddWithValue("$pass", reader.GetString(6));
                    insertCommand.Parameters.AddWithValue("$createdAt", reader.GetString(7));
                    insertCommand.Parameters.AddWithValue("$createdBy", reader.GetString(8));
                    insertCommand.Parameters.AddWithValue("$active", reader.GetBoolean(9));

                    insertCommand.ExecuteNonQuery();
                    count++;

                }

                AuditLogger.Log(userId, AuditAction.ViewUserConfig, $"Synced {count} active users from {sourceDbPath} into local users_tbl");

                AnsiConsole.MarkupLine($"\n[green]✅ Synced {count} active users into local database.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]❌ Sync failed:[/] {ex.Message}");
                AnsiConsole.MarkupLine("[grey]Press Enter to try again...[/]");
                Console.ReadLine();
                Render(caseId, userId, isNewCase);
                return;
            }

            AnsiConsole.MarkupLine("\n[grey]Press Enter to return to main menu.[/]");
            Console.ReadLine();
            MainMenu.Show(caseId, userId, isNewCase);
        }

        private static string? PromptForDbPath()
        {
            while (true)
            {
                AnsiConsole.Clear();
                AsciiTitle.Render("Locate forensync.db");

                AnsiConsole.MarkupLine("[yellow]Please locate 'forensync.db' via File Explorer and paste its full path below.[/]");
                AnsiConsole.MarkupLine("[grey]Example: C:\\Cases\\Active\\forensync.db[/]\n");

                AnsiConsole.Markup("[blue]Path:[/] ");
                string? inputPath = Console.ReadLine()?.Trim();

                if (string.IsNullOrWhiteSpace(inputPath))
                {
                    AnsiConsole.MarkupLine("[red]❌ No path entered.[/]");
                }
                else if (!File.Exists(inputPath))
                {
                    AnsiConsole.MarkupLine("[red]❌ File not found.[/]");
                }
                else if (Path.GetFileName(inputPath) != "forensync.db")
                {
                    AnsiConsole.MarkupLine("[red]❌ File must be strictly named 'forensync.db'.[/]");
                }
                else
                {
                    return inputPath;
                }

                AnsiConsole.MarkupLine("\n[grey]Press Enter to try again or type 'cancel' to abort.[/]");
                string? retry = Console.ReadLine()?.Trim().ToLower();
                if (retry == "cancel")
                    return null;
            }
        }
    }
}
