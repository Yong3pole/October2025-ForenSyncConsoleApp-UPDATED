using ForenSync.Utils;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace ForenSync_Console_App.UI.MainMenuOptions.CaseOperations_SubMenu
{
    public static class DriveImager
    {
        public static void Show(string caseId, string userId, bool isNewCase)
        {
            Console.Clear();
            Console.CursorVisible = false;
            AsciiTitle.Render("Drive Imaging");

            var (selectedDrive, driveSize) = PromptDriveSelection();
            if (selectedDrive == null) return;

            var outputName = AnsiConsole.Ask<string>("💾 Enter output image filename (e.g., [grey]volume_image.dd[/]):");
            var casePath = Path.Combine("Cases", caseId, "Evidence", "Cloned Drive");
            Directory.CreateDirectory(casePath);
            var outputPath = Path.Combine(casePath, outputName);

            var rawPath = $"\\\\.\\{selectedDrive.Substring(0, 2)}"; // e.g., "C:"

            ImageVolume(rawPath, outputPath, caseId, userId, driveSize);
        }

        private static (string driveLabel, long driveSize) PromptDriveSelection()
        {
            var drives = DriveInfo.GetDrives();
            var choices = new List<(string label, DriveInfo drive)>();

            foreach (var drive in drives)
            {
                if (!drive.IsReady) continue;
                string label = $"{drive.Name} ({drive.DriveFormat}) - {FormatBytes(drive.TotalFreeSpace)} free of {FormatBytes(drive.TotalSize)}";
                choices.Add((label, drive));
            }

            if (choices.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]❌ No ready drives found.[/]");
                return (null, 0);
            }

            // Render prompt manually
            var console = AnsiConsole.Console;
            var selected = choices[0].label;
            int index = 0;

            while (true)
            {
                console.Clear();
                Console.CursorVisible = false;
                AsciiTitle.Render("Drive Imaging");
                AnsiConsole.MarkupLine("[green]Use ↑↓ to navigate, Enter to select, Esc to cancel.[/]\n");

                for (int i = 0; i < choices.Count; i++)
                {
                    if (i == index)
                        AnsiConsole.MarkupLine($"[blue]> {choices[i].label}[/]");
                    else
                        AnsiConsole.MarkupLine($"  {choices[i].label}");
                }

                var key = Console.ReadKey(true).Key;

                switch (key)
                {
                    case ConsoleKey.UpArrow:
                        index = (index - 1 + choices.Count) % choices.Count;
                        break;
                    case ConsoleKey.DownArrow:
                        index = (index + 1) % choices.Count;
                        break;
                    case ConsoleKey.Enter:
                        return (choices[index].label, choices[index].drive.TotalSize);
                    case ConsoleKey.Escape:
                        return (null, 0);
                }
            }
        }

        private static async Task ImageVolume(string volumePath, string outputPath, string caseId, string userId, long driveSize)
        {
            if (!IsAdmin())
            {
                AnsiConsole.MarkupLine("[red]🔒 Imaging requires administrator privileges.[/]");
                return;
            }

            AnsiConsole.MarkupLine($"🛠  Imaging [yellow]{volumePath}[/] to [green]{outputPath}[/]...");
            AnsiConsole.MarkupLine($"[grey]Drive Size: {FormatBytes(driveSize)}[/]");
            AnsiConsole.MarkupLine("[grey]Press Esc anytime to cancel imaging...[/]");

            string hash = "";
            var cts = new CancellationTokenSource();

            // Monitor for Esc key in background
            Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                    {
                        cts.Cancel();
                        AnsiConsole.MarkupLine("\n[red]❌ Imaging cancelled by user.[/]");
                        break;
                    }
                    Thread.Sleep(100);
                }
            });

            try
            {
                using var src = new FileStream(volumePath, FileMode.Open, FileAccess.Read);
                using var dst = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                using var hasher = SHA256.Create();

                byte[] buffer = new byte[1024 * 1024]; // 1MB
                long totalBytes = 0;

                var progress = AnsiConsole.Progress()
                    .AutoClear(true)
                    .Columns(new ProgressColumn[]
                    {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn(),
                new MyRemainingTimeColumn(),
                new MyElapsedTimeColumn()
                    });

                progress.Start(ctx =>
                {
                    var task = ctx.AddTask("Imaging volume", autoStart: true);
                    task.MaxValue = driveSize;

                    while (!cts.IsCancellationRequested)
                    {
                        int bytesRead = src.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;

                        dst.Write(buffer, 0, bytesRead);
                        hasher.TransformBlock(buffer, 0, bytesRead, null, 0);
                        totalBytes += bytesRead;
                        task.Increment(bytesRead);
                    }
                });

                if (cts.IsCancellationRequested)
                {
                    dst.Close();
                    File.Delete(outputPath);
                    AuditLogger.Log(userId, AuditAction.Image, $"Imaging cancelled for volume: {volumePath}");
                    return;
                }

                hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                hash = BitConverter.ToString(hasher.Hash).Replace("-", "").ToLowerInvariant();

                LogToDatabase(caseId, "drive", ".NET FileStream", outputPath, hash);
                AuditLogger.Log(userId, AuditAction.Image, $"Imaged volume: {volumePath} → {outputPath} | SHA256: {hash} | Drive Size: {FormatBytes(driveSize)}");

                AnsiConsole.MarkupLine("\n[green]✅ Imaging complete.[/]");

                // NEW: Auto-extract MFT after imaging
                AnsiConsole.MarkupLine("[blue]🔍 Auto-extracting $MFT for anomaly analysis...[/]");
                await ExtractAndAnalyzeMFT(outputPath, caseId, userId);

                AnsiConsole.MarkupLine("\n[grey]Press any key to return to Case Operations menu...[/]");
                Console.ReadKey(true);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"⚠️ [red]Error:[/] {ex.Message}");
            }
        }

        private static async Task<string> FindPartitionOffset(string imagePath, string sleuthkitBin)
        {
            var process = new Process();
            process.StartInfo.FileName = Path.Combine(sleuthkitBin, "mmls.exe");
            process.StartInfo.Arguments = $"\"{imagePath}\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            // Parse output to find NTFS partition offset
            foreach (string line in output.Split('\n'))
            {
                if (line.Contains("NTFS"))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                        return parts[2]; // Offset is typically in the third column
                }
            }

            // If no partition table found, assume raw volume (offset 0)
            return "0";
        }

        private static async Task<string> FindMFTInode(string imagePath, string sleuthkitBin, string offset)
        {
            var process = new Process();
            process.StartInfo.FileName = Path.Combine(sleuthkitBin, "fls.exe");
            process.StartInfo.Arguments = $"-o {offset} \"{imagePath}\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            // Parse output to find $MFT entry
            foreach (string line in output.Split('\n'))
            {
                if (line.Contains("$MFT"))
                {
                    var parts = line.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        var inodePart = parts[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        return inodePart[^1]; // Last part before colon is the inode
                    }
                }
            }

            throw new Exception("$MFT not found in image");
        }

        private static async Task<string> ExtractMFT(string imagePath, string sleuthkitBin, string offset, string inode)
        {
            string outputPath = Path.Combine(Path.GetDirectoryName(imagePath),
                                           $"{Path.GetFileNameWithoutExtension(imagePath)}_mft.raw");

            var process = new Process();
            process.StartInfo.FileName = Path.Combine(sleuthkitBin, "icat.exe");
            process.StartInfo.Arguments = $"-o {offset} \"{imagePath}\" {inode}";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            using var outputFile = File.Create(outputPath);
            process.Start();

            await process.StandardOutput.BaseStream.CopyToAsync(outputFile);
            process.WaitForExit();

            return outputPath;
        }

        private static async Task<string> ParseMFTWithMFTECmd(string mftPath, string mftecmdDir, string caseId)
        {
            string outputDir = Path.GetDirectoryName(mftPath);

            var process = new Process();
            process.StartInfo.FileName = Path.Combine(mftecmdDir, "MFTECmd.exe");
            process.StartInfo.Arguments = $"-f \"{mftPath}\" --csv \"{outputDir}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            await process.WaitForExitAsync();

            // Give it a moment and retry finding the CSV
            await Task.Delay(500);

            var csvFiles = Directory.GetFiles(outputDir, "*_MFTECmd_*.csv");
            return csvFiles.FirstOrDefault() ?? "CSV not found (check MFTECmd output)";
        }

        private static async Task ExtractAndAnalyzeMFT(string imagePath, string caseId, string userId)
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;

                // Use the correct folder name
                string sleuthkitBin = Path.Combine(baseDir, "sleuthkit-4.14.0-win32", "bin");
                string mftecmdDir = Path.Combine(baseDir, "MFTECmd");

                AnsiConsole.MarkupLine($"[grey]Checking tools at: {sleuthkitBin}[/]");

                // Verify mmls.exe exists
                string mmlsPath = Path.Combine(sleuthkitBin, "mmls.exe");
                if (!File.Exists(mmlsPath))
                {
                    throw new FileNotFoundException($"mmls.exe not found at: {mmlsPath}");
                }

                AnsiConsole.MarkupLine($"[green]✅ Found mmls.exe[/]");

                // Step 1: Find partition offset
                string offset = await FindPartitionOffset(imagePath, sleuthkitBin);

                // Step 2: Find $MFT inode
                string inode = await FindMFTInode(imagePath, sleuthkitBin, offset);

                // Step 3: Extract $MFT
                string mftPath = await ExtractMFT(imagePath, sleuthkitBin, offset, inode);

                // Step 4: Parse with MFTECmd
                string csvPath = await ParseMFTWithMFTECmd(mftPath, mftecmdDir, caseId);

                AnsiConsole.MarkupLine($"[green]✅ $MFT analysis complete: {csvPath}[/]");
                // AuditLogger.Log(userId, AuditAction.Analyze, $"MFT extracted and analyzed for case: {caseId}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠️ MFT extraction failed: {ex.Message}[/]");
            }
        }


        // Custom progress columns with clear labels
        private class MyRemainingTimeColumn : ProgressColumn
        {
            public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
            {
                var remaining = task.RemainingTime;
                return remaining == TimeSpan.MaxValue ?
                    new Text("Remaining: --:--:--", new Style(Color.Grey)) :
                    new Text($"Remaining: {remaining:hh\\:mm\\:ss}", new Style(Color.Yellow));
            }
        }

        private class MyElapsedTimeColumn : ProgressColumn
        {
            public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
            {
                var elapsed = task.ElapsedTime;
                return new Text($"Elapsed: {elapsed:hh\\:mm\\:ss}", new Style(Color.Green));
            }
        }

        private static void LogToDatabase(string caseId, string type, string tool, string outputPath, string hash)
        {
            string createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string outputPathRelative = outputPath.Replace(AppContext.BaseDirectory, "").TrimStart(Path.DirectorySeparatorChar);

            string canonicalEntry = $"{caseId}|{type}|{tool}|{outputPathRelative}|{hash}|{createdAt}";
            string entryHash = ComputeSha256(canonicalEntry);

            // Generate GUID for acquisition_id
            string acquisitionId = Guid.NewGuid().ToString();

            string basePath = AppContext.BaseDirectory;
            string dbPath = Path.Combine(basePath, "forensync.db");
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            string sql = @"
            INSERT INTO acquisition_log (acquisition_id, case_id, type, tool, output_path, hash, created_at, entry_hash)
            VALUES (@acquisition_id, @case_id, @type, @tool, @output_path, @hash, @created_at, @entry_hash);";

            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@acquisition_id", acquisitionId);
            cmd.Parameters.AddWithValue("@case_id", caseId);
            cmd.Parameters.AddWithValue("@type", type);
            cmd.Parameters.AddWithValue("@tool", tool);
            cmd.Parameters.AddWithValue("@output_path", outputPath);
            cmd.Parameters.AddWithValue("@hash", hash);
            cmd.Parameters.AddWithValue("@created_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@entry_hash", entryHash);

            cmd.ExecuteNonQuery();
        }

        private static string FormatBytes(long bytes)
        {
            double gb = bytes / 1024d / 1024d / 1024d;
            return $"{gb:N2} GB";
        }

        private static bool IsAdmin()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
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