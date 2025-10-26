using ForenSync.Utils;
using ForenSync_Console_App.Data;
using ForenSync_Console_App.UI;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;



namespace ForenSync_Console_App.CaseManagement
{
    public static class CaseSession
    {
        private class CaseRow
        {
            public string CaseId;
            public string Department;
            public string UserId;
            public string Date;
            public string Notes;
        }

        // Starts a new case session
        public static void StartNewCase(string userId)
        {
            AnsiConsole.Clear();
            Console.CursorVisible = false;
            AsciiTitle.Render("ForenSync");
            AnsiConsole.MarkupLine("[bold blue]🆕 Starting New Case[/]");
            AnsiConsole.MarkupLine("\n[green]Press [[Esc]] to cancel and return to login.[/]\n");

            string department = PromptWithEsc("Enter Jurisdiction/Department:");
            Console.CursorVisible = false;
            if (department == null)
            {
                LoginPage.ShowSessionPrompt(userId); // ✅ Return to session options
                return;
            }

            string notes = PromptWithEsc("Enter Notes (optional):");
            Console.CursorVisible = false;
            if (notes == null)
            {
                LoginPage.ShowSessionPrompt(userId); // ✅ Return to session options
                return;
            }

            string caseId = GenerateCaseId();

            string basePath = AppContext.BaseDirectory;
            string dbPath = Path.Combine(basePath, "forensync.db");

            string fullName = "Unknown User";
            string role = "Unknown Role";

            try
            {
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT firstname, lastname, role
                    FROM users_tbl
                    WHERE user_id = $userId";
                command.Parameters.AddWithValue("$userId", userId);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    string first = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    string last = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    fullName = $"{first} {last}".Trim();
                    role = reader.IsDBNull(2) ? "Unknown Role" : reader.GetString(2);
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]⚠️ User not found in database.[/]");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]❌ Error fetching user info: {ex.Message}[/]");
            }

            // Display case summary
            Console.WriteLine("\n────────────────────────────────────────────");
            Console.WriteLine("📋 Case Summary:");
            Console.WriteLine("────────────────────────────────────────────");
            Console.WriteLine($"Case ID         : {caseId}");
            Console.WriteLine($"Department      : {department}");
            Console.WriteLine($"Notes           : {(string.IsNullOrWhiteSpace(notes) ? "None" : notes)}");
            Console.WriteLine($"User            : {fullName}");
            Console.WriteLine($"Role            : {role}");
            Console.WriteLine("────────────────────────────────────────────\n");

            string casePath = CreateCaseFolder(caseId, department, notes, fullName, role);
            SaveToDatabase(caseId, department, notes, userId, casePath);

            AuditLogger.Log(userId, AuditAction.CreateCase, $"Created case: {caseId} in {department} with notes: {(string.IsNullOrWhiteSpace(notes) ? "None" : notes)}");

