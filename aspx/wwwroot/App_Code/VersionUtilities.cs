using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web;

namespace RAWebServer.Utilities
{
    public static class VersionHelpers
    {
        public static Version ToVersion(string versionString)
        {
            if (string.IsNullOrEmpty(versionString))
            {
                return new Version(1, 0, 0, 0);
            }

            var versionParts = versionString.Split('.');
            if (versionParts.Length >= 4 && 
                int.TryParse(versionParts[0], out var year) &&
                int.TryParse(versionParts[1], out var month) &&
                int.TryParse(versionParts[2], out var day) &&
                int.TryParse(versionParts[3], out var revision))
            {
                return new Version(year, month, day, revision);
            }

            return new Version(1, 0, 0, 0);
        }
    }

    public static class LocalVersions
    {
        public static string GetApplicationVersionString()
        {
            // Get the AssemblyFileVersion from AssemblyInfo.cs
            // (this version is set during the release process for RAWeb)
            var versionAttribute = Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false)
                .OfType<AssemblyFileVersionAttribute>()
                .FirstOrDefault();

            return versionAttribute?.Version ?? "1.0.0.0";
        }

        public static string? GetFrontendVersionString()
        {
            var timestampFilePath = HttpContext.Current.Server.MapPath("~/lib/build.timestamp");
            if (File.Exists(timestampFilePath))
            {
                try
                {
                    var timestamp = File.ReadAllText(timestampFilePath).Trim();
                    return string.IsNullOrEmpty(timestamp) ? null : timestamp;
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return null;
        }
    }
}
