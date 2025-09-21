using AuthUtilities;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.DirectoryServices.AccountManagement;
using System.Web.Script.Serialization;
using System.Web.Script.Services;
using System.Web.Services;
using System.Web.Services.Protocols;

[WebService(Namespace = "https://raweb.app/AuthService")]
[WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
[ScriptService]
public class AuthService : WebService
{
    [WebMethod]
    [ScriptMethod(UseHttpGet = true)]
    public bool CheckLoginPageForAnonymousAuthentication(string loginPageUrl)
    {
        return AuthUtilities.SignOn.CheckLoginPageForAnonymousAuthentication(loginPageUrl);
    }

    [WebMethod]
    [ScriptMethod]
    public string ChangeCredentials(string username, string oldPassword, string newPassword)
    {
        if (System.Configuration.ConfigurationManager.AppSettings["PasswordChange.Enabled"] == "false")
        {
            return new JavaScriptSerializer().Serialize(new { success = false, error = "Password change is disabled." });
        }

        // if the username contains a domain, split it to get the username and domain separately
        string domain = null;
        if (username.Contains("\\"))
        {
            string[] parts = username.Split(new[] { '\\' }, 2);
            domain = parts[0]; // the part before the backslash is the domain
            username = parts[1]; // the part after the backslash is the username
        }
        else
        {
            domain = AuthUtilities.SignOn.GetDomainName();
        }

        if (string.IsNullOrEmpty(username))
        {
            return new JavaScriptSerializer().Serialize(new { success = false, error = "Username must be provided.", domain = domain });
        }

        // attempt to change the credentials for the user
        var result = AuthUtilities.SignOn.ChangeCredentials(username, oldPassword, newPassword, domain);
        var success = result.Item1;
        var errorMessage = result.Item2;

        if (success)
        {
            return new JavaScriptSerializer().Serialize(new { success = true, username = username, domain = domain });
        }
        else
        {
            return new JavaScriptSerializer().Serialize(new { success = false, error = errorMessage, domain = domain });
        }
    }
}