            Console.WriteLine("✅ Case folder created. Proceeding to main menu...\n");
            Thread.Sleep(3000);
            MainMenu.Show(caseId, userId, true);
        }


        // Generates a unique case ID based on timestamp
        private static string GenerateCaseId()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return $"CASE_{timestamp}";
        }

        // Creates case folder and summary file
        private static string CreateCaseFolder(string caseId, string department, string notes, string fullName, string role)
        {
            string basePath = Path.Combine(AppContext.BaseDirectory, "Cases");
            string casePath = Path.Combine(basePath, caseId);
            string evidencePath = Path.Combine(casePath, "Evidence");

            try
            {
                Directory.CreateDirectory(evidencePath);

                string summaryPath = Path.Combine(casePath, "summary.txt");
                string summaryContent = $@"
                    Case ID       : {caseId}
                    Department    : {department}
                    Notes         : {(string.IsNullOrWhiteSpace(notes) ? "None" : notes)}
                    User          : {fullName}
                    Role          : {role}
                    Created At    : {DateTime.Now}
                ";

                File.WriteAllText(summaryPath, summaryContent.Trim());
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Error creating case folder or summary: {ex.Message}");
                Console.ResetColor();
            }

            return casePath;
        }

        // Saves case details to database
        private static void SaveToDatabase(string caseId, string department, string notes, string userId, string casePath)
        {
            string basePath = AppContext.BaseDirectory;
            string dbPath = Path.Combine(basePath, "forensync.db");

            // Convert casePath to relative path
            string casePathRelative = casePath
                .Replace(basePath, "")
                .TrimStart(Path.DirectorySeparatorChar);



            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO case_logs (case_id, department, user_id, notes, date, case_path)
                VALUES ($id, $department, $userId, $notes, $date, $path);";

            command.Parameters.AddWithValue("$id", caseId);
            command.Parameters.AddWithValue("$department", department);
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$notes", string.IsNullOrWhiteSpace(notes) ? "None" : notes);
            command.Parameters.AddWithValue("$date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("$path", casePathRelative);

            command.ExecuteNonQuery();
        }

        // Displays and selects an existing case
            public static string SelectExistingCase(string userId)
            {
                AnsiConsole.Clear();
                AsciiTitle.Render("ForenSync");

                string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
                string casesRoot = Path.Combine(AppContext.BaseDirectory, "Cases");

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT c.case_id, c.department, c.user_id, c.notes, c.date
                    FROM case_logs c
                    ORDER BY c.date DESC;";

                using var reader = command.ExecuteReader();
                var caseRows = new List<CaseRow>();

                while (reader.Read())
                {
                    string caseId = reader.GetString(0);
                    string folderPath = Path.Combine(casesRoot, caseId);
                    if (!Directory.Exists(folderPath)) continue;

                    string department = reader.GetString(1);
                    string caseOwnerId = reader.GetString(2);
                    string notes = reader.GetString(3).Replace("\n", " ").Replace("\r", "").Replace("\t", " ");
                    string rawDate = reader.GetString(4);

                    DateTime parsedDate;
                    string formattedDate = DateTime.TryParse(rawDate, out parsedDate)
                        ? parsedDate.ToString("MMM dd, yyyy")
                        : rawDate;

                    caseRows.Add(new CaseRow
                    {
                        CaseId = caseId,
                        Department = department,
                        UserId = caseOwnerId,
                        Date = formattedDate,
                        Notes = notes
                    });
                }

                if (caseRows.Count == 0)
                {
                    AnsiConsole.MarkupLine("[red]⚠️ No valid case folders found.[/]");
                    return null;
                }

                int selectedIndex = 0;
                Console.CursorVisible = false;

                while (true)
                {
                    AnsiConsole.Clear();
                    Console.CursorVisible = false;
                    AsciiTitle.Render("ForenSync");

                    var table = new Table()
                        .Border(TableBorder.Rounded)
                        .Title("[bold yellow underline]Available Cases[/]")
                        .AddColumn("Title")
                        .AddColumn("Department")
                        .AddColumn("User ID")
                        .AddColumn("Date Created")
                        .AddColumn("Notes");

                    for (int i = 0; i < caseRows.Count; i++)
                    {
                        var row = caseRows[i];
                        bool isSelected = i == selectedIndex;
                        string style = isSelected ? "[bold blue]" : "";
                        string end = isSelected ? "[/]" : "";

                        table.AddRow(
                            $"{style}{row.CaseId}{end}",
                            $"{style}{row.Department}{end}",
                            $"{style}{row.UserId}{end}",
                            $"{style}{row.Date}{end}",
                            $"{style}{row.Notes}{end}"
                        );
                    }

                    AnsiConsole.Write(table);
                    AnsiConsole.MarkupLine("\n[green]Use ↑↓ to navigate, [[Enter]] to resume, [[Esc]] to cancel.[/]");

                    var key = Console.ReadKey(true).Key;
                    switch (key)
                    {
                        case ConsoleKey.UpArrow:
                            selectedIndex = (selectedIndex - 1 + caseRows.Count) % caseRows.Count;
                            break;
                        case ConsoleKey.DownArrow:
                            selectedIndex = (selectedIndex + 1) % caseRows.Count;
                            break;
                        case ConsoleKey.Enter:
                            Console.CursorVisible = true;
                            string selectedCaseId = caseRows[selectedIndex].CaseId;
                            string selectedDepartment = caseRows[selectedIndex].Department;
                            string selectedNotes = caseRows[selectedIndex].Notes;

                            // ✅ Log the case access in audit trail
                            AuditLogger.Log(userId, AuditAction.AccessCase, $"Accessed case: {selectedCaseId} in {selectedDepartment} with notes: {(string.IsNullOrWhiteSpace(selectedNotes) ? "None" : selectedNotes)}");

                            MainMenu.Show(selectedCaseId, userId, true); // true = show summary even for existing case
                            return null;
                        case ConsoleKey.Escape:
                            return null;
                    }
                }
            }

            private static string PromptWithEsc(string label)
            {
            Console.CursorVisible = false;
            AnsiConsole.Markup($"[white]{label}[/] ");
                var buffer = new StringBuilder();

                while (true)
                {
                Console.CursorVisible = false;

                var key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Escape)
                {
                    AnsiConsole.Clear();
                    return null; // ✅ Just cancel input
                }
                else if (key.Key == ConsoleKey.Enter)
                    {
                        Console.WriteLine();
                        return buffer.ToString().Trim();
                    }
                    else if (key.Key == ConsoleKey.Backspace && buffer.Length > 0)
                    {
                        buffer.Length--;
                        Console.Write("\b \b");
                    }
                    else if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Append(key.KeyChar);
                        Console.Write(key.KeyChar);
                    }
                }
            }

    }
}
