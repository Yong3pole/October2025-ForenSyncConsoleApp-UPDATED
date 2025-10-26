using Spectre.Console;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using ForenSync.Utils;

namespace ForenSync_Console_App.UI.MainMenuOptions.CaseOperations_SubMenu
{
    public static class AndroidAcquisition
    {
        private static CancellationTokenSource _cts;
        private static string _acquisitionPath = string.Empty;
        private static ProgressContext _progressContext;
        private static ProgressTask _mainProgressTask;

        public static void Run(string caseId, string userId, bool isNewCase)
        {
            Console.Clear();
            AsciiTitle.Render("Android Acquisition");

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Proceed with Android acquisition?")
                    .AddChoices("✅ Start", "🔙 Cancel"));

            if (confirm.Contains("Cancel")) return;

            // Log start of Android acquisition to audit trail
            LogAuditEvent(userId, "AndroidAcquisition", $"Started Android acquisition for case: {caseId}");

            _cts = new CancellationTokenSource();
            StartEscapeListener();

            string adbPath = Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe");
            if (!File.Exists(adbPath))
            {
                AnsiConsole.MarkupLine($"[red]❌ ADB not found at: {Escape(adbPath)}[/]");
                LogAuditEvent(userId, "AndroidAcquisition", $"ADB not found at: {adbPath}");
                return;
            }

            bool deviceFound = false;
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("green"))
                .Start("🔍 Scanning for Android device...", ctx =>
                {
                    string output = RunAdbCommand(adbPath, "devices", _cts.Token).Trim();
                    deviceFound = output.Split('\n').Any(line => line.Contains("\tdevice"));
                });

            if (!deviceFound)
            {
                KillAdbProcesses(); // Ensure ADB is killed even if no device found
                AnsiConsole.MarkupLine("[red]❌ No Android device detected. Make sure USB debugging is enabled and the device is connected.[/]");
                LogAuditEvent(userId, "AndroidAcquisition", "No Android device detected - acquisition failed");
                Thread.Sleep(2500);
                return;
            }

            string evidenceDir = Path.Combine(AppContext.BaseDirectory, "Cases", caseId, "Evidence");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _acquisitionPath = Path.Combine(evidenceDir, $"Android Acquisition {timestamp}");
            Directory.CreateDirectory(_acquisitionPath);
            string stagingPath = Path.Combine(_acquisitionPath, "staging");
            Directory.CreateDirectory(stagingPath);

            AnsiConsole.MarkupLine($"[green]📁 Creating acquisition folder:[/] {Escape(_acquisitionPath)}");

