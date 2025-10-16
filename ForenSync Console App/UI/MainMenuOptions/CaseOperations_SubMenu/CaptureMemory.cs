using ForenSync.Utils;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ForenSync_Console_App.UI.MainMenuOptions.CaseOperations_SubMenu
{
    public static class CaptureMemory
    {
        // Create a dictionary to store speed for each task
        private static readonly Dictionary<ProgressTask, double> _taskSpeeds = new Dictionary<ProgressTask, double>();
        private static readonly object _lockObject = new object();

        public static void Run(string caseId, string userId, bool isNewCase)
        {
            Console.Clear();
            AsciiTitle.Render("Memory Capture");

            var confirmCapture = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold green]Do you want to capture volatile memory for this device session?[/]")
                    .PageSize(3)
                    .AddChoices(new[]
                    {
            "✅ Yes, proceed with memory capture",
            "❌ No, return to Case Operations"
                    }));

            if (confirmCapture == "❌ No, return to Case Operations")
            {
                CaseOperations.Show(caseId, userId, isNewCase);
                return;
            }

            // Check for admin privileges
            bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);

            if (!isAdmin)
            {
                Console.WriteLine("❌ This operation requires Administrator privileges.");
                return;
            }

            string basePath = AppContext.BaseDirectory;
            string winpmemPath = Path.Combine(basePath, "winpmem.exe");

            if (!File.Exists(winpmemPath))
            {
                Console.WriteLine("❌ winpmem.exe not found in base directory.");
                return;
            }

            // Get total memory size and check disk space
            long totalMemorySize = GetTotalMemorySize();
            if (totalMemorySize == 0)
            {
                AnsiConsole.MarkupLine("[yellow]⚠️ Could not determine total memory size, using fallback progress...[/]");
                totalMemorySize = 8L * 1024 * 1024 * 1024; // Fallback: 8GB
            }

            string outputDir = Path.Combine(basePath, "Cases", caseId, "Evidence", "Captured Memory");
            Directory.CreateDirectory(outputDir); // Ensure directory exists for drive info

            // Check available disk space
            var driveInfo = new DriveInfo(Path.GetPathRoot(outputDir) ?? basePath);
            long availableSpace = driveInfo.AvailableFreeSpace;

            AnsiConsole.MarkupLine($"[grey]Total memory to capture:[/] {FormatBytes(totalMemorySize)}");
            AnsiConsole.MarkupLine($"[grey]Available disk space:[/] {FormatBytes(availableSpace)}");
            AnsiConsole.MarkupLine($"[grey]Output directory:[/] {outputDir}");

            // Check if there's enough disk space (add 10% buffer for safety)
            long requiredSpace = (long)(totalMemorySize * 1.1); // 10% buffer for file overhead

            if (availableSpace < requiredSpace)
            {
                AnsiConsole.MarkupLine("\n[red]❌ Insufficient disk space![/]");
                AnsiConsole.MarkupLine($"[red]Required: {FormatBytes(requiredSpace)} | Available: {FormatBytes(availableSpace)}[/]");
                AnsiConsole.MarkupLine("[yellow]Please free up disk space or choose a different location.[/]");

                AnsiConsole.MarkupLine("\n[grey]Press any key to return to Case Operations...[/]");
                Console.ReadKey(true);
                CaseOperations.Show(caseId, userId, isNewCase);
                return;
            }

            AnsiConsole.MarkupLine($"[green]✓ Sufficient disk space available ({FormatBytes(availableSpace - requiredSpace)} free after capture)[/]\n");

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
            string filename = $"memdump_{timestamp}.raw";
            string fullOutputPath = Path.Combine(outputDir, filename);

            AnsiConsole.MarkupLine($"[grey]Output file:[/] {filename}\n");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = winpmemPath,
                    Arguments = $"acquire --progress --nosparse \"{filename}\"",
                    WorkingDirectory = outputDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            string output = "", error = "";
            var cts = new CancellationTokenSource();

            // Start the memory capture process
            process.Start();

            // Monitor progress by watching file size growth
            var progressTask = Task.Run(() =>
            {
                var progress = AnsiConsole.Progress()
                    .AutoClear(false)
                    .Columns(new ProgressColumn[]
                    {
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new SpinnerColumn(),
                        new MyRemainingTimeColumn(),
                        new MyElapsedTimeColumn(),
                        new TransferSpeedColumn()
                    });

                progress.Start(ctx =>
                {
                    var task = ctx.AddTask("[green]Capturing memory[/]");
                    task.MaxValue = totalMemorySize;

                    long lastFileSize = 0;
                    DateTime lastUpdate = DateTime.Now;

                    while (!process.HasExited && !cts.Token.IsCancellationRequested)
                    {
                        if (File.Exists(fullOutputPath))
                        {
                            try
                            {
                                var fileInfo = new FileInfo(fullOutputPath);
                                long currentSize = fileInfo.Length;

                                // Update progress
                                task.Value = Math.Min(currentSize, totalMemorySize);

                                // Calculate transfer speed
                                var now = DateTime.Now;
                                var timeDiff = (now - lastUpdate).TotalSeconds;
                                if (timeDiff > 1) // Update speed every second
                                {
                                    var sizeDiff = currentSize - lastFileSize;
                                    var speed = sizeDiff / timeDiff;

                                    // Store speed in our dictionary
                                    lock (_lockObject)
                                    {
                                        _taskSpeeds[task] = speed;
                                    }

                                    lastFileSize = currentSize;
                                    lastUpdate = now;
                                }
                            }
                            catch
                            {
                                // File might be locked, ignore errors
                            }
                        }

                        // Check if user pressed Escape to cancel
                        if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                        {
                            cts.Cancel();
                            AnsiConsole.MarkupLine("\n[red]❌ Memory capture cancelled by user.[/]");
                            try { process.Kill(); } catch { }
                            break;
                        }

                        Thread.Sleep(500); // Check every 500ms
                    }

                    // Ensure we show 100% when complete
                    if (!cts.Token.IsCancellationRequested && process.HasExited && process.ExitCode == 0)
                    {
                        task.Value = totalMemorySize;
                    }

                    // Clean up speed tracking
                    lock (_lockObject)
                    {
                        _taskSpeeds.Remove(task);
                    }
                });
            });

            // Read output in background
            var outputTask = Task.Run(() =>
            {
                output = process.StandardOutput.ReadToEnd();
            });

            var errorTask = Task.Run(() =>
            {
                error = process.StandardError.ReadToEnd();
            });

            // Wait for process to complete
            process.WaitForExit();

            // Wait a bit for progress to update
            Thread.Sleep(1000);
            cts.Cancel();

            // Wait for progress display to complete
            try { progressTask.Wait(2000); } catch { }

            if (cts.Token.IsCancellationRequested && !process.HasExited)
            {
                // Clean up partial file if cancelled
                if (File.Exists(fullOutputPath))
                {
                    try { File.Delete(fullOutputPath); } catch { }
                }
                AuditLogger.Log(userId, AuditAction.MemCapture, $"Memory capture cancelled for case: {caseId}");
                return;
            }

            if (!File.Exists(fullOutputPath))
            {
                AnsiConsole.MarkupLine("[red]❌ Memory capture failed - no output file created.[/]");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    AnsiConsole.MarkupLine("[yellow]WinPmem error:[/]");
                    Console.WriteLine(error);
                }
                return;
            }

            // Hash calculation with progress
            string hash = "";
            var fileInfo = new FileInfo(fullOutputPath);
            long fileSize = fileInfo.Length;

            AnsiConsole.MarkupLine($"\n[grey]Computing SHA-256 hash for {FormatBytes(fileSize)}...[/]");

            var hashProgress = AnsiConsole.Progress()
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

            hashProgress.Start(ctx =>
            {
                var task = ctx.AddTask("[yellow]Computing hash[/]");
                task.MaxValue = fileSize;

                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(fullOutputPath);

                byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
                long totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                    totalBytesRead += bytesRead;
                    task.Increment(bytesRead);
                }

                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                hash = BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();
            });

            // Database logging
            string dbPath = Path.Combine(basePath, "forensync.db");
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            string outputPathRelative = Path.Combine("Cases", caseId, "Evidence", "Captured Memory", filename);
            string createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            string canonicalEntry = $"{caseId}|memory capture|ForenSync | WinPmem|{outputPathRelative}|{hash}|{createdAt}";
            string entryHash = ComputeSha256(canonicalEntry);

            // Generate GUID for acquisition_id
            string acquisitionId = Guid.NewGuid().ToString();

            using var command = connection.CreateCommand();
            command.CommandText = @"
            INSERT INTO acquisition_log (acquisition_id, case_id, type, tool, output_path, hash, created_at, entry_hash)
            VALUES (@acquisition_id, @case_id, @type, @tool, @output_path, @hash, @created_at, @entry_hash)";
            command.Parameters.AddWithValue("@acquisition_id", acquisitionId);
            command.Parameters.AddWithValue("@case_id", caseId);
            command.Parameters.AddWithValue("@type", "memory capture");
            command.Parameters.AddWithValue("@tool", "ForenSync | WinPmem");
            command.Parameters.AddWithValue("@output_path", outputPathRelative);
            command.Parameters.AddWithValue("@hash", hash);
            command.Parameters.AddWithValue("@created_at", createdAt);
            command.Parameters.AddWithValue("@entry_hash", entryHash);

            command.ExecuteNonQuery();

            // Log to audit trail
            AuditLogger.Log(userId, AuditAction.MemCapture, $"Captured volatile memory for case: {caseId} — output: {filename}, size: {FormatBytes(fileSize)}, hash: {hash}");

            AnsiConsole.MarkupLine("\n[green]✅ Memory capture completed successfully![/]");
            AnsiConsole.MarkupLine($"[grey]Saved to:[/] [bold]{fullOutputPath}[/]");
            AnsiConsole.MarkupLine($"[grey]File size:[/] [blue]{FormatBytes(fileSize)}[/]");
            AnsiConsole.MarkupLine($"[grey]SHA-256:[/] [blue]{hash}[/]");

            if (!string.IsNullOrWhiteSpace(error))
            {
                AnsiConsole.MarkupLine("[yellow]⚠️ WinPmem reported errors:[/]");
                Console.WriteLine(error);
            }

            AnsiConsole.MarkupLine("\n[bold]Press any key to return to Case Operations...[/]");
            Console.ReadKey(true);

            CaseOperations.Show(caseId, userId, isNewCase);
        }

        private static long GetTotalMemorySize()
        {
            try
            {
                // Method 1: Use Windows Management to get total physical memory
                using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                using var results = searcher.Get();
                foreach (var result in results)
                {
                    if (result["TotalPhysicalMemory"] != null)
                    {
                        return Convert.ToInt64(result["TotalPhysicalMemory"]);
                    }
                }
            }
            catch
            {
                // Fallback if WMI fails
            }

            try
            {
                // Method 2: Use Environment working set (less accurate but works)
                return Environment.WorkingSet * 4; // Rough estimate
            }
            catch
            {
                return 0;
            }
        }

        private static string ComputeSha256(string input)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n2} {suffixes[counter]}";
        }

        // Custom progress columns
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

        private class TransferSpeedColumn : ProgressColumn
        {
            public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
            {
                lock (_lockObject)
                {
                    if (_taskSpeeds.TryGetValue(task, out double speed) && speed > 0)
                    {
                        return new Text($"Speed: {FormatSpeed(speed)}/s", new Style(Color.Blue));
                    }
                }
                return new Text("Speed: --/s", new Style(Color.Grey));
            }

            private static string FormatSpeed(double bytesPerSecond)
            {
                string[] suffixes = { "B", "KB", "MB", "GB" };
                int counter = 0;
                decimal number = (decimal)bytesPerSecond;
                while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
                {
                    number /= 1024;
                    counter++;
                }
                return $"{number:n1} {suffixes[counter]}";
            }
        }
    }
}