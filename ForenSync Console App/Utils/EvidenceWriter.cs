using System;
using System.IO;
using Spectre.Console;

namespace ForenSync.Utils
{
    public static class EvidenceWriter
    {
        /// <summary>
        /// Saves the given content to a timestamped .txt file inside the case's Evidence folder.
        /// </summary>
        public static void SaveToEvidence(string casePath, string content, string label)
        {
            if (string.IsNullOrWhiteSpace(casePath))
            {
                Console.WriteLine("[debug] ❌ casePath is null or empty. Aborting save.");
                return;
            }

            string evidencePath = Path.Combine(casePath, "Evidence");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{label}_{timestamp}.txt";
            string filePath = Path.Combine(evidencePath, fileName);

            try
            {
                Directory.CreateDirectory(evidencePath);
                File.WriteAllText(filePath, content);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ Saved to Evidence: {fileName}");
                Console.WriteLine($"[debug] File written to: {filePath}");
                Console.WriteLine($"[debug] Content length: {content.Length}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Error saving to Evidence: {ex.Message}");
                PromptRetry();
            }
        }

        /// <summary>
        /// Safely reads a key from the console if input is available.
        /// Returns null if input is redirected or unavailable.
        /// </summary>
        public static ConsoleKeyInfo? TryReadKey()
        {
            if (Environment.UserInteractive &&
                !Console.IsInputRedirected &&
                !Console.IsOutputRedirected)
            {
                try
                {
                    return Console.ReadKey(true);
                }
                catch (InvalidOperationException)
                {
                    // Input not available—skip gracefully
                }
            }
            return null;
        }

        /// <summary>
        /// Prompts the user to press Enter, with modal hygiene.
        /// Skips prompt if input is unavailable.
        /// </summary>
        public static void PromptEnter()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Press Enter to continue...");
            Console.ResetColor();
            TryReadKey();
        }

        /// <summary>
        /// Prompts the user to retry after an error, with modal hygiene.
        /// Skips prompt if input is unavailable.
        /// </summary>
        public static void PromptRetry()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Press Enter to try again...");
            Console.ResetColor();
            TryReadKey();
        }
    }
}
