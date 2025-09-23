using System;
using System.Collections.Generic;

namespace RAWebServer.Utilities
{
    public class AliasResolver
    {
        private readonly Dictionary<string, string> _aliasMap;

        public AliasResolver()
        {
            var aliasConfig = System.Configuration.ConfigurationManager.AppSettings["TerminalServerAliases"] ?? string.Empty;
            _aliasMap = ParseConfigString(aliasConfig);
        }

        private Dictionary<string, string> ParseConfigString(string configString)
        {
            // Split the aliases into a map that allows us to find the alias for a given input
            // The input is the key, and the alias is the value
            var aliasMap = new Dictionary<string, string>();
            
            // Format: "INPUT=Alias;INPUT2=Alias with spaces; INPUT3=Alias with spaces ,and commas"
            var aliases = configString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var alias in aliases)
            {
                var aliasPair = alias.Split(new[] { '=' }, 2); // Limit to 2 parts to handle = in values
                if (aliasPair.Length == 2)
                {
                    var input = aliasPair[0].Trim();
                    var aliasValue = aliasPair[1].Trim();
                    
                    if (!string.IsNullOrEmpty(input) && !_aliasMap.ContainsKey(input))
                    {
                        _aliasMap[input] = aliasValue;
                    }
                }
            }
            
            return aliasMap;
        }

        public string Resolve(string name)
        {
            // If the name is in the alias map, return the alias value
            return _aliasMap.TryGetValue(name, out var alias) ? alias : name;
        }
    }
}
