using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace RAWebInstaller
{
  public static class IISHelpers
  {
    // IIS features for Server (via Install-WindowsFeature)
    private static readonly string[] ServerFeatures =
    [
        "Web-Server",
        "Web-Asp-Net45",
        "Web-Windows-Auth",
        "Web-Http-Redirect",
        "Web-Mgmt-Console",
        "Web-Basic-Auth"
    ];

    // IIS features for Client (via Enable-WindowsOptionalFeature)
    private static readonly string[] ClientFeatures =
    [
        "IIS-WebServerRole",
        "IIS-WebServer",
        "IIS-CommonHttpFeatures",
        "IIS-HttpErrors",
        "IIS-HttpRedirect",
        "IIS-ApplicationDevelopment",
        "IIS-Security",
        "IIS-RequestFiltering",
        "IIS-NetFxExtensibility45",
        "IIS-HealthAndDiagnostics",
        "IIS-HttpLogging",
        "IIS-Performance",
        "IIS-WebServerManagementTools",
        "IIS-StaticContent",
        "IIS-DefaultDocument",
        "IIS-DirectoryBrowsing",
        "IIS-ASPNET45",
        "IIS-ISAPIExtensions",
        "IIS-ISAPIFilter",
        "IIS-HttpCompressionStatic",
        "IIS-ManagementConsole",
        "IIS-WindowsAuthentication",
        "NetFx4-AdvSrvs",
        "NetFx4Extended-ASPNET45",
        "IIS-BasicAuthentication"
    ];

    /// <summary>
    /// Gets a list of missing IIS features that are required for RAWeb.
    /// </summary>
    public static List<string> GetMissingFeatures()
    {
      // pick the features to check based on OS type
      var features = OSHelpers.IsServer() ? ServerFeatures : ClientFeatures;

      //. check each feature via DISM and add it to the list if missing
      var missing = new List<string>();
      foreach (var feature in features)
      {
        if (!IsFeatureInstalled(feature))
          missing.Add(feature);
      }

      return missing;
    }

    /// <summary>
    /// Checks if a Windows feature is installed via DISM.
    /// </summary>
    private static bool IsFeatureInstalled(string name)
    {
      var output = CommandRunner.Run("dism.exe", $"/online /Get-FeatureInfo /FeatureName:{name}");
      return output.Contains("State : Enabled", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Installs the specified windows features.
    /// <br /><br />
    /// On Windows Home editions, the WebServer AddOn packages will be installed
    /// from the C:\Windows\servicing\Packages folder first since Home editions don't have
    /// those optional features available by default (even though the addon packages are present).
    /// <br /><br />
    /// On Server editions, Install-WindowsFeature is used.
    /// <br /><br />
    /// On Client editions, Enable-WindowsOptionalFeature is used.
    /// </summary>
    public static void InstallFeatures(IEnumerable<string>? features = null, ProgressContext? progressContext = null)
    {
      // if no features list was provided, check for missing features
      features ??= GetMissingFeatures();


      if (!features.Any())
      {
        AnsiConsole.WriteLine("All required IIS features are already installed.");
        return;
      }

      AnsiConsole.WriteLine($"Installing IIS features: {string.Join(", ", features)}");

      // if Windows Home, we need to manually install the WebServer AddOn Package
      // before installing the features since Home editions don't have these features
      // available by default
      if (OSHelpers.IsHome())
      {
        AnsiConsole.WriteLine("Detected Windows Home edition. Installing WebServer AddOn Package...");

        // it is in %SystemRoot%\servicing\Packages\Microsoft-Windows-WebServer-AddOn-Package~31bf3856ad364e35~amd64~~<version>.cab
        var packagesFolder = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.Windows),
          "servicing",
          "Packages"
        );

        // find the latest version of the package
        // see: https://stackoverflow.com/a/48197086/9861747
        var mumFiles2 = Directory.GetFiles(packagesFolder, "Microsoft-Windows-IIS-WebServer-AddOn-2-Package~31bf3856ad364e35~amd64~~*.mum");
        if (mumFiles2.Length == 0)
        {
          throw new InvalidOperationException("Failed to find WebServer AddOn2 Package CAB file.");
        }
        var latestMum2 = mumFiles2
          .Select(f => new FileInfo(f))
          .OrderByDescending(f => f.CreationTimeUtc)
          .First()
          .FullName;

        var mumFiles1 = Directory.GetFiles(packagesFolder, "Microsoft-Windows-IIS-WebServer-AddOn-2-Package~31bf3856ad364e35~amd64~~*.mum");
        if (mumFiles1.Length == 0)
        {
          throw new InvalidOperationException("Failed to find WebServer AddOn2 Package CAB file.");
        }
        var latestMum1 = mumFiles1
          .Select(f => new FileInfo(f))
          .OrderByDescending(f => f.CreationTimeUtc)
          .First()
          .FullName;

        var task1 = progressContext?.AddTask("Installing WebServer AddOn", true, 100);
        var task2 = progressContext?.AddTask("Installing WebServer AddOn2", true, 100);
        CommandRunner.RunDism($"/Online /NoRestart /Add-Package:\"{latestMum1}\"", task1);
        CommandRunner.RunDism($"/Online /NoRestart /Add-Package:\"{latestMum2}\"", task2);
      }

      if (OSHelpers.IsServer())
      {
        var args = "Install-WindowsFeature -Name " + string.Join(",", features) + " -IncludeManagementTools";
        CommandRunner.RunPS(args, progressContext, (activity) => activity.StartsWith("Install-WindowsFeature") ? "Installing IIS features" : activity);
      }
      else
      {
        var args = "Enable-WindowsOptionalFeature -Online -FeatureName " + string.Join(",", features) + " -All -NoRestart";
        CommandRunner.RunPS(args, progressContext, (activity) => activity.StartsWith("Enable-WindowsOptionalFeature") ? "Installing IIS features" : activity);
      }
    }

    /// <summary>
    /// Lists all IIS websites.
    /// </summary>
    /// <returns></returns>
    public static string[] ListWebSites()
    {
      var output = CommandRunner.RunPS(@"
        Import-Module WebAdministration
        Get-Website | Select-Object -ExpandProperty Name
      ");
      return output.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Checks if a website with the given name exists.
    /// </summary>
    /// <param name="siteNameToCheck"></param>
    /// <returns></returns>
    public static bool SiteExists(string siteNameToCheck)
    {
      var sites = ListWebSites();
      return sites.Contains(siteNameToCheck, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes an existing application from the given site.
    /// </summary>
    /// <param name="siteName"></param>
    /// <param name="appName"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static Exception? RemoveApplication(string siteName, string appName)
    {
      if (!SiteExists(siteName))
      {
        throw new InvalidOperationException($"Website '{siteName}' does not exist.");
      }

      try
      {
        CommandRunner.RunPS($@"
          Import-Module WebAdministration
          Remove-WebApplication -Site ""{siteName}"" -Name ""{appName}""
        ");

        // old versions used to create a virtual directory, so
        // we need to remove it if it exists
        try
        {
          CommandRunner.RunPS($@"
            Import-Module WebAdministration
            Remove -Item -Path ""IIS:\Sites\{siteName}\{appName}"" -Recurse -Force
          ");
        }
        catch { }

        return null;
      }
      catch (Exception ex)
      {
        if (ex.Message.Contains("Cannot find path") && ex.Message.Contains("because it does not exist"))
        {
          // if the application doesn't exist, that's fine
          return null;
        }

        AnsiConsole.MarkupLine($"[red]Failed to remove existing application '{appName}' from site '{siteName}':[/]");
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
        return ex;
      }
    }

    /// <summary>
    /// Checks if an application with the given name exists in the specified site.
    /// </summary>
    /// <param name="siteName"></param>
    /// <param name="appName"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static bool ApplicationExists(string siteName, string appName)
    {
      if (!SiteExists(siteName))
      {
        throw new InvalidOperationException($"Website '{siteName}' does not exist.");
      }

      try
      {
        var output = CommandRunner.RunPS($@"
          Import-Module WebAdministration
          $app = Get-WebApplication -Site ""{siteName}"" -Name ""{appName}""
          if ($null -ne $app) {{
              $true
          }} else {{
              $false
          }}
        ");

        return output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Gets information about an application in the specified site.
    /// <br /><br />
    /// Currently. only the physical path and application pool name are returned.
    /// </summary>
    /// <param name="siteName"></param>
    /// <param name="appName"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static ApplicationInfo GetApplicationInfo(string siteName, string appName)
    {
      if (!SiteExists(siteName))
      {
        throw new InvalidOperationException($"Website '{siteName}' does not exist.");
      }

      try
      {
        var output = CommandRunner.RunPS($@"
          Import-Module WebAdministration
          $app = Get-WebApplication -Site ""{siteName}"" -Name ""{appName}""
          if ($null -ne $app) {{
              $app.PhysicalPath
              $app.ApplicationPool
          }}
        ");

        var lines = output
          .Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
          .Select(l => l.Trim())
          .ToArray();

        if (lines.Length >= 2)
        {
          var physicalPath = lines[0];
          var appPool = lines[1];
          return new ApplicationInfo(physicalPath, appPool);
        }
        else
        {
          throw new InvalidOperationException($"Application '{appName}' in site '{siteName}' does not exist.");
        }
      }
      catch (Exception ex)
      {
        AnsiConsole.MarkupLine($"[red]Failed to get information for application '{appName}' in site '{siteName}':[/]");
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
        throw;
      }
    }

    public class ApplicationInfo(string PhysicalPath, string AppPoolName)
    {
      public string PhysicalPath { get; } = PhysicalPath;
      public string AppPoolName { get; } = AppPoolName;
    }

    /// <summary>
    /// Stops the specified application pool.
    /// <br /><br />
    /// This will unload any worker processes (w3wp.exe) that may lock files,
    /// such as SQLite.Interop.dll.
    /// </summary>
    /// <param name="appPoolName"></param>
    /// <param name="gracefulTimeoutSeconds">Time to wait for a graceful shutdown before forcing the shutdown (default: 5 seconds). Specify 0 seconds to disable force shutdown of worker processes.</param>
    /// <exception cref="InvalidOperationException"></exception>
    public static void StopAppPool(string appPoolName, int gracefulTimeoutSeconds = 5)
    {
      if (!AppPoolExists(appPoolName))
      {
        throw new InvalidOperationException(
            $"Application pool '{appPoolName}' does not exist."
        );
      }

      try
      {
        // ask IIS tp stop gracefully
        CommandRunner.RunPS($@"
          Import-Module WebAdministration
          Stop-WebAppPool -Name ""{appPoolName}""
        ");

        // give IIS a change to shut down the worker processes
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(gracefulTimeoutSeconds))
        {
          // chick if there are still worker processes for this app pool
          if (!GetWorkerProcesses()
              .Any(p => string.Equals(
                  p.AppPool, appPoolName, StringComparison.OrdinalIgnoreCase)))
          {
            return; // graceful stop succeeded
          }
          Thread.Sleep(500); // check again in
        }

        // return early if we are not allowed to force kill
        if (gracefulTimeoutSeconds <= 0)
        {
          return;
        }

        // if the worker processes are still running, kill them
        foreach (var (pid, pool) in GetWorkerProcesses())
        {
          if (string.Equals(pool, appPoolName,
                            StringComparison.OrdinalIgnoreCase))
          {
            try
            {
              var proc = Process.GetProcessById(pid);
              proc.Kill();
              proc.WaitForExit(2000);
            }
            catch (Exception killEx)
            {
              AnsiConsole.MarkupLine(
                  $"[yellow]Warning: failed to kill w3wp for pool '{pool}' (PID {pid}): {killEx.Message}[/]");
            }
          }
        }
      }
      catch (Exception ex)
      {
        AnsiConsole.MarkupLine($"[red]Failed to stop app pool '{appPoolName}':[/]");
        AnsiConsole.WriteException(
            ex,
            ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks
        );
        throw;
      }


    }

    /// <summary>
    /// Gets a list of all running IIS worker processes (w3wp.exe) and their associated application pools.
    /// </summary>
    /// <returns></returns>
    static IEnumerable<(int Pid, string AppPool)> GetWorkerProcesses()
    {
      var searcher = new ManagementObjectSearcher(
          "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'w3wp.exe'");

      foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
      {
        var pid = (uint)obj["ProcessId"];
        var cmd = obj["CommandLine"]?.ToString() ?? "";

        var match = Regex.Match(cmd, @"-ap\s+(""([^""]+)""|(\S+))");
        if (match.Success)
        {
          yield return ((int)pid,
              match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value);
        }
      }
    }

    /// <summary>
    /// Starts the specified application pool.
    /// </summary>
    /// <param name="appPoolName"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public static void StartAppPool(string appPoolName)
    {
      if (!AppPoolExists(appPoolName))
      {
        throw new InvalidOperationException(
            $"Application pool '{appPoolName}' does not exist."
        );
      }

      try
      {
        CommandRunner.RunPS($@"
          Import-Module WebAdministration
          Start-WebAppPool -Name ""{appPoolName}""
        ");
      }
      catch (Exception ex)
      {
        AnsiConsole.MarkupLine($"[red]Failed to start app pool '{appPoolName}':[/]");
        AnsiConsole.WriteException(
            ex,
            ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks
        );
        throw;
      }
    }


    /// <summary>
    /// Creates a new application in the specified site.
    /// <br /><br />
    /// Also sets up the necessary file system permissions for the application pool identity
    /// and configures authentication for the /auth directory.
    /// </summary>
    /// <param name="siteName"></param>
    /// <param name="appName"></param>
    /// <param name="physicalPath"></param>
    /// <param name="appPoolName"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static Exception? CreateApplication(string siteName, string appName, string physicalPath, string appPoolName)
    {
      if (!SiteExists(siteName))
      {
        throw new InvalidOperationException($"Website '{siteName}' does not exist.");
      }

      appName = appName.Trim('/');
      if (string.IsNullOrWhiteSpace(appName))
      {
        throw new InvalidOperationException($"Application name '{appName}' is invalid.");
      }

      if (!Directory.Exists(physicalPath))
      {
        throw new InvalidOperationException($"Physical path '{physicalPath}' does not exist.");
      }

      if (!AppPoolExists(appPoolName))
      {
        throw new InvalidOperationException($"Application pool '{appPoolName}' does not exist.");
      }

      try
      {
        CommandRunner.RunPS($@"
          Import-Module WebAdministration
          New-WebApplication -Site ""{siteName}"" -Name ""{appName}"" -PhysicalPath ""{physicalPath}"" -ApplicationPool ""{appPoolName}""
        ");

        // disable permission inheritance on the physic path
        var rawebDirInfo = new DirectoryInfo(physicalPath);
        var rawebAcl = rawebDirInfo.GetAccessControl();
        rawebAcl.SetAccessRuleProtection(true, false);
        rawebDirInfo.SetAccessControl(rawebAcl);

        // allow full control for SYSTEM and Administrators
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var localAdminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var systemRule = new FileSystemAccessRule(
          systemSid,
          FileSystemRights.FullControl,
          InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
          PropagationFlags.None,
          AccessControlType.Allow
        );
        var adminRule = new FileSystemAccessRule(
          localAdminSid,
          FileSystemRights.FullControl,
          InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
          PropagationFlags.None,
          AccessControlType.Allow
        );
        rawebAcl.AddAccessRule(systemRule);
        rawebAcl.AddAccessRule(adminRule);

        // grant read access to the app pool identity (IIS AppPool\appPoolName)
        var appPoolIdentity = new NTAccount($"IIS AppPool\\{appPoolName}");
        var appPoolRule = new FileSystemAccessRule(
          appPoolIdentity,
          FileSystemRights.Read,
          InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
          PropagationFlags.None,
          AccessControlType.Allow
        );
        rawebAcl.AddAccessRule(appPoolRule);

        // additionally grant write access to the App_Data folder, which is required for the policies web editor
        var appDataPath = Path.Combine(physicalPath, "App_Data");
        var appDataDirInfo = new DirectoryInfo(appDataPath);
        var appDataAcl = appDataDirInfo.GetAccessControl();
        var appDataRule = new FileSystemAccessRule(
          appPoolIdentity,
          FileSystemRights.Write | FileSystemRights.Modify,
          InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
          PropagationFlags.None,
          AccessControlType.Allow
        );
        appDataAcl.AddAccessRule(appDataRule);

        // allow Administrators read access to App_Data so they can open the folder
        // and choose to grant themselves write access in order to add RDP files
        var adminAppDataRule = new FileSystemAccessRule(
          localAdminSid,
          FileSystemRights.Read,
          InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
          PropagationFlags.None,
          AccessControlType.Allow
        );
        appDataAcl.AddAccessRule(adminAppDataRule);

        // allow read access for Everyone to the auth directory (login fails otherwise)
        var authPath = Path.Combine(physicalPath, "auth");
        var authDirInfo = new DirectoryInfo(authPath);
        var authAcl = authDirInfo.GetAccessControl();
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var everyoneRule = new FileSystemAccessRule(
          everyoneSid,
          FileSystemRights.Read,
          InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
          PropagationFlags.None,
          AccessControlType.Allow
        );
        authAcl.AddAccessRule(everyoneRule);

        // allow read access for the Users group for App_Data\resources since all users should have access to the resources by default
        var resourcesPath = Path.Combine(appDataPath, "resources");
        var resourcesDirInfo = new DirectoryInfo(resourcesPath);
        var resourcesAcl = resourcesDirInfo.GetAccessControl();
        var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var usersRule = new FileSystemAccessRule(
          usersSid,
          FileSystemRights.Read,
          InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
          PropagationFlags.None,
          AccessControlType.Allow
        );
        resourcesAcl.AddAccessRule(usersRule);

        // apply read and execute access on all DLLs for the RAWeb app pool identity
        var binPath = Path.Combine(physicalPath, "bin");
        var binDirInfo = new DirectoryInfo(binPath);
        var binAcl = binDirInfo.GetAccessControl();
        var binRule = new FileSystemAccessRule(
            appPoolIdentity,
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow
        );
        binAcl.AddAccessRule(binRule);
        binDirInfo.SetAccessControl(binAcl);


        rawebDirInfo.SetAccessControl(rawebAcl);
        appDataDirInfo.SetAccessControl(appDataAcl);
        authDirInfo.SetAccessControl(authAcl);
        resourcesDirInfo.SetAccessControl(resourcesAcl);

        // configure anonymous authentication to use the RAWeb application pool identity
        CommandRunner.RunPS($@"
          Import-Module WebAdministration
          Set-WebConfigurationProperty -Filter /system.webServer/security/authentication/anonymousAuthentication -Location ""{siteName}/{appName}"" -Name ""enabled"" -Value ""True""
          Set-WebConfigurationProperty -Filter /system.webServer/security/authentication/anonymousAuthentication -Location ""{siteName}/{appName}"" -Name ""userName"" -Value """"
        ");

        return null;
      }
      catch (Exception ex)
      {
        AnsiConsole.MarkupLine($"[red]Failed to create application '{appName}' in site '{siteName}':[/]");
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
        return ex;
      }
    }

    /// <summary>
    /// Checks if an application pool with the given name exists.
    /// </summary>
    /// <param name="appPoolName"></param>
    /// <returns></returns>
    public static bool AppPoolExists(string appPoolName)
    {
      try
      {
        CommandRunner.RunPS($@"
          Import-Module WebAdministration
          Get-WebAppPoolState -Name ""{appPoolName}""
        ");
        return true;
      }
      catch (Exception ex)
      {
        if (ex.Message.Contains("Cannot find path") && ex.Message.Contains("because it does not exist"))
        {
          return false;
        }

        throw;
      }
    }

    /// <summary>
    /// Creates a new application pool with the given name.
    /// <br /><br />
    /// The application pool will be configured to use the ApplicationPoolIdentity.
    /// </summary>
    /// <param name="appPoolName"></param>
    /// <returns></returns>
    public static Exception? CreateAppPool(string appPoolName)
    {
      try
      {
        CommandRunner.RunPS($@"
          Import-Module WebAdministration
          New-WebAppPool -Name ""{appPoolName}""
          Set-WebConfigurationProperty -Filter /system.applicationHost/applicationPools/add[@name='{appPoolName}'] -Name processModel.identityType -Value ApplicationPoolIdentity # auth as ApplicationPoolIdentity (IIS AppPool\raweb)
          Set-WebConfigurationProperty -Filter /system.applicationHost/applicationPools/add[@name='{appPoolName}'] -Name managedRuntimeVersion -Value 'v4.0'
        ");

        return null;
      }
      catch (Exception ex)
      {
        AnsiConsole.MarkupLine($"[red]Failed to create application pool '{appPoolName}':[/]");
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
        return ex;
      }
    }

    /// <summary>
    /// Checks if HTTPS is enabled for the given site (i.e. if there is an HTTPS binding).
    /// </summary>
    /// <param name="siteName"></param>
    /// <returns></returns>
    public static bool IsHttpsEnabled(string siteName)
    {
      try
      {
        var output = CommandRunner.RunPS($@"
          Import-Module WebAdministration
          $binding = Get-WebBinding -Name ""{siteName}"" -Protocol https -Port 443
          $null -ne $binding
        ");
        return output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Checks if an HTTPS certificate is bound to the given site (i.e. if the HTTPS binding has a certificate).
    /// </summary>
    /// <param name="siteName"></param>
    /// <returns></returns>
    public static bool IsHttpsCertificateBound(string siteName)
    {
      try
      {
        var output = CommandRunner.RunPS($@"
          Import-Module WebAdministration
          $binding = Get-WebBinding -Name ""{siteName}"" -Protocol https -Port 443
          $is_httpsenabled = $null -ne $binding

          if ($is_httpsenabled) {{
              $cert = $binding.certificateHash
              $is_certificate = $null -ne $cert
          }}
          else {{
              $is_certificate = $false
          }}

          $is_certificate
        ");
        return output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Enables Windows and Basic authentication and disables anonymous authentication
    /// for the /auth directory of the specified application in the given site.
    /// </summary>
    /// <param name="siteName"></param>
    /// <param name="appName"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static Exception? EnableAuthentication(string siteName, string appName)
    {
      if (!SiteExists(siteName))
      {
        throw new InvalidOperationException($"Website '{siteName}' does not exist.");
      }

      try
      {
        CommandRunner.RunPS($@"
          Import-Module WebAdministration
          Set-WebConfigurationProperty -Filter /system.webServer/security/authentication/anonymousAuthentication -Location ""{siteName}/{appName}/auth"" -Name ""enabled"" -Value ""False""
          Set-WebConfigurationProperty -Filter /system.webServer/security/authentication/windowsAuthentication -Location ""{siteName}/{appName}/auth"" -Name ""enabled"" -Value ""True""
          Set-WebConfigurationProperty -Filter /system.webServer/security/authentication/basicAuthentication -Location ""{siteName}/{appName}/auth"" -Name ""enabled"" -Value ""True""
        ");

        return null;
      }
      catch (Exception ex)
      {
        AnsiConsole.MarkupLine($"[red]Failed to enable authentication for application '{appName}' in site '{siteName}':[/]");
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
        return ex;
      }
    }

    /// <summary>
    /// Enables HTTPS for the given site (i.e. creates an HTTPS binding on port 443 if it doesn't exist).
    /// <br /><br />
    /// Does not create or bind a certificate, just enables the HTTPS binding.
    /// </summary>
    /// <param name="siteName"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static Exception? EnableHttps(string siteName)
    {
      if (!SiteExists(siteName))
      {
        throw new InvalidOperationException($"Website '{siteName}' does not exist.");
      }

      try
      {
        CommandRunner.RunPS($@"
          Import-Module WebAdministration
          $binding = Get-WebBinding -Name ""{siteName}"" -Protocol https -Port 443
          if ($null -eq $binding) {{
              New-WebBinding -Name ""{siteName}"" -Protocol https -Port 443
          }}
        ");

        return null;
      }
      catch (Exception ex)
      {
        AnsiConsole.MarkupLine($"[red]Failed to enable HTTPS for site '{siteName}':[/]");
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
        return ex;
      }
    }

    /// <summary>
    /// Creates a new self-signed certificate and binds it to the given site.
    /// <br /><br />
    /// The certificate will be created in the LocalMachine\My store with the computer's
    /// hostname as the DNS name.
    /// </summary>
    /// <param name="siteName"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static Exception? CreateAndBindSelfSignedCert(string siteName)
    {
      if (!SiteExists(siteName))
      {
        throw new InvalidOperationException($"Website '{siteName}' does not exist.");
      }

      try
      {
        CommandRunner.RunPS($@"
          Import-Module WebAdministration
          $cert = New-SelfSignedCertificate -DnsName $env:COMPUTERNAME -CertStoreLocation ""Cert:\LocalMachine\My""
          $thumbprint = $cert.Thumbprint

          (Get-WebBinding -Name ""{siteName}"" -Port 443 -Protocol ""https"").AddSslCertificate($thumbprint, ""my"") | Out-Null
        ");

        return null;
      }
      catch (Exception ex)
      {
        AnsiConsole.MarkupLine($"[red]Failed to create and bind self-signed certificate for site '{siteName}':[/]");
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
        return ex;
      }
    }


    /// <summary>
    /// Gets a list of all HTTPS bindings for the given site.
    /// <br /><br />
    /// Each binding contains the IP, port and hostname.
    /// <br /><br />
    /// This is useful when attempting to construct the URL to access the site.
    /// </summary>
    /// <param name="siteName"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static List<Binding> GetHttpsBindings(string siteName)
    {
      if (!SiteExists(siteName))
      {
        throw new InvalidOperationException($"Website '{siteName}' does not exist.");
      }

      try
      {
        var output = CommandRunner.RunPS($@"
            Import-Module WebAdministration
            $bindings = Get-WebBinding -Name ""{siteName}"" -Protocol https
            if ($bindings) {{
                $bindings | ForEach-Object {{ $_.bindingInformation }}
            }}
        ");

        var lines = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Binding.Parse(line.Trim()))
            .ToList();

        return lines;
      }
      catch (Exception ex)
      {
        AnsiConsole.MarkupLine($"[red]Failed to get HTTPS bindings for site '{siteName}':[/]");
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
        return new List<Binding>();
      }
    }

    public class Binding(string IP, int Port, string Hostname)
    {
      public override string ToString() => $"{IP}:{Port}:{Hostname}";

      public string IP { get; } = IP;
      public int Port { get; } = Port;
      public string Hostname { get; } = Hostname;

      public static Binding Parse(string bindingInfo)
      {
        var parts = bindingInfo.Split(':');
        var ip = parts.Length > 0 ? parts[0] : "*";
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 443;
        var host = parts.Length > 2 ? parts[2] : "";
        return new Binding(ip, port, host);
      }

    }
  }
}
