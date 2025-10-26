using ForenSync.Utils;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace ForenSync_Console_App.UI.MainMenuOptions.Tools_SubMenu
{
    public static class ViewNetworkConnections
    {
        private static string EscapeMarkup(string input)
        {
            return input.Replace("[", "[[").Replace("]", "]]");
        }

        public static void Show(string currentCasePath, string userId)
        {
            AnsiConsole.Clear();
            Console.CursorVisible = false;
            AsciiTitle.Render("Network Connections");

            TcpConnectionInformation[] connections;

            try
            {
                connections = IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpConnections();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]❌ Failed to retrieve network connections: {ex.Message}[/]");
                return;
            }

            // Bar Chart: Connection states
            var stateGroups = connections
                .GroupBy(c => c.State)
                .Select(g => new BarChartItem(g.Key.ToString(), g.Count(), Color.Yellow))
                .ToList();

            AnsiConsole.Write(new BarChart()
                .Width(60)
                .Label("[green bold underline]TCP Connection States[/]")
                .CenterLabel()
                .AddItems(stateGroups));

            // Tree View: Top remote IPs
            var ipGroups = connections
                .Where(c => !c.RemoteEndPoint.Address.Equals(IPAddress.Any))
                .GroupBy(c => c.RemoteEndPoint.Address.ToString())
                .OrderByDescending(g => g.Count())
                .Take(5);

            var root = new Tree("[bold yellow]Top Remote IPs[/]").Guide(TreeGuide.BoldLine);

            foreach (var group in ipGroups)
            {
                var node = root.AddNode($"[green]{group.Key}[/] [grey]({group.Count()} connections)[/]");
                foreach (var conn in group.Take(3))
                {
                    //node.AddNode($"Local: {conn.LocalEndPoint} → State: {conn.State}");
                    // FIX 
                    var childText = $"Local: {conn.LocalEndPoint} → State: {conn.State}";
                    var childNode = new TreeNode(new Markup(EscapeMarkup(childText)));
                    node.AddNode(childNode);


                }
            }

            AnsiConsole.Write(root);

            // Table: Full connection list
            var table = new Table()
                .RoundedBorder()
                .AddColumn("Local")
                .AddColumn("Remote")
                .AddColumn("State");

            var sb = new StringBuilder();

            foreach (var conn in connections)
            {
                string local = conn.LocalEndPoint.ToString().Replace("[", "[[").Replace("]", "]]");
                string remote = conn.RemoteEndPoint.Address.Equals(IPAddress.Any)
                    ? "N/A"
                    : $"{conn.RemoteEndPoint.Address}:{conn.RemoteEndPoint.Port}";
                string state = conn.State.ToString();
                table.AddRow(local, remote, state);
                sb.AppendLine($"{local} | {remote} | {state}");
            }

            AnsiConsole.Write(new Panel(table)
                .Header("[bold green]TCP Connection Snapshot[/]")
                .Border(BoxBorder.Double)
                .Padding(1, 1)
                .BorderStyle(new Style(Color.Blue)));

            // Snapshot logic
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
                string filename = $"network_connections_snapshot_{timestamp}.txt";
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
                string canonicalEntry = $"{caseId}|snapshot|ForenSync | ViewNetworkConnections|{outputPathRelative}|{hash}|{createdAt}";
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
                command.Parameters.AddWithValue("@tool", "ForenSync | ViewNetworkConnections");
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
