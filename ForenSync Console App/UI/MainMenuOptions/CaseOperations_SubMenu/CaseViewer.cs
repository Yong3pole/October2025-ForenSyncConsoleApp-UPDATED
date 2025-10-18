using ForenSync.Utils;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;

namespace ForenSync_Console_App.UI.MainMenuOptions.CaseOperations_SubMenu
{
    public static class CaseViewer
    {
        public static void Show(string caseId, string userId, bool isNewCase)
        {
            AnsiConsole.Clear();
            AsciiTitle.Render("Case Viewer");

            var caseRows = LoadValidCases();
            if (caseRows.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]⚠️ No valid case folders found.[/]");
                AnsiConsole.MarkupLine("\n[grey]Press any key to return to Case Operations...[/]");
                Console.ReadKey(true);
                CaseOperations.Show(null, userId, false);
                return;
            }

            int selectedIndex = 0;
            Console.CursorVisible = false;

            while (true)
            {
                AnsiConsole.Clear();
                AsciiTitle.Render("Case Viewer");
                RenderCaseTable(caseRows, selectedIndex);

                AnsiConsole.MarkupLine("\n[green]Use ↑↓ to navigate, [[Enter]] to view, [[Esc]] to return.[/]");
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
                        var selected = caseRows[selectedIndex];
                        AnsiConsole.Clear();
                        AsciiTitle.Render($"Case: {selected.CaseId}");
                        RenderCaseDetails(selected);

                        bool hasActiveCase = !string.IsNullOrWhiteSpace(caseId);
                        string prompt = hasActiveCase
                            ? $"Do you wish to exit current case: {caseId} and load {selected.CaseId}?"
                            : $"Do you wish to load existing case: {selected.CaseId}?";

                        Console.WriteLine($"\n{prompt}");
                        Console.WriteLine("Press [1] = YES   [2] = NO");

                        var confirm = Console.ReadKey(true).Key;
                        if (confirm != ConsoleKey.D1 && confirm != ConsoleKey.NumPad1) break;

                        string action = hasActiveCase
                            ? $"Exited case: {caseId}, loaded case: {selected.CaseId}"
                            : $"Loaded existing case: {selected.CaseId}";

                        AuditLogger.Log(userId, AuditAction.AccessCase, action);
                        MainMenu.Show(selected.CaseId, userId, true);
                        return;

                    case ConsoleKey.Escape:
                        AnsiConsole.Clear();
                        AsciiTitle.Render("Returning to Case Operations");
                        AnsiConsole.MarkupLine("[grey]Exiting Case Viewer...[/]");
                        Thread.Sleep(500);
                        CaseOperations.Show(caseId, userId, isNewCase);
                        return;
                }
            }
        }

        private static List<CaseRow> LoadValidCases()
        {
            string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
            string casesRoot = Path.Combine(AppContext.BaseDirectory, "Cases");

            var caseRows = new List<CaseRow>();
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT c.case_id, c.department, c.user_id, c.notes, c.date
                FROM case_logs c
                ORDER BY c.date DESC;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string rowCaseId = reader.GetString(0);
                string folderPath = Path.Combine(casesRoot, rowCaseId);
                if (!Directory.Exists(folderPath)) continue;

                string department = reader.GetString(1);
                string ownerId = reader.GetString(2);
                string notes = reader.GetString(3).Replace("\n", " ").Replace("\r", "").Replace("\t", " ");
                string rawDate = reader.GetString(4);

                string formattedDate = DateTime.TryParse(rawDate, out var parsedDate)
                    ? parsedDate.ToString("MMM dd, yyyy")
                    : rawDate;

                caseRows.Add(new CaseRow
                {
                    CaseId = rowCaseId,
                    Department = department,
                    UserId = ownerId,
                    Notes = notes,
                    Date = formattedDate
                });
            }

            return caseRows;
        }

        private static void RenderCaseTable(List<CaseRow> caseRows, int selectedIndex)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold yellow underline]Available Cases[/]")
                .AddColumn("🆔 Case ID")
                .AddColumn("🏢 Department")
                .AddColumn("👤 User ID")
                .AddColumn("📅 Date")
                .AddColumn("📝 Notes");

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
        }

        private static void RenderCaseDetails(CaseRow selected)
        {
            var panel = new Panel($@"
                [bold]Case ID:[/] {selected.CaseId}
                [bold]User ID:[/] {selected.UserId}
                [bold]Department:[/] {selected.Department}
                [bold]Date:[/] {selected.Date}
                [bold]Notes:[/] {selected.Notes}")
                .Header("[bold yellow]Case Details[/]")
                .Border(BoxBorder.Double)
                .Padding(1, 1)
                .BorderStyle(new Style(Color.Blue));

            AnsiConsole.Write(panel);
        }

        private class CaseRow
        {
            public string CaseId { get; set; }
            public string Department { get; set; }
            public string UserId { get; set; }
            public string Notes { get; set; }
            public string Date { get; set; }
        }
    }
}
