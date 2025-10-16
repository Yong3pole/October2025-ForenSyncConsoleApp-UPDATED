using Spectre.Console;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using System.Threading;
using System.Text;
using System.Threading.Tasks;

namespace ForenSync_Console_App.UI.MainMenuOptions.CaseOperations_SubMenu
{
    public static class AndroidAcquisition
    {
        private static CancellationTokenSource _cts;
        private static string _acquisitionPath = string.Empty;

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
                _cts.Cancel();
                e.Cancel = true;
                AnsiConsole.MarkupLine("[yellow]⏹ ESC pressed — canceling acquisition...[/]");
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
                    string output = Run(adbPath, "devices", _cts.Token).Trim();
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
            AnsiConsole.MarkupLine("[grey](Press ESC to cancel)[/]");

            try
            {
                string manufacturer = Run(adbPath, "shell getprop ro.product.manufacturer", _cts.Token).Trim();
                string model = Run(adbPath, "shell getprop ro.product.model", _cts.Token).Trim();
                string androidVersion = Run(adbPath, "shell getprop ro.build.version.release", _cts.Token).Trim();
                string serial = Run(adbPath, "get-serialno", _cts.Token).Trim();

                var table = new Table().Border(TableBorder.Rounded).Title("[bold cyan]Connected Android Device[/]");
                table.AddColumn("Property").AddColumn("Value");
                table.AddRow("Manufacturer", manufacturer);
                table.AddRow("Model", model);
                table.AddRow("Android Version", androidVersion);
                table.AddRow("Serial Number", serial);
                AnsiConsole.Write(table);

                AnsiConsole.Status()
                    .Spinner(Spinner.Known.Line)
                    .SpinnerStyle(Style.Parse("green"))
                    .Start("📦 Acquiring data from device...", ctx =>
                    {
                        AcquireSystemData(adbPath, stagingPath);
                        AcquireUserData(adbPath, stagingPath);
                    });

                string imgPath = Path.Combine(_acquisitionPath, "android_evidence.img");

                long totalBytes = 0;
                var files = Directory.GetFiles(stagingPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                    totalBytes += new FileInfo(file).Length;

                using var fs = new FileStream(imgPath, FileMode.Create, FileAccess.Write);
                using var hasher = SHA256.Create();

                AnsiConsole.Progress()
                    .AutoClear(true)
                    .Columns(new ProgressColumn[]
                    {
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new SpinnerColumn(),
                        new RemainingTimeColumn(),
                        new ElapsedTimeColumn()
                    })
                    .Start(ctx =>
                    {
                        var task = ctx.AddTask("Creating disk image", autoStart: true);
                        task.MaxValue = totalBytes;

                        foreach (var file in files)
                        {
                            if (_cts.IsCancellationRequested) throw new OperationCanceledException();

                            byte[] content = File.ReadAllBytes(file);
                            fs.Write(content, 0, content.Length);
                            hasher.TransformBlock(content, 0, content.Length, null, 0);
                            task.Increment(content.Length);
                        }
                    });

                Directory.Delete(stagingPath, true);
                hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                string hash = BitConverter.ToString(hasher.Hash).Replace("-", "").ToLowerInvariant();

                LogToDatabase(caseId, "android", "ADB Acquisition", imgPath, hash);

                AnsiConsole.MarkupLine($"[green]✅ Acquisition complete.[/]");
                AnsiConsole.MarkupLine($"Image saved: [italic]{Escape(imgPath)}[/]");
                AnsiConsole.MarkupLine($"SHA-256: [bold]{hash}[/]");
            }
            catch (OperationCanceledException)
            {
                SafeDelete(_acquisitionPath);
                KillAdbProcesses();
                AnsiConsole.MarkupLine("[yellow]Acquisition canceled by ESC. Folder deleted.[/]");
            }
            catch (Exception ex)
            {
                SafeDelete(_acquisitionPath);
                KillAdbProcesses();
                AnsiConsole.MarkupLine($"[red]Error:[/] {Escape(ex.Message)}");
            }

            KillAdbProcesses();
            Console.ResetColor();
            Console.Clear();
            CaseOperations.Show(caseId, userId, isNewCase);
        }

        // ==== Acquisition Steps ====