            try
            {
                string manufacturer = RunAdbCommand(adbPath, "shell getprop ro.product.manufacturer", _cts.Token).Trim();
                string model = RunAdbCommand(adbPath, "shell getprop ro.product.model", _cts.Token).Trim();
                string androidVersion = RunAdbCommand(adbPath, "shell getprop ro.build.version.release", _cts.Token).Trim();
                string serial = RunAdbCommand(adbPath, "get-serialno", _cts.Token).Trim();

                var table = new Table().Border(TableBorder.Rounded).Title("[bold cyan]Connected Android Device[/]");
                table.AddColumn("Property").AddColumn("Value");
                table.AddRow("Manufacturer", manufacturer);
                table.AddRow("Model", model);
                table.AddRow("Android Version", androidVersion);
                table.AddRow("Serial Number", serial);
                AnsiConsole.Write(table);

                // Log device information to audit trail
                LogAuditEvent(userId, "AndroidAcquisition",
                    $"Connected to Android device: {manufacturer} {model} (Android {androidVersion}, Serial: {serial})");

                bool cancelled = false;

                // Use Progress instead of Status for the main acquisition
                AnsiConsole.Progress()
                    .AutoClear(false)
                    .Columns(new ProgressColumn[]
                    {
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new RemainingTimeColumn(),
                        new SpinnerColumn()
                    })
                    .Start(ctx =>
                    {
                        _progressContext = ctx;

                        // Main progress task for overall acquisition
                        _mainProgressTask = ctx.AddTask("[green]Android Acquisition[/]", autoStart: true);
                        _mainProgressTask.MaxValue = 100;

                        try
                        {
                            // Phase 1: System data (20% of progress)
                            if (!_cts.IsCancellationRequested)
                                AcquireSystemDataWithProgress(adbPath, stagingPath);

                            if (_cts.IsCancellationRequested) throw new OperationCanceledException();
                            _mainProgressTask.Value = 20;

                            // Phase 2: User data (80% of progress)
                            if (!_cts.IsCancellationRequested)
                                AcquireUserDataWithProgress(adbPath, stagingPath);

                            _mainProgressTask.Value = 100;
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                            // Don't re-throw here, let the progress context complete gracefully
                        }
                    });

                if (cancelled) throw new OperationCanceledException();

                // Compression phase
                AnsiConsole.MarkupLine("[cyan]Compressing acquired data...[/]");
                string zipPath = Path.Combine(_acquisitionPath, "android_evidence.zip");
                string imgPath = Path.Combine(_acquisitionPath, "android_evidence.img");
                CreateZipAsImg(stagingPath, zipPath, imgPath);

                string hash = ComputeFileSHA256(imgPath);
                LogToDatabase(caseId, "android", "ADB Acquisition", imgPath, hash);

                // Log successful acquisition to audit trail
                LogAuditEvent(userId, "AndroidAcquisition",
                    $"Android acquisition completed successfully for case: {caseId}. Image: {Path.GetFileName(imgPath)}, Hash: {hash}");

                GenerateReadme(_acquisitionPath, hash);

                Directory.Delete(stagingPath, true);
                long fileSize = new FileInfo(imgPath).Length;

                AnsiConsole.MarkupLine($"[green]✅ Acquisition complete.[/]");
                AnsiConsole.MarkupLine($"Image saved: [italic]{Escape(imgPath)}[/]");
                AnsiConsole.MarkupLine($"SHA-256: [bold]{hash}[/]");
                AnsiConsole.MarkupLine($"Size: [bold]{FormatBytes(fileSize)}[/]");

                // Kill ADB processes after successful completion
                KillAdbProcesses();
                AnsiConsole.MarkupLine("[grey]ADB processes stopped.[/]");

                AnsiConsole.MarkupLine("[grey]Press any key to return to menu...[/]");
                Console.ReadKey(true);
            }
            catch (OperationCanceledException)
            {
                SafeDelete(_acquisitionPath);
                KillAdbProcesses(); // Ensure ADB is killed on cancellation

                // Log cancellation to audit trail
                LogAuditEvent(userId, "AndroidAcquisition", $"Android acquisition cancelled by user for case: {caseId}");

                AnsiConsole.MarkupLine("[yellow]⏹ Acquisition cancelled by user. Folder deleted.[/]");
                AnsiConsole.MarkupLine("[grey]ADB processes stopped.[/]");
                AnsiConsole.MarkupLine("[grey]No data was saved. Press any key to return...[/]");
                Console.ReadKey(true);
            }
            catch (Exception ex)
            {
                SafeDelete(_acquisitionPath);
                KillAdbProcesses(); // Ensure ADB is killed on error

                // Log error to audit trail
                LogAuditEvent(userId, "AndroidAcquisition",
                    $"Android acquisition failed for case: {caseId}. Error: {ex.Message}");

                AnsiConsole.MarkupLine($"[red]Error:[/] {Escape(ex.Message)}");
                AnsiConsole.MarkupLine("[grey]ADB processes stopped.[/]");
                AnsiConsole.MarkupLine("[grey]Press any key to return to menu...[/]");
                Console.ReadKey(true);
            }
            finally
            {
                // Final cleanup - ensure ADB is always killed
                KillAdbProcesses();
            }

