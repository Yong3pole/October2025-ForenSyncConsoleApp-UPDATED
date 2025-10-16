using ForenSync_Console_App;
using ForenSync_Console_App.UI;
using ForenSync_Console_App.UI.MainMenuOptions.Help_SubMenu;
using Spectre.Console;
using System.IO;
using System.Text;

class Program
{
    private const string TermsFileName = "terms.accepted";

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        if (!HasAcceptedTerms())
        {
            ShowTermsAndPromptUser();
        }

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
