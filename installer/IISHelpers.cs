using System.Management.Automation;
using System.Security.AccessControl;
using System.Security.Principal;
using Spectre.Console;

namespace RAWebInstaller
{
  public static class IISHelpers
  {
    // IIS features for Server (via Install-WindowsFeature)
    private static readonly string[] ServerFeatures =
    {
        "Web-Server",
        "Web-Asp-Net45",
        "Web-Windows-Auth",
        "Web-Http-Redirect",
        "Web-Mgmt-Console",
        "Web-Basic-Auth"
    };

    // IIS features for Client (via Enable-WindowsOptionalFeature)
    private static readonly string[] ClientFeatures =
    {
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
    };

    /// <summary>
    /// Get list of missing IIS features.
    /// </summary>
    public static List<string> GetMissingFeatures()
    {
      var features = OSHelpers.IsServer() ? ServerFeatures : ClientFeatures;
      var missing = new List<string>();

      foreach (var feature in features)
      {
        if (!IsFeatureInstalled(feature))
          missing.Add(feature);
      }

      return missing;
    }

    /// <summary>
    /// Check if a Windows feature is installed via DISM.
    /// </summary>
    private static bool IsFeatureInstalled(string name)
    {
      var output = CommandRunner.Run("dism.exe", $"/online /Get-FeatureInfo /FeatureName:{name}");
      return output.Contains("State : Enabled", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Install the given features (all missing if none provided).
    /// </summary>
    public static void InstallFeatures(IEnumerable<string>? features = null, ProgressContext? progressContext = null)
    {
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


        // allow read and execute access to bin\SQLite.Interop.dll for the RAWeb application pool identity
        var sqliteInteropPath = Path.Combine(physicalPath, "bin", "SQLite.Interop.dll");
        if (File.Exists(sqliteInteropPath))
        {
          var sqliteInteropFileInfo = new FileInfo(sqliteInteropPath);
          var sqliteInteropAcl = sqliteInteropFileInfo.GetAccessControl();
          var sqliteInteropRule = new FileSystemAccessRule(
            appPoolIdentity,
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow
          );
          sqliteInteropAcl.AddAccessRule(sqliteInteropRule);
          sqliteInteropFileInfo.SetAccessControl(sqliteInteropAcl);
        }

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
