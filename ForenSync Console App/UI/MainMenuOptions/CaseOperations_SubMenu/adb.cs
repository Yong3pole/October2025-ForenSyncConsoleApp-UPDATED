using Spectre.Console;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;

namespace ForenSync_Console_App.UI.MainMenuOptions.CaseOperations_SubMenu
{
    public static class AndroidAcquisition
    {
        private static CancellationTokenSource _cts;
        private static string _acquisitionPath = string.Empty;
        private static Process _currentAdbProcess;

        public static void Run(string caseId, string userId, bool isNewCase)
        {
            Console.Clear();
            AsciiTitle.Render("Android Acquisition");

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Proceed with Android acquisition?")
                    .AddChoices("✅ Start", "🔙 Cancel"));

            if (confirm.Contains("Cancel"))
            {
                CaseOperations.Show(caseId, userId, isNewCase);
                return;
            }

            _cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                HandleCancellation();
            };

            string adbPath = Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe");
            if (!File.Exists(adbPath))
            {
                AnsiConsole.MarkupLine($"[red]❌ ADB not found at: {Escape(adbPath)}[/]");
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
                KillAdbProcesses();
                AnsiConsole.MarkupLine("[red]❌ No Android device detected. Make sure USB debugging is enabled and the device is connected.[/]");
                Thread.Sleep(2500);
                Run(caseId, userId, isNewCase);
                return;
            }

            string evidenceDir = Path.Combine(AppContext.BaseDirectory, "Cases", caseId, "Evidence");
            _acquisitionPath = Path.Combine(evidenceDir, "Android Acquisition");
            Directory.CreateDirectory(_acquisitionPath);
            string stagingPath = Path.Combine(_acquisitionPath, "staging");
            Directory.CreateDirectory(stagingPath);

            AnsiConsole.MarkupLine("[cyan]Acquiring device data...[/]");
            AnsiConsole.MarkupLine("[grey](Press Ctrl+C to cancel)[/]");

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

                // Check for cancellation before starting acquisition
                _cts.Token.ThrowIfCancellationRequested();

                AnsiConsole.Status()
                    .Spinner(Spinner.Known.Line)
                    .SpinnerStyle(Style.Parse("green"))
                    .Start("📦 Acquiring data from device...", ctx =>
                    {
                        AcquireSystemData(adbPath, stagingPath, _cts.Token);
                        AcquireUserData(adbPath, stagingPath, _cts.Token);
                    });

                // Check for cancellation before creating final archive
                _cts.Token.ThrowIfCancellationRequested();

                string zipPath = Path.Combine(_acquisitionPath, "android_evidence.zip");
                string imgPath = Path.Combine(_acquisitionPath, "android_evidence.img");
                CreateZipAsImg(stagingPath, zipPath, imgPath);

                string hash = ComputeFileSHA256(imgPath);
                LogToDatabase(caseId, "android", "ADB Acquisition", imgPath, hash);

                Directory.Delete(stagingPath, true);
                long fileSize = new FileInfo(imgPath).Length;

