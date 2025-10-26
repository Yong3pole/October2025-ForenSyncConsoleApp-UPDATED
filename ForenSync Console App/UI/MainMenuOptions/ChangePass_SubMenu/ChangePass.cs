using ForenSync.Utils;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using System;
using System.IO;

namespace ForenSync_Console_App.UI.MainMenuOptions
{
    public static class ChangePassword
    {
        private class FormField
        {
            public string Label { get; set; }
            public string Value { get; set; } = "";
            public bool IsSecret { get; set; } = false;
        }

        private static bool IsPasswordValid(string password)
        {
            if (password.Length < 8) return false;

            int score = 0;
            if (password.Any(char.IsUpper)) score++;
            if (password.Any(char.IsDigit)) score++;
            if (password.Any(c => !char.IsLetterOrDigit(c))) score++;

            return score >= 2;
        }

        public static void Render(string caseId, string userId, bool isNewCase)
        {
            var fields = new[]
            {
                new FormField { Label = "Enter Old Password", IsSecret = true },
                new FormField { Label = "Enter New Password", IsSecret = true },
                new FormField { Label = "Confirm New Password", IsSecret = true }
            };

            int fieldIndex = 0;

            while (true)
            {
                AnsiConsole.Clear();
                AsciiTitle.Render("Change Password");
                AnsiConsole.MarkupLine("[green]Use ↑↓ to navigate, [[F10]] to confirm, [[Esc]] to cancel.[/]");
                AnsiConsole.MarkupLine("[white]🔐 Please enter your credentials below.[/]\n");

                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    string highlight = i == fieldIndex ? "[blue bold]" : "";
                    string end = i == fieldIndex ? "[/]" : "";
                    string displayValue = field.IsSecret ? new string('*', field.Value.Length) : field.Value;
                    AnsiConsole.MarkupLine($"{highlight}{field.Label,-25}:{end} {displayValue}");
                }

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.UpArrow)
                    fieldIndex = (fieldIndex - 1 + fields.Length) % fields.Length;
                else if (key.Key == ConsoleKey.DownArrow)
                    fieldIndex = (fieldIndex + 1) % fields.Length;
                else if (key.Key == ConsoleKey.F10)
                    break;
                else if (key.Key == ConsoleKey.Backspace && fields[fieldIndex].Value.Length > 0)
                    fields[fieldIndex].Value = fields[fieldIndex].Value[..^1];
                else if (key.KeyChar >= 32 && key.KeyChar <= 126)
                    fields[fieldIndex].Value += key.KeyChar;
                else if (key.Key == ConsoleKey.Escape)
                {
                    MainMenu.Show(caseId, userId, isNewCase);
                    return;
                }
            }

            string oldPassword = fields[0].Value.Trim();
            string newPassword = fields[1].Value.Trim();
            string confirmPassword = fields[2].Value.Trim();

            AnsiConsole.Clear();
            AsciiTitle.Render("Confirm Password Change");

            if (newPassword != confirmPassword)
            {
                AnsiConsole.MarkupLine("[red]❌ New passwords do not match.[/]");
                AnsiConsole.MarkupLine("[grey]Press any key to return to Main Menu...[/]");
                Console.ReadKey(true);
                MainMenu.Show(caseId, userId, isNewCase);
                return;
            }

            if (newPassword == oldPassword)
            {
                AnsiConsole.MarkupLine("[red]❌ New password cannot be the same as old password.[/]");
                AnsiConsole.MarkupLine("[grey]Press any key to return to Main Menu...[/]");
                Console.ReadKey(true);
                MainMenu.Show(caseId, userId, isNewCase);
                return;
            }

            if (!IsPasswordValid(newPassword))
            {
                AnsiConsole.MarkupLine("[red]❌ Password must be at least 8 characters and include at least two of the following: uppercase letter, number, special character.[/]");
                AnsiConsole.MarkupLine("[grey]Tip: Strong passwords often include all three.[/]");
                AnsiConsole.MarkupLine("[grey]Press any key to return to Main Menu...[/]");
                Console.ReadKey(true);
                MainMenu.Show(caseId, userId, isNewCase);
                return;
            }

            string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            var verifyCommand = connection.CreateCommand();
            verifyCommand.CommandText = "SELECT COUNT(*) FROM users_tbl WHERE user_id = $id AND password = $current;";
            verifyCommand.Parameters.AddWithValue("$id", userId);
            verifyCommand.Parameters.AddWithValue("$current", oldPassword);

            long match = (long)verifyCommand.ExecuteScalar();
            if (match != 1)
            {
                AnsiConsole.MarkupLine("[red]❌ Old password is incorrect.[/]");
                AnsiConsole.MarkupLine("[grey]Press any key to return to Main Menu...[/]");
                Console.ReadKey(true);
                MainMenu.Show(caseId, userId, isNewCase);
                return;
            }

            var updateCommand = connection.CreateCommand();
            updateCommand.CommandText = "UPDATE users_tbl SET password = $new WHERE user_id = $id;";
            updateCommand.Parameters.AddWithValue("$new", newPassword);
            updateCommand.Parameters.AddWithValue("$id", userId);
            updateCommand.ExecuteNonQuery();

            AuditLogger.Log(userId, AuditAction.ChangePassword, "User changed their password successfully.");
            AnsiConsole.MarkupLine("[green]✅ Password successfully updated.[/]");
            AnsiConsole.MarkupLine("[grey]Press any key to return to Main Menu...[/]");
            Console.ReadKey(true);
            MainMenu.Show(caseId, userId, isNewCase);
        }
    }
}
