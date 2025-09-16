using System.IO;
using System.Text;

namespace RAWebInstaller;

public class UninstallHelper
{
  public static string CreateUninstallId(string siteName, string sitePath, string appPoolName)
  {
    return $@"{appPoolName}::{siteName}\{sitePath.Trim('/').Replace('/', '\\')}";
  }

  public static (string siteName, string sitePath, string appPoolName, string registryKeyName, string installLocation) ParseUninstallId(string uninstallId)
  {
    var parts = uninstallId.Split("::");
    if (parts.Length != 2)
    {
      throw new ArgumentException("Invalid uninstall ID format.");
    }
    string appPoolName = parts[0];
    var siteParts = parts[1].Split('\\', 2);
    if (siteParts.Length != 2)
    {
      throw new ArgumentException("Invalid uninstall ID format.");
    }
    string siteName = siteParts[0];
    string sitePath = "/" + siteParts[1].Replace('\\', '/');
    string registryKeyName = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RAWeb_{parts[1].Replace('\\', '_')}";
    string installLocation = $@"C:\Program Files\RAWeb\{siteName}\{sitePath.Trim('/').Replace("/", "_")}";
    return (siteName, sitePath, appPoolName, registryKeyName, installLocation);
  }

  public static void RegisterUninstallInformation(string installLabel, string installDir, string siteName, string sitePath, string appPoolName)
  {
    string uninstallId = CreateUninstallId(siteName, sitePath, appPoolName);
    var (_, _, _, registryKeyName, _) = ParseUninstallId(uninstallId);

    using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(registryKeyName);

    key?.SetValue("DisplayName", installLabel);
    key?.SetValue("InstallLocation", installDir);
    key?.SetValue("Publisher", "RAWeb");
    key?.SetValue("DisplayVersion", VersionInfo.GetVersionString());
    key?.SetValue("DisplayIcon", Path.Combine(installDir, "install_raweb.exe"));
    key?.SetValue("ModifyPath", Path.Combine(installDir, "install_raweb.exe"));
    key?.SetValue("UninstallString",
        $"\"{Path.Combine(installDir, "install_raweb.exe")}\" --uninstall \"{uninstallId}\"");
  }

  /// <summary>
  /// Schedules the deletion of the specified executable once
  /// it is no longer running.
  /// 
  /// Specify the raweb installer exe from the install directory
  /// to have it deleted after uninstalling.
  /// </summary>
  /// <param name="exePath"></param>
  public static void ScheduleSelfDelete(string exePath)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(exePath))
        return;

      string exeName = Path.GetFileName(exePath);
      string? dirPath = Path.GetDirectoryName(exePath);

      string batPath = Path.Combine(Path.GetTempPath(), "selfdelete.bat");

      var sb = new StringBuilder();
      sb.AppendLine("@echo off");
      sb.AppendLine(":waitloop");

      // wait for all processes with this name to exit,
      // and then delete the exe
      sb.AppendLine($"tasklist /fi \"imagename eq {exeName}\" | find /i \"{exeName}\" > nul");
      sb.AppendLine("if not errorlevel 1 (");
      sb.AppendLine("    timeout /t 1 /nobreak > nul");
      sb.AppendLine("    goto waitloop");
      sb.AppendLine(")");
      sb.AppendLine($"del \"{exePath}\"");

      // attempt to delete parent directories if they are empty
      if (!string.IsNullOrEmpty(dirPath))
      {
        sb.AppendLine($"set \"curDir={dirPath}\"");
        sb.AppendLine(":cleanuploop");
        sb.AppendLine("if exist \"%curDir%\" (");
        sb.AppendLine("    dir /b \"%curDir%\" | findstr . >nul");
        sb.AppendLine("    if errorlevel 1 (");
        sb.AppendLine("        rd \"%curDir%\"");
        sb.AppendLine("        for %%a in (\"%curDir%\\..\") do set \"curDir=%%~fa\"");
        sb.AppendLine("        goto cleanuploop");
        sb.AppendLine("    )");
        sb.AppendLine(")");
      }

      // delete the batch file itself
      sb.AppendLine("del \"%~f0\"");

      File.WriteAllText(batPath, sb.ToString());

      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
      {
        FileName = batPath,
        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        CreateNoWindow = true
      });
    }
    catch
    {
      throw;
    }
  }

  public static void Uninstall(string uninstallId, bool keepAppData, Func<string, bool, bool?> SetStatus)
  {
    SetStatus("Uninstalling...", true);

    var (siteName, sitePath, appPoolName, registryKeyName, pathToUninstall) = ParseUninstallId(uninstallId);
    OSHelpers.TakeControl(pathToUninstall);

    // confirm that the installation path exists
    if (!Directory.Exists(pathToUninstall))
    {
      throw new FileNotFoundException($"The installation path does not exist:\n{pathToUninstall}");
    }

    SetStatus("Deleting application pool...", true);
    if (IISHelpers.AppPoolExists(appPoolName))
    {
      if (IISHelpers.IsAppPoolRunning(appPoolName))
      {
        IISHelpers.StopAppPool(appPoolName);
      }
      IISHelpers.DeleteAppPool(appPoolName);
    }

    SetStatus("Deleting application...", true);
    IISHelpers.RemoveApplication(siteName, sitePath);

    SetStatus("Removing installation folder...", true);
    string appDataDir = Path.Combine(pathToUninstall, "App_Data");
    foreach (string file in Directory.GetFiles(pathToUninstall))
    {
      if (Path.GetFileName(file).Equals("install_raweb.exe", StringComparison.OrdinalIgnoreCase))
      {
        ScheduleSelfDelete(file);
        continue;
      }
      File.Delete(file);
    }
    foreach (string dir in Directory.GetDirectories(pathToUninstall))
    {
      if (keepAppData && dir.Equals(appDataDir, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }
      Directory.Delete(dir, true);
    }

    // delete the registry uninstall key
    SetStatus("Removing registry entries...", true);
    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(registryKeyName, true);
    if (key != null)
    {
      Microsoft.Win32.Registry.LocalMachine.DeleteSubKey(registryKeyName);
    }

    // finish with a small delay because the uninstall process is
    // so fast that it may look broken without this delay
    SetStatus("Uninstalling...", true);
    SetStatus("", false);
  }
}
