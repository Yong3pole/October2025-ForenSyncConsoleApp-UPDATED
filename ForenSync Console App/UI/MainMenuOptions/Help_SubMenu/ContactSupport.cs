using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Spectre.Console;

namespace ForenSync_Console_App.UI.MainMenuOptions.Help_SubMenu
{
    public static class ContactSupport
    {
        public static void Show()
        {
            AnsiConsole.Clear();
            AsciiTitle.Render("Contact Support");

            var supportText = new Markup(@"
                [bold green]Need help with ForenSync?[/]

                If you're experiencing issues, have questions, or want to report a bug, we're here to help.

                [bold]Support Channels:[/]
                • [yellow]Email:[/] read.medel16@gmail.com  
                • [yellow]Phone:[/] +63 - 916 - 362 - 1891   
                • [yellow]Documentation:[/] See [blue]📖 View Documentation[/] for usage tips

                [italic grey]Our team is ready to assist you with any technical or operational concerns.[/]
                ");

            AnsiConsole.Write(new Panel(supportText)
                .Header("[bold blue]Contact ForenSync Support[/]")
                .Border(BoxBorder.Double)
                .Padding(1, 1)
                .BorderStyle(new Style(Color.Green)));

            AnsiConsole.MarkupLine("\n[grey]Press any key to return to Help menu...[/]");
            Console.ReadKey(true);
        }
    }
}