        private static void AcquireSystemData(string adbPath, string stagingPath)
        {
            if (_cts.IsCancellationRequested) throw new OperationCanceledException();

            File.WriteAllText(Path.Combine(stagingPath, "device_info.txt"),
                $"Manufacturer: {Run(adbPath, "shell getprop ro.product.manufacturer", _cts.Token).Trim()}\n" +
                $"Model: {Run(adbPath, "shell getprop ro.product.model", _cts.Token).Trim()}\n" +
                $"Android Version: {Run(adbPath, "shell getprop ro.build.version.release", _cts.Token).Trim()}\n" +
                $"Serial: {Run(adbPath, "get-serialno", _cts.Token).Trim()}");

            File.WriteAllText(Path.Combine(stagingPath, "battery.txt"), Run(adbPath, "shell dumpsys battery", _cts.Token));
            File.WriteAllText(Path.Combine(stagingPath, "activity.txt"), Run(adbPath, "shell dumpsys activity", _cts.Token));
            File.WriteAllText(Path.Combine(stagingPath, "packages.txt"), Run(adbPath, "shell pm list packages", _cts.Token));
            File.WriteAllText(Path.Combine(stagingPath, "logcat.txt"), Run(adbPath, "shell logcat -d", _cts.Token));
            File.WriteAllText(Path.Combine(stagingPath, "df.txt"), Run(adbPath, "shell df /storage/emulated/0", _cts.Token));
        }

        private static void AcquireUserData(string adbPath, string stagingPath)
        {
            if (_cts.IsCancellationRequested) throw new OperationCanceledException();

            var folders = Run(adbPath, "shell ls -d /storage/emulated/0/*/", _cts.Token)
                .Split('\n')
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();

            foreach (var folder in folders)
            {
                if (_cts.IsCancellationRequested) throw new OperationCanceledException();

                string folderName = Path.GetFileName(folder.TrimEnd('/'));
                string targetPath = Path.Combine(stagingPath, folderName);

                AnsiConsole.MarkupLine($"[blue]Acquiring folder:[/] {Escape(folder)}");

                try
                {
                    Run(adbPath, $"pull --sync {folder} \"{targetPath}\"", _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    continue;
                }

                if (Directory.Exists(targetPath))
                {
                    int count = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories).Length;
                    if (count > 0)
                    {
                        AnsiConsole.MarkupLine($"[green]✅ Acquired:[/] {folderName} [grey]({count} files)[/]");
                    }
                }
            }
        }

        // ==== Logging and Utilities ====

        private static void LogToDatabase(string caseId, string type, string tool, string outputPath, string hash)
        {
            string createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string outputPathRelative = outputPath.Replace(AppContext.BaseDirectory, "").TrimStart(Path.DirectorySeparatorChar);
            string canonicalEntry = $"{caseId}|{type}|{tool}|{outputPathRelative}|{hash}|{createdAt}";
            string entryHash = ComputeSha256(canonicalEntry);

            string dbPath = Path.Combine(AppContext.BaseDirectory, "forensync.db");
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO acquisition_log (case_id, type, tool, output_path, hash, created_at, entry_hash)
                VALUES (@case_id, @type, @tool, @output_path, @hash, @created_at, @entry_hash)";
            command.Parameters.AddWithValue("@case_id", caseId);
            command.Parameters.AddWithValue("@type", type);
            command.Parameters.AddWithValue("@tool", tool);
            command.Parameters.AddWithValue("@output_path", outputPath);
            command.Parameters.AddWithValue("@hash", hash);
            command.Parameters.AddWithValue("@created_at", createdAt);
            command.Parameters.AddWithValue("@entry_hash", entryHash);
            command.ExecuteNonQuery();
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

        private static string Run(string adbPath, string arguments, CancellationToken token)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException();

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

            process.Start();

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            while (!process.HasExited)
            {
                if (token.IsCancellationRequested)
                {
                    try { process.Kill(true); } catch { }
                    throw new OperationCanceledException();
                }

                outputBuilder.Append(process.StandardOutput.ReadToEnd());
                errorBuilder.Append(process.StandardError.ReadToEnd());
                Thread.Sleep(50);
            }

            string output = outputBuilder.ToString();
            string error = errorBuilder.ToString();

            if (!string.IsNullOrWhiteSpace(error) &&
                !error.Contains("daemon not running") &&
                !error.Contains("daemon started successfully"))
            {
                AnsiConsole.MarkupLine($"[red]{Escape(error)}");
            }

            return output;
        }

        private static string Escape(string input)
        {
            return input.Replace("[", "\\[").Replace("]", "\\]").Replace("\\", "\\\\");
        }
    }
}

