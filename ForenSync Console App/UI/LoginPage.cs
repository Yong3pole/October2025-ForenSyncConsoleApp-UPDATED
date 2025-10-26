using ForenSync_Console_App.CaseManagement;
using ForenSync_Console_App.Data;
using Spectre.Console;
using System;

namespace ForenSync_Console_App.UI
{
    public static class LoginPage
    {

        private class LoginField
        {

            public string Label { get; set; }
            public string Value { get; set; } = "";
            public bool IsSecret { get; set; } = false;
        }

        public static void PromptCredentials()
        {

            AnsiConsole.Clear();
            AsciiTitle.Render("ForenSync");

            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Please select an operation to begin.[/]")
                    .PageSize(3)
                    .AddChoices("🔐 Log in", "🚪 Exit"));

            Console.CursorVisible = false;

            if (action == "🚪 Exit")
            {
                AnsiConsole.MarkupLine("\n[red]👋 Exiting ForenSync. Stay safe out there.[/]");
                Environment.Exit(0);
            }

            bool showError = false;

            while (true)
            {
                var fields = new[]
                {
                    new LoginField { Label = "Enter User ID", IsSecret = false },
                    new LoginField { Label = "Enter Password", IsSecret = true }
                };

                int fieldIndex = 0;

                while (true)
                {
                    AnsiConsole.Clear();
                    AsciiTitle.Render("ForenSync");
                    AnsiConsole.MarkupLine("[bold blue]🔐 Please log in to continue[/]");
                    if (showError)
                        AnsiConsole.MarkupLine("[red]❌ Invalid credentials. Please try again.[/]\n");

                    AnsiConsole.MarkupLine("[green]Use ↑↓ to navigate, [[Del]] to clear, [[Enter]] to submit, [[Esc]] to exit.[/]\n");
                    Console.CursorVisible = false;


                    for (int i = 0; i < fields.Length; i++)
                    {
                        var field = fields[i];
                        string highlight = i == fieldIndex ? "[blue bold]" : "";
                        string end = i == fieldIndex ? "[/]" : "";
                        string displayValue = field.IsSecret ? new string('*', field.Value.Length) : field.Value;
                        AnsiConsole.MarkupLine($"{highlight}{field.Label,-20}:{end} {displayValue}");
                    }

                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.UpArrow)
                        fieldIndex = (fieldIndex - 1 + fields.Length) % fields.Length;
                    else if (key.Key == ConsoleKey.DownArrow)
                        fieldIndex = (fieldIndex + 1) % fields.Length;
                    else if (key.Key == ConsoleKey.Delete)
                        fields[fieldIndex].Value = "";
                    else if (key.Key == ConsoleKey.Enter)
                        break;
                    else if (key.Key == ConsoleKey.Backspace && fields[fieldIndex].Value.Length > 0)
                        fields[fieldIndex].Value = fields[fieldIndex].Value[..^1];
                    else if (key.KeyChar >= 32 && key.KeyChar <= 126)
                        fields[fieldIndex].Value += key.KeyChar;
                    else if (key.Key == ConsoleKey.Escape)
                    {
                        AnsiConsole.MarkupLine("\n[red]👋 Exiting ForenSync. Stay safe out there.[/]");
                        Environment.Exit(0);
                    }
                }

                string userId = fields[0].Value.Trim();
                string password = fields[1].Value.Trim();

                if (userId.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    AnsiConsole.MarkupLine("\n[red]👋 Exiting ForenSync. Stay safe out there.[/]");
                    Environment.Exit(0);
                }

                AnsiConsole.Status()
                    .SpinnerStyle(Style.Parse("yellow"))
                    .Start("🔄 Authenticating...", ctx =>
                    {
                        Thread.Sleep(3000);
                    });

                bool isAuthenticated = UserAuthenticator.ValidateUser(userId, password);

                if (isAuthenticated)
                {
                    ShowSessionPrompt(userId);
                    return;
                }
                else
                {
                    showError = true;
                }
            }
        }

        public static void ShowSessionPrompt(string userId)
        {
            var options = new[]
            {
                "🆕 Initiate new case or session",
                "📂 Load existing case",
                "⏭️ Skip setup and open to main menu"
            };

            int selectedIndex = 0;

            while (true)
            {
                AnsiConsole.Clear();
                Console.CursorVisible = false;
                AsciiTitle.Render("ForenSync");
                AnsiConsole.MarkupLine("[green]✅ Login successful![/]\n");
                AnsiConsole.MarkupLine("────────────────────────────────────────────");
                AnsiConsole.MarkupLine("[green]Use ↑↓ to navigate, [[Enter]] to select, [[Esc]] to return to login.[/]\n");
                AnsiConsole.MarkupLine("[bold blue]🧭 Session Options: Choose to proceed:[/]\n");

                for (int i = 0; i < options.Length; i++)
                {
                    string prefix = i == selectedIndex ? "[bold blue]> " : "  ";
                    string suffix = i == selectedIndex ? "[/]" : "";
                    AnsiConsole.MarkupLine($"{prefix}{options[i]}{suffix}");
                }

                var key = Console.ReadKey(true).Key;

                switch (key)
                {
                    case ConsoleKey.UpArrow:
                        selectedIndex = (selectedIndex - 1 + options.Length) % options.Length;
                        break;

                    case ConsoleKey.DownArrow:
                        selectedIndex = (selectedIndex + 1) % options.Length;
                        break;

                    case ConsoleKey.Enter:
                        switch (options[selectedIndex])
                        {
                            case "🆕 Initiate new case or session":
                                CaseSession.StartNewCase(userId);
                                return;

                            case "📂 Load existing case":
                                string selectedCaseId = CaseSession.SelectExistingCase(userId);
                                if (!string.IsNullOrEmpty(selectedCaseId))
                                {
                                    MainMenu.Show(selectedCaseId, userId, false);
                                }
                                break;

                            case "⏭️ Skip setup and open to main menu":
                                MainMenu.Show(null, userId, false);
                                return;
                        }
                        break;

                    case ConsoleKey.Escape:
                        AnsiConsole.Clear();
                        PromptCredentials();
                        return;
                }
            }
        }
    }
}
