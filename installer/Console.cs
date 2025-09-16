using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RAWebInstaller
{
  internal sealed class RAWebInstallerCommand : Command<RAWebInstallerCommand.Settings>
  {
    public sealed class Settings : CommandSettings
    {

      [Description("Perform an express installation with recommended settings. In most cases, this option will install RAWeb to the default location without any further prompts or questions. It is recommended for most users. Combine this with other options to customize specific settings while still using the express installation defaults for everything else.")]
      [CommandOption("--express")]
      public bool? ExpressMode { get; init; }

      [Description("Whether to install IIS if it is not already installed.")]
      [CommandOption("--install-iis")]
      public bool? InstallIIS { get; init; }

      [Description("The IIS website into which RAWeb should be installed. It should be the name of an existing IIS website, such as 'Default Web Site'.")]
      [CommandOption("--site-name")]
      public string? SiteName { get; init; }

      [Description("The path within an IIS website into which RAWeb should be installed. It should start with a forward slash (/) and not end with a forward slash unless it is just '/'.")]
      [CommandOption("--path")]
      public string? DestinationPath { get; init; }

      [Description("The name of the application pool to create/use for the RAWeb application. If not specified, a name will be generated based on the website and path.")]
      [CommandOption("--app-pool-name")]
      public string? AppPoolName { get; init; }


      [Description("Whether to enable HTTPS in the selected IIS website if it is not already enabled. If this is true, and there is no certificate bound to the HTTPS binding, a self-signed certificate will be created and bound. Required for some features.")]
      [CommandOption("--https")]
      public bool? EnableHttps { get; init; }

      [Description("Whether to enable Basic Authentication and Windows Authentication for the RAWeb application. These are required for some features and recommended for general security.")]
      [CommandOption("--enable-auth")]
      public bool? EnableAuth { get; init; }

      [Description("The directory into which RAWeb should be installed. If not specified, it will be installed into 'C:\\Program Files\\RAWeb\\<site>\\<path>'.")]
      [CommandOption("--install-dir")]
      public string? InstallDirectory { get; init; }

      [Description("Exit immediately after performing the installation without waiting for user input. This is useful when running the installer in scripts or unattended scenarios.")]
      [CommandOption("--exit-on-complete")]
      public bool ExitOnComplete { get; init; } = false;

      public override ValidationResult Validate()
      {
        var result = SitePathValidator(DestinationPath ?? "/RAWeb", false);
        if (!result.Successful) return result;

        result = ValidateInstallDirectory(InstallDirectory);
        if (!result.Successful) return result;

        return ValidationResult.Success();
      }
    }

    private static ValidationResult SitePathValidator(string path, bool color = true)
    {
      if (string.IsNullOrWhiteSpace(path))
        return ValidationResult.Error($"{(color ? "[red]" : "")}Path cannot be empty{(color ? "[/]" : "")}");
      if (path.Contains(' '))
        return ValidationResult.Error($"{(color ? "[red]" : "")}Path cannot contain spaces{(color ? "[/]" : "")}");
      if (path.Contains('\\'))
        return ValidationResult.Error($"{(color ? "[red]" : "")}Path cannot contain backslashes{(color ? "[/]" : "")}");
      if (!path.StartsWith('/'))
        return ValidationResult.Error($"{(color ? "[red]" : "")}Path must start with a forward slash{(color ? "[/]" : "")}");
      if (path != "/" && path.EndsWith('/'))
        return ValidationResult.Error($"{(color ? "[red]" : "")}Path cannot end with a forward slash unless it is just '/'{(color ? "[/]" : "")}");
      return ValidationResult.Success();
    }

    /// <summary>
    /// Checks that the install directory is a valid Windows path.
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    private static ValidationResult ValidateInstallDirectory(string? path)
    {
      if (!string.IsNullOrEmpty(path))
      {
        try
        {
          var fullPath = Path.GetFullPath(path);
          if (!Path.IsPathRooted(fullPath))
          {
            return ValidationResult.Error("Install directory must be an absolute path.");
          }
        }
        catch (Exception ex)
        {
          return ValidationResult.Error($"Install directory is not a valid path: {ex.Message}");
        }
      }
      return ValidationResult.Success();
    }

    public override int Execute(CommandContext context, Settings settings)
    {
      try
      {
        AnsiConsole.Write(new Align(
          new Rows(
            new Text(""),
            new Markup("[lightgreen on black]+++ RAWeb Setup +++[/]"),
            new Markup($"[italic green]{VersionInfo.GetVersionString()}[/]")
          ),
          HorizontalAlignment.Center,
          VerticalAlignment.Top
        ));
        AnsiConsole.Write("\n");
        AnsiConsole.Write("\n");

        // check if OS is supported
        if (!OSHelpers.IsSupportedOS())
        {
          AnsiConsole.MarkupLine("[red]This operating system is not supported by RAWeb. Exiting.[/]");
          throw new ConsoleExitCode(1);
        }

        // description talking about what this installer does before proceeding
        AnsiConsole.WriteLine("This script will install RAWeb and its dependencies on this device.");
        AnsiConsole.Write("\n");

        // this is the path to the RAWeb code to install, which is bundles
        // with the installer and automatically extracted to a temporary
        // directory by .NET when the installer is run
        string rawebCodeDir = Path.Combine(AppContext.BaseDirectory, "wwwroot");

        // choose between express install and custom install
        var installType = settings.ExpressMode == true ? "express" : settings.ExpressMode == false ? "custom" : "";
        if (string.IsNullOrEmpty(installType))
        {

          // write the table describing the differences between express and custom install
          AnsiConsole.WriteLine("RAWeb can be installed using the express installation (recommended) or custom installation (advanced).");
          var table = new Table();
          table.AddColumn("[bold]Express[/]");
          table.AddColumn("[bold]Custom[/]");
          table.AddRow(
            new Rows(
              new Markup("- [lightgreen]Recommended for most users[/]"),
              new Markup("- Installs RAWeb to the Default Web Site at /RAWeb"),
              new Markup("  - Automatically upgrades/repairs existing installations without prompts"),
              new Markup("- Enables authentication"),
              new Markup("- Enables HTTPS if not already enabled"),
              new Markup("- Creates and installs an SSL certificate if no certificate is found"),
              new Markup("- Installs required IIS features")
            ),
            new Rows(
              new Markup("- [yellow]Recommended for advanced users[/]"),
              new Markup("- Allows installation to a different website and path"),
              new Markup("  - Prompts before overwriting existing installed files"),
              new Markup("- Choose whether to enable authentication"),
              new Markup("- Choose whether to enable HTTPS and create a certificate"),
              new Markup("- Prompts before installaing missing IIS features")
            )
          );
          AnsiConsole.Write(table);
          AnsiConsole.Write("\n");

          // prompt to choose the installation type
          installType = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
              .Title("Choose an installation type:")
              .AddChoices([
                "express",
              "custom"
              ])
              .UseConverter(choice => choice == "express" ? "Express Install (recommended)" : "Custom Install (advanced)")
            );
        }
        AnsiConsole.MarkupLine($"Installing in [yellow]{installType}[/] mode.");
        AnsiConsole.Write("\n");

        // check whether the required IIS components are installed
        List<string> missingIisFeatures = [];
        AnsiConsole.Status()
            .Start("Checking IIS features...", ctx =>
            {
              missingIisFeatures = IISHelpers.GetMissingFeatures();
            });

        // if any are missing, show a prompt to install them
        if (missingIisFeatures.Count > 0)
        {
          AnsiConsole.WriteLine($"This system is missing the required Internet Information Services (IIS) components.");
          var shouldInstallIis = settings.InstallIIS ?? AnsiConsole.Prompt(
            new TextPrompt<bool>($"Do you want to install them now?")
              .AddChoice(true)
              .AddChoice(false)
              .DefaultValue(true)
              .WithConverter(choice => choice ? "y" : "n")
          );
          if (shouldInstallIis)
          {
            try
            {
              AnsiConsole.Progress()
                .Start(ctx =>
                {
                  IISHelpers.InstallFeatures(missingIisFeatures, ctx);
                }
              );
            }
            catch (InvalidOperationException ex)
            {
              AnsiConsole.MarkupLine($"[red]Error installing IIS features: {ex.Message}[/]");
              throw new ConsoleExitCode(1);
            }
          }
          else
          {
            AnsiConsole.MarkupLine("[red]Installation cannot continue without the required IIS features. Exiting.[/]");
            throw new ConsoleExitCode(1);
          }
        }

        // collect information about the system that is required for installation
        string[] siteNames = [];
        AnsiConsole.Status()
            .Start("Collecting system information...", ctx =>
            {
              ctx.Status = "Detecting IIS websites...";
              siteNames = IISHelpers.ListWebSites();
              if (siteNames.Length == 0)
              {
                AnsiConsole.MarkupLine("[red]No IIS websites found. Please create a website in IIS before running this installer.[/]");
                throw new ConsoleExitCode(1);
              }
            }
        );

        // prompt to specify the IIS website
        string siteName = settings.SiteName ?? "Default Web Site";
        if (string.IsNullOrEmpty(settings.SiteName) && installType == "custom" && siteNames.Length > 1)
        {
          siteName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
              .Title("Select an IIS [green]website[/] for RAWeb:")
              .AddChoices(siteNames)
          );
          AnsiConsole.MarkupLine($"Selected [green]website[/]: {siteName}");
        }
        if (!siteNames.Contains(siteName))
        {
          AnsiConsole.MarkupLineInterpolated($"[red]The selected website '{siteName}' does not exist. Exiting.[/]");
          throw new ConsoleExitCode(1);
        }

        // prompt to specify the path in the website
        string sitePath = settings.DestinationPath ?? "/RAWeb";
        if (string.IsNullOrEmpty(settings.DestinationPath) && installType == "custom")
        {
          sitePath = AnsiConsole.Prompt(
            new TextPrompt<string>("Specify the [green]path[/] in the website for RAWeb:")
              .DefaultValue("/RAWeb")
              .Validate(sitePath => SitePathValidator(sitePath))
          );
        }

        string appPoolName = settings.AppPoolName ?? "raweb";
        if (string.IsNullOrEmpty(settings.AppPoolName) && (siteName != "Default Web Site" || sitePath.Trim('/') != "RAWeb"))
        {
          appPoolName += $"_{OSHelpers.Slugify(siteName, '_')}_{OSHelpers.Slugify(sitePath.Trim('/'), '_')}";
        }

        // check if HTTPS is enabled in the selected website
        // and if not, prompt to enable it
        bool httpsEnabled = false;
        AnsiConsole.Status()
          .Start("Checking security...", ctx =>
          {
            httpsEnabled = IISHelpers.IsHttpsEnabled(siteName);
          }
        );
        bool enableHttps = settings.EnableHttps ?? !httpsEnabled;  // always enable HTTPS in express install
        if (settings.EnableHttps == null && !httpsEnabled && installType == "custom")
        {
          enableHttps = AnsiConsole.Prompt(
            new TextPrompt<bool>($"The selected website does not have HTTPS enabled. HTTPS is required use the workspace feature. Do you want to enable HTTPS?")
              .AddChoice(true)
              .AddChoice(false)
              .DefaultValue(true)
              .WithConverter(choice => choice ? "y" : "n")
          );
        }

        // check if there is a a certificate for the HTTPS binding if
        // HTTPS is enabled
        bool IsHttpsCertificateBound = false;
        AnsiConsole.Status()
          .Start("Checking certificate...", ctx =>
          {
            IsHttpsCertificateBound = IISHelpers.IsHttpsCertificateBound(siteName);
          }
        );
        bool shouldCreateSelfSignedCert = settings.EnableHttps ?? ((httpsEnabled || enableHttps) && !IsHttpsCertificateBound); // always create in express install
        if (settings.EnableHttps == null && httpsEnabled && !IsHttpsCertificateBound && installType == "custom")
        {
          shouldCreateSelfSignedCert = AnsiConsole.Prompt(
            new TextPrompt<bool>($"The selected website has HTTPS enabled but does not have a valid certificate bound to the HTTPS binding. An SSL certificate is required use the workspace feature. Do you want to create and bind a self-signed certificate?")
              .AddChoice(true)
              .AddChoice(false)
              .DefaultValue(true)
              .WithConverter(choice => choice ? "y" : "n")
          );
        }

        // sk if we should enable Basic Authentication and Windows Authentication,
        // which are required for the workspace feature and recommended for
        // general security
        bool enableAuth = settings.EnableAuth ?? true; // always enable in express install
        if (settings.EnableAuth == null && installType == "custom")
        {
          enableAuth = AnsiConsole.Prompt(
            new TextPrompt<bool>($"Do you want to enable Basic Authentication and Windows Authentication for RAWeb? These are required for the workspace feature and recommended for general security.")
              .AddChoice(true)
              .AddChoice(false)
              .DefaultValue(true)
              .WithConverter(choice => choice ? "y" : "n")
          );
        }

        // build the installation directory path based on the website name and path
        // using Program Files to minimize the likelyhood of conflicts with existing folders
        string installDir = settings.InstallDirectory ?? $@"C:\Program Files\RAWeb\{siteName}\{sitePath.Trim('/').Replace("/", "_")}";

        // check if RAWeb is already installed in that directory
        if (Directory.Exists(installDir) && !string.IsNullOrEmpty(settings.InstallDirectory))
        {
          var overwrite = AnsiConsole.Prompt(
            new TextPrompt<bool>($"RAWeb is already installed for the specified web site and path. Do you want to update/repair/overwrite it?")
              .AddChoice(true)
              .AddChoice(false)
              .DefaultValue(false)
              .WithConverter(choice => choice ? "y" : "n")
          );
          if (!overwrite)
          {
            AnsiConsole.MarkupLine("[red]Installation cancelled.[/]");
            throw new ConsoleExitCode(1);
          }
        }

        // check if an IIS application already exists and does not point to the same directory
        bool shouldDeleteLegacyInstallLocation = false;
        AnsiConsole.Status()
          .Start("Checking existing IIS applications for conflicts...", ctx =>
          {
            if (IISHelpers.ApplicationExists(siteName, sitePath.Trim('/')))
            {
              var appInfo = IISHelpers.GetApplicationInfo(siteName, sitePath.Trim('/'));

              if (appInfo.PhysicalPath != installDir)
              {
                // if the paths are different because it is an upgrade from an old version
                // of RAWeb (which used to be installed in C:\inetpub\RAWeb), copy the
                // existing data to the new location and continue without an error message
                bool isLegacyInstallLocation = appInfo.PhysicalPath == "C:\\inetpub\\RAWeb" && siteName == "Default Web Site" && sitePath == "/RAWeb";
                if (isLegacyInstallLocation)
                {
                  ctx.Status = "Copying existing RAWeb data to new installation location...";
                  OSHelpers.TakeControl(appInfo.PhysicalPath);
                  {
                    if (!Directory.Exists(installDir))
                    {
                      Directory.CreateDirectory(installDir);
                    }
                    CommandRunner.Run("robocopy", $"\"{appInfo.PhysicalPath}\" \"{installDir}\" /E /COPYALL /DCOPY:T", writeStdout: false, allowedExitCodes: [1, 3]);
                    shouldDeleteLegacyInstallLocation = true;
                  }
                }
                else
                {
                  AnsiConsole.MarkupLineInterpolated($"[red]An IIS application already exists at '{sitePath}' in website '{siteName}' but points to '{appInfo.PhysicalPath}' instead of '{installDir}'. Please choose a different path or remove the existing application in IIS.[/]");
                  throw new ConsoleExitCode(1);
                }
              }
            }
          });

        // copy the RAWeb files to the installation directory
        AnsiConsole.Status()
            .Start("Installing RAWeb files...", ctx =>
            {
              if (Directory.Exists(installDir))
              {
                OSHelpers.TakeControl(installDir);
              }

              // stop the eixsting application pool (if this is an upgrade/reinstall)
              // before we try to remove the application and move files (some files
              // may have locks on them otherwise)
              ctx.Status = "Checking for existing application pool...";
              if (IISHelpers.AppPoolExists(appPoolName) && IISHelpers.IsAppPoolRunning(appPoolName))
              {
                ctx.Status = "Stopping existing application pool...";
                IISHelpers.StopAppPool(appPoolName);
              }

              // if the application already exists in IIS, remove it first
              try
              {
                IISHelpers.RemoveApplication(siteName, sitePath.Trim('/'));
              }
              catch (Exception ex)
              {
                AnsiConsole.MarkupLine("[red]Installation cannot continue.[/]");
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
                throw new ConsoleExitCode(1);
              }

              ctx.Status = "Preserving app data resources...";
              // Preserve the app data folders in the temp directory
              //  - App_Data: where the RAWeb data is stored
              //  - resources: where RAWeb used to read RDP files and icons (now in App_Data\resources)
              //  - multiuser-resources: where RAWeb used to read RDP files and icons and assigned permissions based on folder name (now in App_Data\multiuser-resources)
              // note: robocopy exit code 1 means success
              bool needsRestore = false;
              string appDataDir = Path.Combine(installDir, "App_Data");
              string oldResourcesDir = Path.Combine(installDir, "resources");
              string oldMultiuserResourcesDir = Path.Combine(installDir, "multiuser-resources");
              string tempResourcesDir = Path.Combine(OSHelpers.GetTempFolder(), "raweb_backup_resources");
              if (!Directory.Exists(tempResourcesDir))
              {
                Directory.CreateDirectory(tempResourcesDir);
              }
              if (Directory.Exists(appDataDir))
              {
                needsRestore = true;
                CommandRunner.Run("robocopy", $"\"{appDataDir}\" \"{tempResourcesDir}/App_Data\" /E /COPYALL /DCOPY:T /B", writeStdout: false, allowedExitCodes: [1]);
              }
              if (Directory.Exists(oldResourcesDir))
              {
                needsRestore = true;
                CommandRunner.Run("robocopy", $"\"{oldResourcesDir}\" \"{tempResourcesDir}/resources\" /E /COPYALL /DCOPY:T /B", writeStdout: false, allowedExitCodes: [1]);
              }
              if (Directory.Exists(oldMultiuserResourcesDir))
              {
                needsRestore = true;
                CommandRunner.Run("robocopy", $"\"{oldMultiuserResourcesDir}\" \"{tempResourcesDir}/multiuser-resources\" /E /COPYALL /DCOPY:T /B", writeStdout: false, allowedExitCodes: [1]);
              }

              // If the appSettings are in Web.config, we need to extract them and
              // move them to the App_Data/appSettings.config file. Old versions of RAWeb
              // stored all appSettings in Web.config, but never versions store them
              // in App_Data/appSettings.config and specify configSource="App_Data\appSettings.config".
              string webConfigPath = Path.Combine(installDir, "Web.config");
              if (File.Exists(webConfigPath))
              {
                var webConfig = new System.Xml.XmlDocument();
                webConfig.Load(webConfigPath);

                // only continue if there are children elements (settings to copy)
                var appSettingsNode = webConfig.SelectSingleNode("/configuration/appSettings");
                if (appSettingsNode != null && appSettingsNode.HasChildNodes)
                {
                  // if the appSettings.config file doesn't exist, create it based on
                  // the appSettings in Web.config
                  string appSettingsFilePath = Path.Combine(appDataDir, "appSettings.config");
                  if (!File.Exists(appSettingsFilePath))
                  {
                    ctx.Status = "Extracting appSettings from Web.config...";

                    // create a new XML document with just the appSettings node
                    var appSettingsDoc = new System.Xml.XmlDocument();
                    var declaration = appSettingsDoc.CreateXmlDeclaration("1.0", "utf-8", null);
                    appSettingsDoc.AppendChild(declaration);
                    appSettingsDoc.AppendChild(appSettingsDoc.ImportNode(appSettingsNode, true));

                    // if there is not already an appSettings.config in the temporary app data
                    // backup directory, put the extracted settings there so they can be restored later
                    string backupAppSettingsFilePath = Path.Combine(tempResourcesDir, "App_Data", "appSettings.config");
                    if (!File.Exists(backupAppSettingsFilePath))
                    {
                      Directory.CreateDirectory(Path.GetDirectoryName(backupAppSettingsFilePath)!);
                      appSettingsDoc.Save(backupAppSettingsFilePath);
                    }
                  }
                }
              }

              // remove the existing installed files
              ctx.Status = "Removing existing files...";
              if (Directory.Exists(installDir))
              {
                Directory.Delete(installDir, true);
              }

              // copy the RAWeb files from the installer to the installation directory
              ctx.Status = "Installing files...";
              Directory.CreateDirectory(installDir);
              CommandRunner.Run("robocopy", $"\"{rawebCodeDir}\" \"{installDir}\" /E /COPY:DAT /DCOPY:T", writeStdout: false, allowedExitCodes: [1]);

              // restore the app data folders if they were backed up
              // note: exit code 1 means success
              // note: exit code 3  means that there were already some files in the destination BUT all files were successfully copied, which is expected because RAWeb has default files in App_Data
              if (needsRestore)
              {
                ctx.Status = "Restoring app data resources from previous installation...";
                if (Directory.Exists(Path.Combine(tempResourcesDir, "App_Data")))
                {
                  CommandRunner.Run("robocopy", $"\"{Path.Combine(tempResourcesDir, "App_Data")}\" \"{appDataDir}\" /E /COPYALL /DCOPY:T /B", writeStdout: false, allowedExitCodes: [1, 3]);
                }
                if (Directory.Exists(Path.Combine(tempResourcesDir, "resources")))
                {
                  CommandRunner.Run("robocopy", $"\"{Path.Combine(tempResourcesDir, "resources")}\" \"{Path.Combine(appDataDir, "resources")}\" /E /COPYALL /DCOPY:T /B", writeStdout: false, allowedExitCodes: [1, 3]);
                }
                if (Directory.Exists(Path.Combine(tempResourcesDir, "multiuser-resources")))
                {
                  CommandRunner.Run("robocopy", $"\"{Path.Combine(tempResourcesDir, "multiuser-resources")}\" \"{Path.Combine(appDataDir, "multiuser-resources")}\" /E /COPYALL /DCOPY:T /B", writeStdout: false, allowedExitCodes: [1, 3]);
                }
                Directory.Delete(tempResourcesDir, true);
              }

              AnsiConsole.MarkupLineInterpolated($"[green]RAWeb files copied/installed successfully to {installDir}.[/]");
            }
        );

        AnsiConsole.Status()
            // if it does not exist, create the application pool
            .Start("Configuring IIS...", ctx =>
            {
              ctx.Status = "Creating application pool...";
              if (!IISHelpers.AppPoolExists(appPoolName))
              {
                IISHelpers.CreateAppPool(appPoolName);
                AnsiConsole.MarkupLineInterpolated($"[green]Created application pool '{appPoolName}'.[/]");
              }
              else
              {
                ctx.Status = "Starting app pool...";
                IISHelpers.StartAppPool(appPoolName);
              }

              // create the application in the specified website
              ctx.Status = "Creating IIS application...";
              try
              {
                IISHelpers.CreateApplication(siteName, sitePath.Trim('/'), installDir, appPoolName);
                AnsiConsole.MarkupLineInterpolated($"[green]Created application '{sitePath}' in website '{siteName}'.[/]");
              }
              catch (Exception ex)
              {
                AnsiConsole.MarkupLine("[red]Installation cannot continue.[/]");
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
                throw new ConsoleExitCode(1);
              }


              if (enableAuth)
              {
                ctx.Status = "Enabling authentication...";
                IISHelpers.EnableAuthentication(siteName, sitePath.Trim('/'));
                AnsiConsole.MarkupLine("[green]Enabled Basic Authentication and Windows Authentication.[/]");
              }

              if (enableHttps)
              {
                ctx.Status = "Enabling HTTPS...";
                IISHelpers.EnableHttps(siteName);
                AnsiConsole.MarkupLine("[green]Enabled HTTPS.[/]");
              }

              if (shouldCreateSelfSignedCert)
              {
                ctx.Status = "Creating and binding self-signed SSL certificate...";
                IISHelpers.CreateAndBindSelfSignedCert(siteName);
                AnsiConsole.MarkupLine("[green]Created and bound self-signed SSL certificate.[/]");
              }
            }
        );

        // register an uninstall handler in the registry
        AnsiConsole.Status()
            .Start("Registering uninstall information...", ctx =>
            {
              try
              {
                string installLabel = $"RAWeb {(siteName == "Default Web Site" ? "" : $"– {siteName}")}{((sitePath == "/" || sitePath == "/RAWeb") ? "" : $"– {sitePath.Trim('/')}")}";
                UninstallHelper.RegisterUninstallInformation(installLabel, installDir, siteName, sitePath);
                AnsiConsole.MarkupLine("[green]Registered uninstall information.[/]");
              }
              catch (Exception ex)
              {
                AnsiConsole.MarkupLine("[yellow]Warning: Could not register uninstall information. You will need to manually remove the RAWeb application in IIS and delete the installation directory if you wish to uninstall RAWeb.[/]");
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
              }
            }
        );


        if (httpsEnabled || enableHttps)
        {
          string httpsUrl = "";
          string localHttpsUrl = $"https://{Environment.MachineName}{(sitePath == "/" ? "" : sitePath)}";
          AnsiConsole.Status()
            .Start("Finalizing...", ctx =>
            {
              // save a copy of this installer in the installation directory for future reference
              if (File.Exists(Environment.ProcessPath))
              {
                try
                {
                  string installerPath = Path.Combine(installDir, "install_raweb.exe");
                  File.Copy(Environment.ProcessPath, installerPath, true);

                  // and allow anyone to traverse and execute in the root RAWeb installation directory
                  // so that the installer can be accessed by Windows
                  var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                  var dirInfo = new DirectoryInfo(installDir);
                  var dirSecurity = dirInfo.GetAccessControl();
                  var dirRule = new FileSystemAccessRule(
                      everyone,
                      FileSystemRights.Traverse | FileSystemRights.ReadAndExecute,
                      InheritanceFlags.ObjectInherit,
                      PropagationFlags.NoPropagateInherit,
                      AccessControlType.Allow);
                  dirSecurity.AddAccessRule(dirRule);
                  dirInfo.SetAccessControl(dirSecurity);
                }
                catch (Exception ex)
                {
                  AnsiConsole.MarkupLine("[yellow]Warning: Could not save a copy of the installer to the installation directory.[/]");
                  AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
                }
              }

              // if we migrated from the old install location, delete the old files now
              if (shouldDeleteLegacyInstallLocation && Directory.Exists("C:\\inetpub\\RAWeb"))
              {
                ctx.Status = "Completing migration...";
                try
                {
                  OSHelpers.TakeControl("C:\\inetpub\\RAWeb");
                  Directory.Delete("C:\\inetpub\\RAWeb", true);
                  AnsiConsole.MarkupLine("[green]Removed old installation from C:\\inetpub\\RAWeb.[/]");
                }
                catch (Exception ex)
                {
                  AnsiConsole.MarkupLine("[yellow]Warning: Could not remove old installation files from C:\\inetpub\\RAWeb. You may need to remove them manually.[/]");
                  AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
                }
              }

              // determine the URL to access the site
              ctx.Status = "Finalizing...";
              var foundSiteBindings = IISHelpers.GetHttpsBindings("Default Web Site")
                .Where(b => b.IP == "*")
                .OrderBy(b => string.IsNullOrEmpty(b.Hostname))
                .ThenBy(b => b.Hostname)
                .ToList();
              var binding = foundSiteBindings.Count > 0 ? (string.IsNullOrEmpty(foundSiteBindings[0].Hostname) ? Environment.MachineName : foundSiteBindings[0].Hostname) + (foundSiteBindings[0].Port == 443 ? "" : $":{foundSiteBindings[0].Port}") : "localhost";
              httpsUrl = $"https://{binding}{(sitePath == "/" ? "" : sitePath)}";
            }
          );

          if (enableAuth)
          {
            var panel = new Panel(new Markup($@"
              [bold lightgreen]RAWeb is installed and ready to use![/]

              [steelblue1]Web interface: {httpsUrl}{(localHttpsUrl != httpsUrl ? $"\nLocal access:  {localHttpsUrl}" : "")}
              Workspace URL: {httpsUrl}/webfeed.aspx[/]

              [silver]If you wish to access RAWeb via a different URL/domain, you will need to
              configure the appropriate DNS records and SSL certificate in IIS.[/]

              To learn how to add RemoteApps and Desktops, visit:
              https://github.com/kimmknight/raweb/wiki/Publishing-RemoteApps-and-Desktops
            "));
            panel.Padding = new Padding(0, 0, 2, 0);
            panel.Border = BoxBorder.Rounded;
            AnsiConsole.Write(panel);
          }
          else
          {
            var panel = new Panel(new Markup($@"
              [bold lightgreen]RAWeb is installed and ready to use![/]

              [steelblue1]Web interface: {httpsUrl}
              {(localHttpsUrl != httpsUrl ? $"Local access:  {localHttpsUrl}" : "")}[/]

              [silver]Some features (such as the workspace feature) require authentication and
              are unavailable because you chose to not enable authentication.[/]

              To learn how to add RemoteApps and Desktops, visit:
              https://github.com/kimmknight/raweb/wiki/Publishing-RemoteApps-and-Desktops
            "));
            panel.Padding = new Padding(0, 0, 2, 0);
            panel.Border = BoxBorder.Rounded;
            AnsiConsole.Write(panel);
          }
        }
        else
        {
          string computerName = Environment.MachineName;
          string httpUrl = $"http://{computerName}{(sitePath == "/" ? "" : sitePath)}";

          var panel = new Panel(new Markup($@"
            [bold lightgreen]RAWeb is installed and ready to use![/]

            [steelblue1]Web interface: {httpUrl}[/]

            [silver]Some features (such as the workspace feature) require HTTPS and
            are unavailable because you chose to not enable HTTPS.[/]

            To learn how to add RemoteApps and Desktops, visit:
            https://github.com/kimmknight/raweb/wiki/Publishing-RemoteApps-and-Desktops
          "));
          panel.Padding = new Padding(0, 0, 2, 0);
          panel.Border = BoxBorder.Rounded;
          AnsiConsole.Write(panel);
        }

        // do not auto-close the console window when running by double-clicking
        if (!settings.ExitOnComplete)
        {
          AnsiConsole.Write("\n");
          AnsiConsole.MarkupLine("[grey]Press any key to exit...[/]");
          Console.ReadKey(true);
        }
        else
        {
          AnsiConsole.Write("\n");
          AnsiConsole.MarkupLine("[grey]Installation complete.[/]");
        }

        return 0;
      }
      catch (Exception ex)
      {
        AnsiConsole.Write(new Rule());
        AnsiConsole.MarkupLine("[red]Installation cannot continue due to an unexpected error.[/]");
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
        return 1;
      }
    }
  }

  public static class VersionInfo
  {
    public static string GetVersionString()
    {
      var asm = Assembly.GetExecutingAssembly();
      Version? version = asm.GetName().Version;
      string versionString = version != null ? $"v{version}" : "unknown version";
      if (versionString == "v1.0.0.0") versionString = "dev build";
      return versionString;
    }
  }

  public class ConsoleExitCode : Exception
  {

    public ConsoleExitCode(int exitCode) : base($"Exiting with code {exitCode}")
    {
    }

    public ConsoleExitCode(int exitCode, Exception innerException) : base($"Exiting with code {exitCode}", innerException)
    {
    }
  }

  public class UninstallHelper
  {
    public static void RegisterUninstallInformation(string installLabel, string installDir, string siteName, string sitePath)
    {
      string uninstallId = $@"{siteName}\{sitePath.Trim('/').Replace('/', '\\')}";

      string uninstallKey =
          $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RAWeb_{uninstallId.Replace('\\', '_')}";

      using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(uninstallKey);

      key?.SetValue("DisplayName", installLabel);
      key?.SetValue("InstallLocation", installDir);
      key?.SetValue("Publisher", "RAWeb");
      key?.SetValue("DisplayVersion", VersionInfo.GetVersionString());
      key?.SetValue("DisplayIcon", Path.Combine(installDir, "install_raweb.exe"));
      key?.SetValue("ModifyPath", Path.Combine(installDir, "install_raweb.exe"));
      key?.SetValue("UninstallString",
          $"\"{Path.Combine(installDir, "install_raweb.exe")}\" --uninstall \"{uninstallId}\"");
    }
  }
}
