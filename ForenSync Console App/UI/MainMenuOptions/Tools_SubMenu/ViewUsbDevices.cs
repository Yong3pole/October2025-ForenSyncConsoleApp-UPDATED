using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Spectre.Console;
using System.Management;

namespace ForenSync_Console_App.UI.MainMenuOptions.Tools_SubMenu
{
    public static class ViewUsbDevices
    {
        public static void Show()
        {
            AnsiConsole.Clear();
            Console.CursorVisible = false;
            AsciiTitle.Render("USB Devices / Removable Media");

            List<ManagementObject> devices = new();

            try
            {
                var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Description LIKE '%USB%'");
                foreach (ManagementObject device in searcher.Get())
                {
                    devices.Add(device);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]❌ Failed to query USB devices: {ex.Message}[/]");
                return;
            }

            // Bar Chart: Device types
            var typeGroups = devices
                .GroupBy(d => d["Description"]?.ToString() ?? "Unknown")
                .Select(g => new BarChartItem(g.Key, g.Count(), Color.Green))
                .ToList();

            AnsiConsole.Write(new BarChart()
                .Width(60)
                .Label("[bold underline green]USB Device Types[/]")
                .CenterLabel()
                .AddItems(typeGroups));

            // Tree View: Top 5 devices
            var root = new Tree("[bold yellow]Connected USB Devices[/]").Guide(TreeGuide.BoldLine);

            foreach (var device in devices.Take(5))
            {
                string name = device["Name"]?.ToString() ?? "Unknown";
                string desc = device["Description"]?.ToString() ?? "N/A";
                string pnpId = device["PNPDeviceID"]?.ToString() ?? "N/A";

                var node = root.AddNode($"[green]{Markup.Escape(name)}[/]");
                node.AddNode($"Description: {Markup.Escape(desc)}");
                node.AddNode($"PNP ID: {Markup.Escape(pnpId)}");
            }

            AnsiConsole.Write(root);

            // Table: Full list
            var table = new Table()
                .RoundedBorder()
                .AddColumn("[blue]Device Name[/]")
                .AddColumn("[green]Description[/]")
                .AddColumn("[yellow]PNP ID[/]");

            foreach (var device in devices)
            {
                string name = Markup.Escape(device["Name"]?.ToString() ?? "Unknown");
                string desc = Markup.Escape(device["Description"]?.ToString() ?? "N/A");
                string pnpId = Markup.Escape(device["PNPDeviceID"]?.ToString() ?? "N/A");

                table.AddRow(name, desc, pnpId);
            }

            AnsiConsole.Write(new Panel(table)
                .Header("[bold green]USB Device Snapshot[/]")
                .Border(BoxBorder.Double)
                .Padding(1, 1)
                .BorderStyle(new Style(Color.Blue)));

            AnsiConsole.MarkupLine("\n[grey]Press any key to return to Tools menu...[/]");
            Console.ReadKey(true);
        }
    }
}
