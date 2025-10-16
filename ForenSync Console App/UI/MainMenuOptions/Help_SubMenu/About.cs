using Spectre.Console;

namespace ForenSync_Console_App.UI.MainMenuOptions.Help_SubMenu
{
    public static class About
    {
        public static void Show()
        {
            AnsiConsole.Clear();
            AsciiTitle.Render("About ForenSync");

            var aboutText = new Markup(@"
                [bold blue]ForenSync CLI Suite[/]  
                [grey]Version:[/] [green]v1.0.0[/]  
                [grey]Build Date:[/] [green]2025-10-12[/]  
                [grey]Developer:[/] [green]Prince Paulo Medel, Kirk Ivan Tijol, and Leoj Tripole[/]

                ForenSyncCLI.exe is a field-ready command-line application purpose-built for secure, 
                reproducible forensic evidence acquisition. Designed with operational integrity at its core, 
                it empowers investigators, analysts, and digital responders to capture and preserve digital artifacts with precision and court-defensible traceability.

                [italic grey]For support or feedback, contact: read.medel16@gmail.com[/]
                ");

            AnsiConsole.Write(new Panel(aboutText)
                .Header("[bold green]About This Tool[/]")
                .Border(BoxBorder.Double)
                .Padding(1, 1)
                .BorderStyle(new Style(Color.Blue)));

            AnsiConsole.MarkupLine("\n[grey]Press any key to return to Help menu...[/]");
            Console.ReadKey(true);
        }
    }
}
