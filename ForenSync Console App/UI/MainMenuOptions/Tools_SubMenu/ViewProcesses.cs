using ForenSync.Utils;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Security.Cryptography;

namespace ForenSync_Console_App.UI.MainMenuOptions.Tools_SubMenu
{
    public static class ViewProcesses
    {
        public static void Show(string currentCasePath, string userId)
        {
            AnsiConsole.Clear();
            Console.CursorVisible = false;
            AsciiTitle.Render("ForenSync");

            var sb = new StringBuilder();
            var processes = Process.GetProcesses().OrderBy(p => p.ProcessName).ToList();

            // Bar Chart
            var topProcesses = processes.OrderByDescending(p => p.WorkingSet64).Take(10)
                .Select(p => new BarChartItem($"{p.ProcessName} ({p.Id})", p.WorkingSet64 / (1024 * 1024), Color.Green)).ToList();

            var chart = new BarChart().Width(80).Label("[bold underline green]Top Memory Consumers (MB)[/]").CenterLabel().AddItems(topProcesses);
            AnsiConsole.Write(chart);
            sb.AppendLine("Top Memory Consumers (MB)");
            foreach (var item in topProcesses)
                sb.AppendLine($"{item.Label} - {item.Value} MB");

            // Tree View
            var root = new Tree("[bold yellow]Running Processes[/]").Guide(TreeGuide.BoldLine);
            foreach (var proc in processes.Take(20))
            {
                var node = root.AddNode($"[green]{proc.ProcessName}[/] [grey](PID: {proc.Id})[/]");
                node.AddNode($"Memory: {(proc.WorkingSet64 / (1024 * 1024)):N0} MB");
                sb.AppendLine($"{proc.ProcessName} (PID: {proc.Id}) - Memory: {(proc.WorkingSet64 / (1024 * 1024)):N0} MB");
            }
            AnsiConsole.Write(root);

            // Table
            var table = new Table().RoundedBorder().AddColumn("PID").AddColumn("Name").AddColumn("Memory (MB)");
            foreach (var proc in processes.Take(20))
            {
                string memory = "N/A";
                try { memory = (proc.WorkingSet64 / (1024 * 1024)).ToString("N0"); } catch { }
                table.AddRow(proc.Id.ToString(), proc.ProcessName, memory);
                sb.AppendLine($"{proc.Id} | {proc.ProcessName} | {memory} MB");
            }

            AnsiConsole.Write(new Panel(table).Header("[bold green]Running Processes[/]").Border(BoxBorder.Double).Padding(1, 1).BorderStyle(new Style(Color.Blue)));

            AnsiConsole.MarkupLine("\n[green][[S]][/]: Save snapshot   [green][[Esc]][/]: Return to Tools");

            var key = EvidenceWriter.TryReadKey();

            if (key?.Key == ConsoleKey.S)
            {
                if (string.IsNullOrWhiteSpace(currentCasePath))
                {
                    AnsiConsole.MarkupLine("\n[red]⚠️ No active case detected. This session is not linked to any case.[/]");
                    AnsiConsole.MarkupLine("[grey]Press [bold]Enter[/] to return to Tools.[/]");
                    Console.ReadKey(true);
                    Tools.Show(null, userId, false);
                    return;
                }

                string caseId = Path.GetFileName(currentCasePath.TrimEnd(Path.DirectorySeparatorChar));
                string evidenceDir = Path.Combine(AppContext.BaseDirectory, "Cases", caseId, "Evidence");
                Directory.CreateDirectory(evidenceDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = $"running_process_snapshot_{timestamp}.txt";
                string fullPath = Path.Combine(evidenceDir, filename);
                File.WriteAllText(fullPath, sb.ToString());

                string hash;
                using (var sha256 = SHA256.Create())
                using (var stream = File.OpenRead(fullPath))
                {
                    byte[] hashBytes = sha256.ComputeHash(stream);
                    hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }

                string outputPathRelative = Path.Combine("Cases", caseId, "Evidence", filename);
                string createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string canonicalEntry = $"{caseId}|snapshot|ForenSync | ViewProcesses|{outputPathRelative}|{hash}|{createdAt}";
                string entryHash = ComputeSha256(canonicalEntry);

                // Generate GUID for acquisition_id
                string acquisitionId = Guid.NewGuid().ToString();

                string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO acquisition_log (acquisition_id, case_id, type, tool, output_path, hash, created_at, entry_hash)
                    VALUES (@acquisition_id, @case_id, @type, @tool, @output_path, @hash, @created_at, @entry_hash);";
                command.Parameters.AddWithValue("@acquisition_id", acquisitionId);
                command.Parameters.AddWithValue("@case_id", caseId);
                command.Parameters.AddWithValue("@type", "snapshot");
                command.Parameters.AddWithValue("@tool", "ForenSync | ViewProcesses");
                command.Parameters.AddWithValue("@output_path", outputPathRelative);
                command.Parameters.AddWithValue("@hash", hash);
                command.Parameters.AddWithValue("@created_at", createdAt);
                command.Parameters.AddWithValue("@entry_hash", entryHash);
                command.ExecuteNonQuery();

                AuditLogger.Log(userId, AuditAction.ExportedSnapshot, $"Saved: {filename}, hash: {hash}");

                AnsiConsole.MarkupLine("\n[green]✅ Snapshot saved successfully![/]");
                AnsiConsole.MarkupLine($"[grey]Saved to:[/] [bold]{fullPath}[/]");
                AnsiConsole.MarkupLine($"[grey]SHA-256:[/] [blue]{hash}[/]");
            }

        }
        private static string ComputeSha256(string input)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

    }

