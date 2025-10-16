using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ForenSync_Console_App.UI
{
    public static class AsciiTitle
    {
        public static void Render(string title)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            string[] bannerLines = new string[]
{
    "███████╗░█████╗░██████╗░███████╗███╗░░██╗░██████╗██╗░░░██╗███╗░░██╗░█████╗░",
    "██╔════╝██╔══██╗██╔══██╗██╔════╝████╗░██║██╔════╝╚██╗░██╔╝████╗░██║██╔══██╗",
    "█████╗░░██║░░██║██████╔╝█████╗░░██╔██╗██║╚█████╗░░╚████╔╝░██╔██╗██║██║░░╚═╝",
    "██╔══╝░░██║░░██║██╔══██╗██╔══╝░░██║╚████║░╚═══██╗░░╚██╔╝░░██║╚████║██║░░██╗",
    "██║░░░░░╚█████╔╝██║░░██║███████╗██║░╚███║██████╔╝░░░██║░░░██║░╚███║╚█████╔╝",
    "╚═╝░░░░░░╚════╝░╚═╝░░╚═╝╚══════╝╚═╝░░╚══╝╚═════╝░░░░╚═╝░░░╚═╝░░╚══╝░╚════╝░"
};
   
         
            int windowWidth = Console.WindowWidth;
            foreach (string line in bannerLines)
            {
                Console.WriteLine(line);
            }


            Console.ResetColor();
            Console.WriteLine($"\nWelcome to {title} Toolkit © 2025\n");
        }
    }

}