                AnsiConsole.MarkupLine("[green]✅ Acquisition complete.[/]");
                AnsiConsole.MarkupLine($"Image saved: [italic]{Escape(imgPath)}[/]");
                AnsiConsole.MarkupLine($"SHA-256: [bold]{hash}[/]");
                AnsiConsole.MarkupLine($"Size: [bold]{FormatBytes(fileSize)}[/]");
                AnsiConsole.MarkupLine("[grey]Press any key to return to Android acquisition menu...[/]");
                Console.ReadKey(true);
            }
            catch (OperationCanceledException)
            {
                HandleCancellation();
                return;
            }
            catch (Exception ex)
            {
                SafeDelete(_acquisitionPath);
                KillAdbProcesses();
                AnsiConsole.MarkupLine($"[red]Error:[/] {Escape(ex.Message)}");
                AnsiConsole.MarkupLine("[grey]Press any key to return to Android acquisition menu...[/]");
                Console.ReadKey(true);
            }

            KillAdbProcesses();
            Console.ResetColor();
            Console.Clear();
            CaseOperations.Show(caseId, userId, isNewCase);
        }

        private static void HandleCancellation()
        {
            _cts?.Cancel();

            // Kill current ADB process if running
            try { _currentAdbProcess?.Kill(true); } catch { }

            // Kill all ADB processes
            KillAdbProcesses();

            // Delete acquisition folder
            SafeDelete(_acquisitionPath);

            AnsiConsole.MarkupLine("[yellow]⏹ Cancelling acquisition...[/]");
            AnsiConsole.MarkupLine("[yellow]🗑️ Deleting acquired data...[/]");
            AnsiConsole.MarkupLine("[yellow]🛑 Stopping ADB processes...[/]");
            Thread.Sleep(1000); // Brief pause to show messages
            AnsiConsole.MarkupLine("[red]❌ Acquisition cancelled by user.[/]");
            AnsiConsole.MarkupLine("[grey]Press any key to return to Android acquisition menu...[/]");
            Console.ReadKey(true);

            Console.ResetColor();
            Console.Clear();
        }

        private static void AcquireSystemData(string adbPath, string stagingPath, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            File.WriteAllText(Path.Combine(stagingPath, "device_info.txt"),
                $"Manufacturer: {RunAdbCommand(adbPath, "shell getprop ro.product.manufacturer", token).Trim()}\n" +
                $"Model: {RunAdbCommand(adbPath, "shell getprop ro.product.model", token).Trim()}\n" +
                $"Android Version: {RunAdbCommand(adbPath, "shell getprop ro.build.version.release", token).Trim()}\n" +
                $"Serial: {RunAdbCommand(adbPath, "get-serialno", token).Trim()}");

            token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(stagingPath, "battery.txt"), RunAdbCommand(adbPath, "shell dumpsys battery", token));

            token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(stagingPath, "activity.txt"), RunAdbCommand(adbPath, "shell dumpsys activity", token));

            token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(stagingPath, "packages.txt"), RunAdbCommand(adbPath, "shell pm list packages", token));

            token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(stagingPath, "logcat.txt"), RunAdbCommand(adbPath, "shell logcat -d", token));

            token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(stagingPath, "df.txt"), RunAdbCommand(adbPath, "shell df /storage/emulated/0", token));
        }

        private static void AcquireUserData(string adbPath, string stagingPath, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var folders = RunAdbCommand(adbPath, "shell ls -d /storage/emulated/0/*/", token)
                .Split('\n')
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();

            foreach (var folder in folders)
            {
                token.ThrowIfCancellationRequested();

                string folderName = Path.GetFileName(folder.TrimEnd('/'));
                string targetPath = Path.Combine(stagingPath, folderName);

                AnsiConsole.MarkupLine($"[blue]Checking folder:[/] {Escape(folderName)}");

                try
                {
                    // First, check if the folder has any files before pulling
                    string fileCheck = RunAdbCommand(adbPath, $"shell find \"{folder}\" -type f | wc -l", token).Trim();
                    if (int.TryParse(fileCheck, out int fileCount) && fileCount > 0)
                    {
                        AnsiConsole.MarkupLine($"[blue]Acquiring {fileCount} files from:[/] {Escape(folderName)}");
                        RunAdbCommand(adbPath, $"pull --sync \"{folder}\" \"{targetPath}\"", token);

                        if (Directory.Exists(targetPath))
                        {
                            int actualCount = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories).Length;
                            if (actualCount > 0)
                            {
                                AnsiConsole.MarkupLine($"[green]✅ Acquired:[/] {folderName} [grey]({actualCount} files)[/]");
                            }
                            else
                            {
                                AnsiConsole.MarkupLine($"[yellow]⚠️ No files acquired from:[/] {Escape(folderName)}");
                                // Clean up empty directory
                                try { Directory.Delete(targetPath, true); } catch { }
                            }
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[grey]📁 Skipping empty folder:[/] {Escape(folderName)}");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]⚠️ Failed to acquire {folderName}: {Escape(ex.Message)}[/]");
                    continue;
                }
            }
        }

        // ==== Zip-as-Image Builder ====
        private static void CreateZipAsImg(string stagingPath, string zipPath, string imgPath)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (File.Exists(imgPath)) File.Delete(imgPath);

            ZipFile.CreateFromDirectory(stagingPath, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
            File.Move(zipPath, imgPath);
        }

        // ==== Improved ADB Command Runner ====
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

            _currentAdbProcess = process;

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.Start();

            var outputTask = Task.Run(() =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    token.ThrowIfCancellationRequested();
                    string line = process.StandardOutput.ReadLine();
                    if (line != null) outputBuilder.AppendLine(line);
                }
            }, token);

            var errorTask = Task.Run(() =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    token.ThrowIfCancellationRequested();
                    string line = process.StandardError.ReadLine();
                    if (line != null) errorBuilder.AppendLine(line);
                }
            }, token);

            var waitTask = process.WaitForExitAsync(token);

            try
            {
                Task.WaitAll(new[] { outputTask, errorTask, waitTask }, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // This is expected behavior when user cancels - just kill the process
                try { process.Kill(true); } catch { }
                throw; // Re-throw to be handled by the main cancellation handler
            }
            catch (Exception ex) when (ex is AggregateException || ex is OperationCanceledException)
            {
                // Handle any other cancellation-related exceptions
                try { process.Kill(true); } catch { }
                throw new OperationCanceledException("ADB command was cancelled", ex);
            }
            finally
            {
                _currentAdbProcess = null;
            }

            // Filter out the "0 files pulled" and other harmless ADB messages
            string error = errorBuilder.ToString();
            if (!string.IsNullOrWhiteSpace(error))
            {
                // Split error lines and filter out the ones we don't want to show
                var errorLines = error.Split('\n')
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Where(line => !line.Trim().EndsWith(": 0 files pulled, 0 skipped."))
                    .Where(line => !line.Trim().EndsWith(": 0 files pulled."))
                    .Where(line => !line.Contains("daemon not running"))
                    .Where(line => !line.Contains("daemon started successfully"))
                    .Where(line => !line.Contains("WARNING: linker: Warning:")) // Common Android linker warnings
                    .Where(line => !line.Contains("adb: warning")) // General ADB warnings
                    .ToList();

                if (errorLines.Count > 0)
                {
                    string filteredError = string.Join(Environment.NewLine, errorLines);
                    AnsiConsole.MarkupLine($"[red]{Escape(filteredError)}[/]");
                }
            }

            return outputBuilder.ToString();
        }

        // ==== Logging and Utilities ====
        private static void LogToDatabase(string caseId, string type, string tool, string outputPath, string hash)
        {
            string createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string outputPathRelative = outputPath.Replace(AppContext.BaseDirectory, "").TrimStart(Path.DirectorySeparatorChar);
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
            cmd.Parameters.AddWithValue("@output_path", outputPath);
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
            foreach (var proc in Process.GetProcessesByName("adb"))
            {
                try { proc.Kill(); } catch { }
            }
        }

        private static string Escape(string input)
        {
            return input.Replace("[", "\\[").Replace("]", "\\]").Replace("\\", "\\\\");
        }
    }
}