    public static class ViewProcessesWMI
    {
        public static void Show(string currentCasePath, string userId)
        {
            AnsiConsole.Clear();
            Console.CursorVisible = false;
            AsciiTitle.Render("ForenSync");

            var sb = new StringBuilder();
            var table = new Table().RoundedBorder().AddColumn("PID").AddColumn("Name").AddColumn("Command Line").AddColumn("Parent PID");

            try
            {
                var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, CommandLine, ParentProcessId FROM Win32_Process");

                foreach (ManagementObject obj in searcher.Get())
                {
                    string pid = obj["ProcessId"]?.ToString() ?? "N/A";
                    string name = obj["Name"]?.ToString() ?? "N/A";
                    string rawCmd = obj["CommandLine"]?.ToString();
                    string cmd = string.IsNullOrWhiteSpace(rawCmd) ? "N/A" : Markup.Escape(rawCmd);
                    string parent = obj["ParentProcessId"]?.ToString() ?? "N/A";

                    table.AddRow(pid, name, cmd, parent);
                    sb.AppendLine($"{pid} | {name} | {rawCmd ?? "N/A"} | Parent: {parent}");
                }

                AnsiConsole.Write(new Panel(table).Header("[bold green]WMI Process Snapshot[/]").Border(BoxBorder.Double).Padding(1, 1).BorderStyle(new Style(Color.Blue)));
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]❌ Failed to query WMI: {ex.Message}[/]");
            }

            AnsiConsole.MarkupLine("\n[green][[S]][/]: Save snapshot   [green][[Esc]][/]: Return to Tools");

            var key = EvidenceWriter.TryReadKey();

            if (key?.Key == ConsoleKey.S)
            {
                if (string.IsNullOrWhiteSpace(currentCasePath))
                {
                    AnsiConsole.MarkupLine("\n[red]⚠️ No active case detected. This session is not linked to any case.[/]");
                    AnsiConsole.MarkupLine("[grey]Press [bold]Enter[/] to return to Tools.[/]");
                    Console.ReadKey(true);
                    Tools.Show(null, userId, false);
                    return;
                }

                string caseId = Path.GetFileName(currentCasePath.TrimEnd(Path.DirectorySeparatorChar));
                string evidenceDir = Path.Combine(AppContext.BaseDirectory, "Cases", caseId, "Evidence");
                Directory.CreateDirectory(evidenceDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = $"running_process_snapshot_{timestamp}.txt";
                string fullPath = Path.Combine(evidenceDir, filename);
                File.WriteAllText(fullPath, sb.ToString());

                string hash;
                using (var sha256 = SHA256.Create())
                using (var stream = File.OpenRead(fullPath))
                {
                    byte[] hashBytes = sha256.ComputeHash(stream);
                    hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }

                string outputPathRelative = Path.Combine("Cases", caseId, "Evidence", filename);
                string createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string canonicalEntry = $"{caseId}|snapshot|ForenSync | ViewProcessWMI|{outputPathRelative}|{hash}|{createdAt}";
                string entryHash = ComputeSha256(canonicalEntry);

                // Generate GUID for acquisition_id
                string acquisitionId = Guid.NewGuid().ToString();

                string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO acquisition_log (acquisition_id, case_id, type, tool, output_path, hash, created_at, entry_hash)
                    VALUES (@acquisition_id, @case_id, @type, @tool, @output_path, @hash, @created_at, @entry_hash);";
                command.Parameters.AddWithValue("@acquisition_id", acquisitionId);
                command.Parameters.AddWithValue("@case_id", caseId);
                command.Parameters.AddWithValue("@type", "snapshot");
                command.Parameters.AddWithValue("@tool", "ForenSync | ViewProcessWMI");
                command.Parameters.AddWithValue("@output_path", outputPathRelative);
                command.Parameters.AddWithValue("@hash", hash);
                command.Parameters.AddWithValue("@created_at", createdAt);
                command.Parameters.AddWithValue("@entry_hash", entryHash);
                command.ExecuteNonQuery();

                AuditLogger.Log(userId, AuditAction.ExportedSnapshot, $"Saved: {filename}, hash: {hash}");

                AnsiConsole.MarkupLine("\n[green]✅ Snapshot saved successfully![/]");
                AnsiConsole.MarkupLine($"[grey]Saved to:[/] [bold]{fullPath}[/]");
                AnsiConsole.MarkupLine($"[grey]SHA-256:[/] [blue]{hash}[/]");
            }

        }
        private static string ComputeSha256(string input)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }


    }

}
