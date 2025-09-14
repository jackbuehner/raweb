using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RAWebInstaller
{
  partial class OSHelpers
  {
    /// <summary>
    /// Detects if OS is a Windows Server edition.
    /// </summary>
    public static bool IsServer()
    {
      using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
      string productName = key?.GetValue("ProductName")?.ToString() ?? "";
      return productName.Contains("Server", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Detects if OS is a Windows Home edition.
    /// </summary>
    /// <returns></returns>
    public static bool IsHome()
    {
      using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
      string productName = key?.GetValue("ProductName")?.ToString() ?? "";
      return productName.Contains("Home", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the OS is supported by RAWeb.
    /// </summary>
    /// <returns></returns>
    public static bool IsSupportedOS()
    {
      int requiredServerBuild = 14393; // Server 2016
      int requiredClientBuild = 10240; // Windows 10 1507
      int requiredClientHomeBuild = 18362; // Windows 10 19H1

      using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
      string buildStr = key?.GetValue("CurrentBuildNumber")?.ToString() ?? ""; // major build number
      string ubrStr = key?.GetValue("UBR")?.ToString() ?? "0"; // minor build number

      int build = int.TryParse(buildStr, out var b) ? b : 0;
      int ubr = int.TryParse(ubrStr, out var u) ? u : 0;

      if (IsServer())
        return build > requiredServerBuild || (build == requiredServerBuild && ubr >= 0);
      else if (IsHome())
        return build > requiredClientHomeBuild || (build == requiredClientHomeBuild && ubr >= 0);
      else
        return build > requiredClientBuild || (build == requiredClientBuild && ubr >= 0);
    }

    /// <summary>
    /// Creats a temporary folder with an optional prefix.
    /// </summary>
    /// <param name="prefix"></param>
    /// <returns></returns>
    public static string GetTempFolder(string? prefix = null)
    {
      string tempFolder = Path.GetTempPath();
      string tempDirName = (prefix ?? "raweb_") + Guid.NewGuid().ToString("N");
      string fullPath = Path.Combine(tempFolder, tempDirName);

      Directory.CreateDirectory(fullPath);
      return fullPath;
    }

    /// <summary>
    /// Converts a string into a URL-friendly (or Windows-path-friendly) "slug".
    /// </summary>
    /// <param name="input"></param>
    /// <param name="replacement"></param>
    /// <returns></returns>
    public static string Slugify(string input, char replacement = '-')
    {
      if (string.IsNullOrWhiteSpace(input))
        return string.Empty;

      // normalize (decompose accents, etc.)
      string normalized = input.Normalize(NormalizationForm.FormD);

      // strip non-spacing marks (accents)
      var sb = new StringBuilder();
      foreach (var ch in normalized)
      {
        var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
        if (uc != UnicodeCategory.NonSpacingMark)
        {
          sb.Append(ch);
        }
      }

      string noAccents = sb.ToString().Normalize(NormalizationForm.FormC);

      // convert to lowercase
      string lower = noAccents.ToLowerInvariant();

      // replace non-alphanumeric with hyphens
      string hyphenated = SlugifyRegex().Replace(lower, $"{replacement}");

      // trim leading/trailing replacement characters
      string slug = hyphenated.Trim(replacement);

      return slug;
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugifyRegex();
  }
}
