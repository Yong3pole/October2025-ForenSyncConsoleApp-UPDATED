using ForenSync_Console_App;
using ForenSync_Console_App.UI;
using ForenSync_Console_App.UI.MainMenuOptions.Help_SubMenu;
using System.IO;
using System.Text;
using Spectre.Console;
using System.Threading;

class Program
{
    private const string TermsFileName = "terms.accepted";

    private static void ShowWelcomeAnimation()
    {
        Console.CursorVisible = false;
        Console.Clear();

        string[] asciiArt = {
        "███████╗░█████╗░██████╗░███████╗███╗░░██╗░██████╗██╗░░░██╗███╗░░██╗░█████╗░",
        "██╔════╝██╔══██╗██╔══██╗██╔════╝████╗░██║██╔════╝╚██╗░██╔╝████╗░██║██╔══██╗",
        "█████╗░░██║░░██║██████╔╝█████╗░░██╔██╗██║╚█████╗░░╚████╔╝░██╔██╗██║██║░░╚═╝",
        "██╔══╝░░██║░░██║██╔══██╗██╔══╝░░██║╚████║░╚═══██╗░░╚██╔╝░░██║╚████║██║░░██╗",
        "██║░░░░░╚█████╔╝██║░░██║███████╗██║░╚███║██████╔╝░░░██║░░░██║░╚███║╚█████╔╝",
        "╚═╝░░░░░░╚════╝░╚═╝░░╚═╝╚══════╝╚═╝░░╚══╝╚═════╝░░░░╚═╝░░░╚═╝░░╚══╝░╚════╝░"
    };

        // Matrix-style vertical reveal
        int maxLength = asciiArt.Max(line => line.Length);

        for (int col = 0; col < maxLength; col++)
        {
            for (int row = 0; row < asciiArt.Length; row++)
            {
                if (col < asciiArt[row].Length)
                {
                    Console.SetCursorPosition(col, row);
                    var color = GetMatrixColor(row, col);
                    AnsiConsole.Markup($"[{color}]{asciiArt[row][col]}[/]");
                }
            }
            Thread.Sleep(10);
        }

        ContinueWithSpinner();
    }

    private static string GetMatrixColor(int row, int col)
    {
        var colors = new[] { "green", "lime", "white", "grey" };
        var random = new Random((row * 1000) + col);
        return colors[random.Next(colors.Length)];
    }

    private static void ContinueWithSpinner()
    {
        Console.SetCursorPosition(0, 8);
        AnsiConsole.MarkupLine("[bold lime]By MEDEL | TIJOL | Tripole[/]");

        AnsiConsole.Status()
            .Start("Loading modules...", ctx =>
            {
                ctx.Spinner(Spinner.Known.Binary);
                ctx.SpinnerStyle(Style.Parse("green"));
                Thread.Sleep(2000);
            });

        Thread.Sleep(500);
        Console.Clear();
    }

    static void Main(string[] args)
    {

        // Try add diri ang script tas pagset ug error handling kung di makita ang mounted drives, return an error message

        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        if (!HasAcceptedTerms())
        {
            ShowTermsAndPromptUser();
        }

        // Cool welcome animation
        ShowWelcomeAnimation();

        LoginPage.PromptCredentials();
    }

    static bool HasAcceptedTerms()
    {
        string path = Path.Combine(AppContext.BaseDirectory, TermsFileName);
        return File.Exists(path);
    }

    static void ShowTermsAndPromptUser()
    {
        TermsOfUse.Show();

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold yellow]Do you accept these Terms of Use?[/]")
                .PageSize(3)
                .AddChoices(new[]
                {
                    "[green]1[/] = Yes, I accept",
                    "[red]2[/] = No, exit"
                }));

        if (selection.StartsWith("2"))
        {
            AnsiConsole.MarkupLine("[red]❌ You must accept the Terms to use ForenSyncCLI.[/]");
            Environment.Exit(0);
        }

        SaveTermsAcceptance();
    }

    static void SaveTermsAcceptance()
    {
        string path = Path.Combine(AppContext.BaseDirectory, TermsFileName);
        File.WriteAllText(path, $"Accepted: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    }
}
