using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Spectre.Console;
using System.Management;

namespace ForenSync_Console_App.UI.MainMenuOptions.DeviceInfo_SubMenu
{
    public static class ViewDiskLayout
    {
        public static void Show()
        {
            AnsiConsole.Clear();
            Console.CursorVisible = false;
            AsciiTitle.Render("ForenSync");

            var table = new Table()
                .RoundedBorder()
                .Title("[bold yellow]Physical Disk Overview[/]")
                .AddColumn("[blue]Model[/]")
                .AddColumn("[green]Interface[/]")
                .AddColumn("[yellow]Size[/]")
                .AddColumn("[cyan]Serial[/]");

            try
            {
                foreach (var disk in new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive").Get())
                {
                    string model = disk["Model"]?.ToString() ?? "N/A";
                    string iface = disk["InterfaceType"]?.ToString() ?? "N/A";
                    string size = FormatBytes(disk["Size"]?.ToString());
                    string serial = disk["SerialNumber"]?.ToString() ?? "N/A";

                    table.AddRow(model, iface, size, serial);
                }

                AnsiConsole.Write(new Panel(table)
                    .Header("[bold green]Physical Disks[/]")
                    .Border(BoxBorder.Double)
                    .Padding(1, 1)
                    .BorderStyle(new Style(Color.Blue)));

                var volumeTable = new Table()
                    .RoundedBorder()
                    .Title("[bold yellow]Logical Volumes[/]")
                    .AddColumn("[blue]Drive[/]")
                    .AddColumn("[green]Label[/]")
                    .AddColumn("[yellow]File System[/]")
                    .AddColumn("[cyan]Free Space[/]")
                    .AddColumn("[magenta]Total Size[/]");

                foreach (var vol in new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDisk WHERE DriveType=3").Get())
                {
                    string name = vol["DeviceID"]?.ToString() ?? "N/A";
                    string label = vol["VolumeName"]?.ToString() ?? "N/A";
                    string fs = vol["FileSystem"]?.ToString() ?? "N/A";
                    string free = FormatBytes(vol["FreeSpace"]?.ToString());
                    string total = FormatBytes(vol["Size"]?.ToString());

                    volumeTable.AddRow(name, label, fs, free, total);
                }

                AnsiConsole.Write(new Panel(volumeTable)
                    .Header("[bold green]Logical Volumes[/]")
                    .Border(BoxBorder.Double)
                    .Padding(1, 1)
                    .BorderStyle(new Style(Color.Green)));

                var breakdownItems = new List<(string Label, double Value, Color Color)>();
                var colors = new[] { Color.Green, Color.Blue, Color.Yellow, Color.Orange1, Color.Magenta1, Color.Cyan1 };
                int colorIndex = 0;

                foreach (var vol in new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDisk WHERE DriveType=3").Get())
                {
                    string name = vol["DeviceID"]?.ToString() ?? "N/A";
                    string label = vol["VolumeName"]?.ToString() ?? "N/A";
                    string totalRaw = vol["Size"]?.ToString();
                    string freeRaw = vol["FreeSpace"]?.ToString();

                    if (ulong.TryParse(totalRaw, out ulong total) && ulong.TryParse(freeRaw, out ulong free))
                    {
                        double used = (total - free) / (1024.0 * 1024 * 1024); // GB
                        string tag = string.IsNullOrWhiteSpace(label) ? name : $"{name} ({label})";

                        breakdownItems.Add((tag, used, colors[colorIndex % colors.Length]));
                        colorIndex++;
                    }
                }

                AnsiConsole.Write(new BreakdownChart()
                    .Width(60)
                    .ShowPercentage()
                    .UseValueFormatter(v => $"{v:F1} GB")
                    .AddItems(breakdownItems, item => new BreakdownChartItem(item.Label, item.Value, item.Color)));
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]❌ Failed to retrieve disk layout: {ex.Message}[/]");
            }

            AnsiConsole.MarkupLine("\n[grey]Press any key to return to Device Info menu...[/]");
            Console.ReadKey(true);
        }

        private static string FormatBytes(string rawBytes)
        {
            if (ulong.TryParse(rawBytes, out ulong bytes))
            {
                double gb = bytes / (1024.0 * 1024 * 1024);
                return $"{gb:F2} GB";
            }
            return "N/A";
        }
    }
}
