using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Spectre.Console;

namespace ForenSync_Console_App.UI.MainMenuOptions.Help_SubMenu
{
    public static class ViewDocumentation
    {
        public static void Show()
        {
            AnsiConsole.Clear();
            AsciiTitle.Render("ForenSync Documentation");

            var docText = new Markup(@"
                    [bold green]Welcome to ForenSync[/]

                    ForenSync is a modular, forensic-grade toolkit designed to help investigators and analysts:

                    • [yellow]Capture system snapshots[/] with clarity and precision  
                    • [yellow]View running processes[/] and detect anomalies  
                    • [yellow]List network connections[/] for traceability  
                    • [yellow]Audit USB devices[/] and removable media access  
                    • [yellow]Log case data[/] with integrity and expressive visuals

                    [bold]Key Features:[/]
                    • Modular CLI interface with expressive Spectre.Console visuals  
                    • Snapshot logging for forensic traceability  
                    • Cross-platform awareness and privilege detection  
                    • Designed for clarity, speed, and forensic robustness

                    [italic grey]ForenSync is built to empower digital investigations with clarity, modularity, and expressive UX.[/]
                    ");

            AnsiConsole.Write(new Panel(docText)
                .Header("[bold blue]About ForenSync[/]")
                .Border(BoxBorder.Double)
                .Padding(1, 1)
                .BorderStyle(new Style(Color.Green)));

            AnsiConsole.MarkupLine("\n[grey]Press any key to return to Help menu...[/]");
            Console.ReadKey(true);
        }
    }
}
