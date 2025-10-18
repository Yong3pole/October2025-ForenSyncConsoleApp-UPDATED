using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Security.Cryptography;
using Spectre.Console;
using System.Management;
using Microsoft.Data.Sqlite;
using ForenSync.Utils;

namespace ForenSync_Console_App.UI.MainMenuOptions.DeviceInfo_SubMenu
{
    public static class ViewSystemInfo
    {
        public static void Show(string currentCasePath, string userId)
        {
            AnsiConsole.Clear();
            Console.CursorVisible = false;
            AsciiTitle.Render("System Information");

            var table = new Table()
                .RoundedBorder()
                .Title("[bold yellow]System Overview[/]")
                .AddColumn("[blue]Property[/]")
                .AddColumn("[green]Value[/]");

            var sb = new StringBuilder();

            try
            {
                foreach (var sys in new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem").Get())
                {
                    AddRow(table, sb, "Machine Name", sys["Name"]);
                    AddRow(table, sb, "Manufacturer", sys["Manufacturer"]);
                    AddRow(table, sb, "Model", sys["Model"]);
                    AddRow(table, sb, "System Type", sys["SystemType"]);
                    AddRow(table, sb, "Total Physical Memory", FormatBytes(sys["TotalPhysicalMemory"]?.ToString()));
                }

                foreach (var os in new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem").Get())
                {
                    AddRow(table, sb, "OS Name", os["Caption"]);
                    AddRow(table, sb, "Version", os["Version"]);
                    AddRow(table, sb, "Architecture", os["OSArchitecture"]);
                    AddRow(table, sb, "Install Date", FormatDate(os["InstallDate"]?.ToString()));
                    AddRow(table, sb, "Last Boot Time", FormatDate(os["LastBootUpTime"]?.ToString()));
                    AddRow(table, sb, "System Uptime", FormatUptime(os["LastBootUpTime"]?.ToString()));
                }

                foreach (var cpu in new ManagementObjectSearcher("SELECT * FROM Win32_Processor").Get())
                {
                    AddRow(table, sb, "CPU Name", cpu["Name"]);
                    AddRow(table, sb, "Cores", cpu["NumberOfCores"]);
                    AddRow(table, sb, "Logical Processors", cpu["NumberOfLogicalProcessors"]);
                    AddRow(table, sb, "Architecture", cpu["Architecture"]);
                }

                foreach (var bios in new ManagementObjectSearcher("SELECT * FROM Win32_BIOS").Get())
                {
                    var biosVersion = string.Join(", ", (string[])bios["BIOSVersion"] ?? new string[] { "N/A" });
                    AddRow(table, sb, "BIOS Version", biosVersion);
                    AddRow(table, sb, "BIOS Vendor", bios["Manufacturer"]);
                    AddRow(table, sb, "BIOS Release Date", FormatDate(bios["ReleaseDate"]?.ToString()));
                }

                foreach (var board in new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard").Get())
                {
                    AddRow(table, sb, "Motherboard Manufacturer", board["Manufacturer"]);
                    AddRow(table, sb, "Product", board["Product"]);
                }

                AnsiConsole.Write(new Panel(table)
                    .Header("[bold green]System Info Snapshot[/]")
                    .Border(BoxBorder.Double)
                    .Padding(1, 1)
                    .BorderStyle(new Style(Color.Blue)));
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]❌ Failed to retrieve system info: {ex.Message}[/]");
            }

            AnsiConsole.MarkupLine("\n[green][[S]][/]: Save snapshot   [green][[Esc]][/]: Return to Device Info");

            var key = EvidenceWriter.TryReadKey();
            if (key?.Key == ConsoleKey.S)
            {
                if (string.IsNullOrWhiteSpace(currentCasePath))
                {
                    AnsiConsole.MarkupLine("\n[red]⚠️ No active case detected. This session is not linked to any case.[/]");
                    AnsiConsole.MarkupLine("[grey]Press [bold]Enter[/] to return to Device Info.[/]");
                    Console.ReadKey(true);
                    DeviceInfo.Show(null, userId, false);
                    return;
                }

                string caseId = Path.GetFileName(currentCasePath.TrimEnd(Path.DirectorySeparatorChar));
                string evidenceDir = Path.Combine(AppContext.BaseDirectory, "Cases", caseId, "Evidence");
                Directory.CreateDirectory(evidenceDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = $"system_info_snapshot_{timestamp}.txt";
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
                string canonicalEntry = $"{caseId}|snapshot|ForenSync | ViewSystemInfo|{outputPathRelative}|{hash}|{createdAt}";
                string entryHash = ComputeSha256(canonicalEntry);

                string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                // Generate GUID for acquisition_id
                string acquisitionId = Guid.NewGuid().ToString();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO acquisition_log (acquisition_id, case_id, type, tool, output_path, hash, created_at, entry_hash)
                    VALUES (@acquisition_id, @case_id, @type, @tool, @output_path, @hash, @created_at, @entry_hash);";
                command.Parameters.AddWithValue("@acquisition_id", acquisitionId);
                command.Parameters.AddWithValue("@case_id", caseId);
                command.Parameters.AddWithValue("@type", "snapshot");
                command.Parameters.AddWithValue("@tool", "ForenSync | ViewSystemInfo");
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

        private static void AddRow(Table table, StringBuilder sb, string label, object value)
        {
            string val = value?.ToString() ?? "N/A";
            table.AddRow(label, val);
            sb.AppendLine($"{label}: {val}");
        }

        private static string FormatBytes(string rawBytes)
        {
            if (ulong.TryParse(rawBytes, out ulong bytes))
            {
                double gb = bytes / (1024.0 * 1024 * 1024);
                return $"{gb:F2} GB";
            }
            return "N/A";
        }

        private static string FormatDate(string wmiDate)
        {
            if (string.IsNullOrWhiteSpace(wmiDate)) return "N/A";
            try
            {
                return ManagementDateTimeConverter.ToDateTime(wmiDate).ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch { return "N/A"; }
        }

        private static string FormatUptime(string bootTime)
        {
            if (string.IsNullOrWhiteSpace(bootTime)) return "N/A";
            try
            {
                var boot = ManagementDateTimeConverter.ToDateTime(bootTime);
                var uptime = DateTime.Now - boot;
                return $"{(int)uptime.TotalHours} hrs {uptime.Minutes} mins";
            }
            catch { return "N/A"; }
        }
    }
}