            Console.ResetColor();
            Console.Clear();
        }

        // ==== Audit Trail Logging ====

        private static void LogAuditEvent(string userId, string action, string context)
        {
            try
            {
                string eventId = Guid.NewGuid().ToString();
                string createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                // Generate audit hash for integrity verification
                string auditHash = ComputeAuditHash(eventId, userId, action, context, createdAt);

                string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                string sql = @"
                    INSERT INTO audit_trail (event_id, user_id, action, created_at, context, audit_hash)
                    VALUES (@event_id, @user_id, @action, @created_at, @context, @audit_hash);";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@event_id", eventId);
                cmd.Parameters.AddWithValue("@user_id", userId);
                cmd.Parameters.AddWithValue("@action", action);
                cmd.Parameters.AddWithValue("@created_at", createdAt);
                cmd.Parameters.AddWithValue("@context", context);
                cmd.Parameters.AddWithValue("@audit_hash", auditHash);

                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // If audit logging fails, at least write to console
                AnsiConsole.MarkupLine($"[yellow]⚠️ Failed to log audit event: {Escape(ex.Message)}[/]");
            }
        }

        private static string ComputeAuditHash(string eventId, string userId, string action, string context, string createdAt)
        {
            // Create a canonical string for hash computation
            string canonicalString = $"{eventId}|{userId}|{action}|{context}|{createdAt}";

            using var sha256 = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(canonicalString);
            byte[] hashBytes = sha256.ComputeHash(bytes);

            // Return as hexadecimal string
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        // ==== Progress-enabled Acquisition Methods ====

        private static void AcquireSystemDataWithProgress(string adbPath, string stagingPath)
        {
            if (_cts.IsCancellationRequested) throw new OperationCanceledException();

            var systemTask = _progressContext.AddTask("[blue]Collecting system information[/]", autoStart: true);
            systemTask.MaxValue = 6; // Number of system commands we'll run

            // Check cancellation before each operation
            if (!_cts.IsCancellationRequested)
            {
                File.WriteAllText(Path.Combine(stagingPath, "device_info.txt"),
                    $"Manufacturer: {RunAdbCommand(adbPath, "shell getprop ro.product.manufacturer", _cts.Token).Trim()}\n" +
                    $"Model: {RunAdbCommand(adbPath, "shell getprop ro.product.model", _cts.Token).Trim()}\n" +
                    $"Android Version: {RunAdbCommand(adbPath, "shell getprop ro.build.version.release", _cts.Token).Trim()}\n" +
                    $"Serial: {RunAdbCommand(adbPath, "get-serialno", _cts.Token).Trim()}");
                systemTask.Increment(1);
            }

            if (!_cts.IsCancellationRequested)
            {
                File.WriteAllText(Path.Combine(stagingPath, "battery.txt"), RunAdbCommand(adbPath, "shell dumpsys battery", _cts.Token));
                systemTask.Increment(1);
            }

            if (!_cts.IsCancellationRequested)
            {
                File.WriteAllText(Path.Combine(stagingPath, "activity.txt"), RunAdbCommand(adbPath, "shell dumpsys activity", _cts.Token));
                systemTask.Increment(1);
            }

            if (!_cts.IsCancellationRequested)
            {
                File.WriteAllText(Path.Combine(stagingPath, "packages.txt"), RunAdbCommand(adbPath, "shell pm list packages", _cts.Token));
                systemTask.Increment(1);
            }

            if (!_cts.IsCancellationRequested)
            {
                File.WriteAllText(Path.Combine(stagingPath, "logcat.txt"), RunAdbCommand(adbPath, "shell logcat -d", _cts.Token));
                systemTask.Increment(1);
            }

            if (!_cts.IsCancellationRequested)
            {
                File.WriteAllText(Path.Combine(stagingPath, "df.txt"), RunAdbCommand(adbPath, "shell df /storage/emulated/0", _cts.Token));
                systemTask.Increment(1);
            }
        }

        private static void AcquireUserDataWithProgress(string adbPath, string stagingPath)
        {
            if (_cts.IsCancellationRequested) throw new OperationCanceledException();

            var folders = RunAdbCommand(adbPath, "shell ls -d /storage/emulated/0/*/", _cts.Token)
                .Split('\n')
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();

            if (folders.Count == 0) return;

            var folderTask = _progressContext.AddTask("[green]Acquiring user data[/]", autoStart: true);
            folderTask.MaxValue = folders.Count;

            int completedFolders = 0;

            foreach (var folder in folders)
            {
                if (_cts.IsCancellationRequested) throw new OperationCanceledException();

                string folderName = Path.GetFileName(folder.TrimEnd('/'));
                string targetPath = Path.Combine(stagingPath, folderName);

                // Update task description to show current folder
                folderTask.Description = $"[green]Acquiring: {Escape(folderName)}[/]";

                try
                {
                    // Run ADB pull with progress monitoring
                    RunAdbPullWithProgress(adbPath, folder, targetPath, folderName);
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    // Continue with next folder even if one fails
                    AnsiConsole.MarkupLine($"[yellow]⚠️ Failed to acquire folder: {Escape(folderName)}[/]");
                }

                if (_cts.IsCancellationRequested) throw new OperationCanceledException();

                completedFolders++;
                folderTask.Value = completedFolders;

                // Update main progress task (system data was 20%, user data is 80%)
                _mainProgressTask.Value = 20 + (completedFolders * 80 / folders.Count);
            }

            folderTask.Description = "[green]User data acquisition complete[/]";
        }

        private static void RunAdbPullWithProgress(string adbPath, string sourceFolder, string targetPath, string folderName)
        {
            var pullTask = _progressContext.AddTask($"[blue]Pulling {Escape(folderName)}[/]", autoStart: true);
            pullTask.MaxValue = 100;

            // Since ADB pull doesn't give progress, we'll simulate it with a timer
            var stopwatch = Stopwatch.StartNew();
            var estimatedTime = TimeSpan.FromSeconds(30); // Estimate 30 seconds per folder

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = $"pull --sync \"{sourceFolder}\" \"{targetPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Monitor the process and update progress
            Task.Run(() =>
            {
                while (!process.HasExited && !_cts.IsCancellationRequested)
                {
                    if (stopwatch.Elapsed > estimatedTime)
                    {
                        pullTask.Value = 100; // If we exceed estimate, just show complete
                    }
                    else
                    {
                        // Linear progress based on time elapsed
                        double progress = (stopwatch.Elapsed.TotalSeconds / estimatedTime.TotalSeconds) * 100;
                        pullTask.Value = Math.Min(95, progress); // Cap at 95% until complete
                    }
                    Thread.Sleep(200);
                }
            });

            // Wait for process completion with cancellation support
            try
            {
                while (!process.HasExited)
                {
                    if (_cts.IsCancellationRequested)
                    {
                        process.Kill();
                        throw new OperationCanceledException();
                    }
                    Thread.Sleep(100);
                }
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill();
                throw;
            }

            // Ensure progress shows 100% when complete
            pullTask.Value = 100;

            if (Directory.Exists(targetPath))
            {
                int fileCount = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories).Length;
                if (fileCount > 0)
                {
                    AnsiConsole.MarkupLine($"[green]✅ Acquired:[/] {Escape(folderName)} [grey]({fileCount} files)[/]");
                }
            }
        }

        // ==== Existing Helper Methods ====

        private static void CreateZipAsImg(string stagingPath, string zipPath, string imgPath)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (File.Exists(imgPath)) File.Delete(imgPath);

            ZipFile.CreateFromDirectory(stagingPath, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
            File.Move(zipPath, imgPath);
        }

        private static void GenerateReadme(string folderPath, string hash)
        {
            string readmePath = Path.Combine(folderPath, "README.txt");
            File.WriteAllText(readmePath,
                "This file was acquired as android_evidence.img\n" +
                "You may rename it to .zip to view contents.\n" +
                "Do not modify or re-zip the contents.\n" +
                $"Original SHA-256 hash: {hash}\n");
        }

        private static void StartEscapeListener()
        {
            Task.Run(() =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                    {
                        _cts.Cancel();
                        AnsiConsole.MarkupLine("\n[yellow]🛑 Cancellation requested. Stopping acquisition and ADB processes...[/]");
                        break;
                    }
                    Thread.Sleep(100);
                }
            });
        }

        // ==== Database Logging and Utilities ====

        private static void LogToDatabase(string caseId, string type, string tool, string outputPath, string hash)
        {
            string createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Get the path starting from "Cases" folder
            string casesPath = Path.Combine(AppContext.BaseDirectory, "Cases");
            string outputPathRelative = outputPath.Replace(casesPath, "Cases").TrimStart(Path.DirectorySeparatorChar);

            string canonicalEntry = $"{caseId}|{type}|{tool}|{outputPathRelative}|{hash}|{createdAt}";
            string entryHash = ComputeSha256(canonicalEntry);
            string acquisitionId = Guid.NewGuid().ToString();

            string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
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
            cmd.Parameters.AddWithValue("@output_path", outputPathRelative); // Use relative path here
            cmd.Parameters.AddWithValue("@hash", hash);
            cmd.Parameters.AddWithValue("@created_at", createdAt);
            cmd.Parameters.AddWithValue("@entry_hash", entryHash);

            cmd.ExecuteNonQuery();
        }

        private static string ComputeFileSHA256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
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
            double gb = bytes / 1024d / 1024d / 1024d;
            return $"{gb:N2} GB";
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch { }
        }

        private static void KillAdbProcesses()
        {
            try
            {
                // Kill adb.exe processes
                foreach (var proc in Process.GetProcessesByName("adb"))
                {
                    try
                    {
                        if (!proc.HasExited)
                        {
                            proc.Kill();
                            proc.WaitForExit(5000); // Wait up to 5 seconds for process to exit
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[yellow]⚠️ Warning: Could not kill ADB process {proc.Id}: {Escape(ex.Message)}[/]");
                    }
                }

                // Also kill any adb subprocesses that might be running
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        if (!proc.HasExited &&
                            (proc.ProcessName.ToLower().Contains("adb") ||
                             (proc.MainModule != null && proc.MainModule.FileName.ToLower().Contains("adb"))))
                        {
                            proc.Kill();
                            proc.WaitForExit(3000);
                        }
                    }
                    catch
                    {
                        // Ignore errors killing subprocesses
                    }
                }

                // Small delay to ensure processes are fully terminated
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠️ Warning during ADB cleanup: {Escape(ex.Message)}[/]");
            }
        }

        private static string RunAdbCommand(string adbPath, string arguments, CancellationToken token)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.Start();

            var outputTask = Task.Run(() =>
            {
                while (!process.StandardOutput.EndOfStream && !token.IsCancellationRequested)
                {
                    string line = process.StandardOutput.ReadLine();
                    if (line != null) outputBuilder.AppendLine(line);
                }
            });

            var errorTask = Task.Run(() =>
            {
                while (!process.StandardError.EndOfStream && !token.IsCancellationRequested)
                {
                    string line = process.StandardError.ReadLine();
                    if (line != null) errorBuilder.AppendLine(line);
                }
            });

            var waitTask = process.WaitForExitAsync(token);

            try
            {
                Task.WaitAll(new[] { outputTask, errorTask, waitTask }, token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                throw;
            }

            string error = errorBuilder.ToString();
            if (!string.IsNullOrWhiteSpace(error) &&
                !error.Contains("daemon not running") &&
                !error.Contains("daemon started successfully"))
            {
                AnsiConsole.MarkupLine($"[red]{Escape(error)}");
            }

            return outputBuilder.ToString();
        }

        private static string Escape(string input)
        {
            return input.Replace("[", "\\[").Replace("]", "\\]").Replace("\\", "\\\\");
        }
    }
}