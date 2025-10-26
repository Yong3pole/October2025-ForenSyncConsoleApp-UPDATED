using Spectre.Console;

namespace ForenSync_Console_App.UI.MainMenuOptions.Help_SubMenu
{
    public static class TermsOfUse
    {
        public static void Show()
        {
            AnsiConsole.Clear();
            AsciiTitle.Render("Terms of Use");

            var termsText = new Markup(@"
[bold blue]ForenSyncCLI Terms of Use – Version 1.0[/]  
[grey]Effective Date:[/] [green]TBD[/]  
[grey]Developed by:[/] [green]Your University Name – Capstone Project Team[/]  
[grey]Beneficiary:[/] [green]Philippine National Police – Cybercrime Group[/]  
[grey]Jurisdiction:[/] [green]Republic of the Philippines[/]

ForenSyncCLI is a command-line forensic acquisition tool developed exclusively for the Philippine National Police Cybercrime Group. It is designed to facilitate secure, reproducible, and court-defensible digital evidence acquisition. Use of this tool is strictly limited to authorized personnel operating within the bounds of Philippine law.

[bold]Authorized Use[/]  
• You are a duly authorized member of the PNP Cybercrime Group or an individual explicitly permitted by the PNP to operate this tool.  
• You understand and accept the responsibilities associated with handling digital forensic evidence.  
• You will not use this tool for personal, recreational, or unauthorized surveillance purposes.

[bold]Evidence Integrity[/]  
• All acquired data must be preserved in its original form.  
• You must not alter, tamper with, or falsify any evidence acquired using this tool.  
• ForenSyncCLI automatically logs all acquisition actions using tamper-evident hashing for traceability.

[bold]Audit Logging and Traceability[/]  
• Every user action is recorded with timestamp, user ID, case ID, and tool context.  
• Logs are stored in immutable formats using canonical entry hashing.  
• You consent to this logging and acknowledge its use in internal reviews or legal proceedings.

[bold]Snapshot and Export Controls[/]  
• Snapshots and exports are only permitted when an active case session is present.  
• Attempting to bypass this safeguard is considered a violation of forensic protocol.  
• ForenSyncCLI will block unauthorized exports and notify the user with actionable feedback.

[bold]Data Privacy and Confidentiality[/]  
• You are responsible for ensuring that all acquired data complies with Republic Act No. 10173 (Data Privacy Act of 2012) and other applicable laws.  
• ForenSyncCLI performs local acquisition only; however, extracted evidence may be transferred to a browser-based application for secure storage and analysis.  
• You must ensure that all transfers, storage, and access controls in both CLI and browser environments uphold data confidentiality, integrity, and lawful handling.  
• You must protect sensitive data from unauthorized access, both during acquisition and throughout its lifecycle in the analysis platform.

[bold]Acceptable Use[/]  
You agree not to:  
• Use ForenSyncCLI for any unlawful, unethical, or deceptive purpose.  
• Share access credentials or bypass authentication safeguards.  
• Reverse-engineer, modify, or redistribute the application without written consent from the development team or the PNP Cybercrime Group.

[bold]Disclaimer of Warranty[/]  
ForenSyncCLI is provided “as-is” without warranty of any kind. The developers are not liable for:  
• Data loss due to user error or environmental factors.  
• Misuse of the tool resulting in legal or operational consequences.  
• Compatibility issues with third-party systems or configurations.

[bold]Limitation of Liability[/]  
Under no circumstances shall the developers or affiliated academic institutions be held liable for:  
• Indirect, incidental, or consequential damages.  
• Legal claims arising from improper use or unauthorized deployment.  
• Damages exceeding the cost of the software license (if applicable).

[bold]Consent and Enforcement[/]  
• You must accept these Terms of Use before using ForenSyncCLI.  
• Acceptance is logged with timestamp and user ID for traceability.  
• Violations may result in access revocation, internal investigation, or legal action under Philippine law.

[bold]Data Retention and Disposal[/]  
• Evidence acquired using ForenSyncCLI must be retained only for as long as legally required or operationally necessary.  
• Secure deletion procedures must be followed when disposing of forensic data, including wiping of storage media and removal of audit logs if permitted.  
• The development team is not responsible for improper retention or disposal practices.

[bold]Updates and Amendments[/]  
• These Terms of Use may be updated as ForenSyncCLI evolves.  
• Users will be notified of significant changes and may be required to re-accept updated terms.  
• Continued use of the tool after updates constitutes acceptance of the revised Terms.


[italic grey]For support or feedback, contact: read.medel16@gmail.com[/]
");

            AnsiConsole.Write(new Panel(termsText)
                .Header("[bold green]Terms of Use[/]")
                .Border(BoxBorder.Double)
                .Padding(1, 1)
                .BorderStyle(new Style(Color.Blue)));

            AnsiConsole.MarkupLine("\n[grey]Press any key to return to Continue...[/]");
            Console.ReadKey(true);
        }
    }
}
