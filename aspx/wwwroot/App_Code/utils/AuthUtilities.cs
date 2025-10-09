using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Principal;
using System.Text;
using System.Web;
using System.Web.Security;
using RAWebServer.Cache;

namespace RAWebServer.Utilities {
    public class AuthCookieHandler {
        public static string cookieName;

        public AuthCookieHandler(string name = ".ASPXAUTH") {
            cookieName = name;
        }

        /// <summary>
        /// Creates an encrypted forms authentication ticket for the user included in the
        /// request info. This use is populated by IIS when authentication is used.
        /// <br /><br />
        /// If override the user, use the <c>CreateAuthTicket(UserInformation)</c>,
        /// <c>CreateAuthTicket(IntPtr)</c>, or <c>CreateAuthTicket(WindowsIdentity)</c> overloads
        /// instead.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="mayWrite"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static AuthTicketResult CreateAuthTicket(HttpRequest request, bool mayWrite = false) {
            if (request == null) {
                throw new ArgumentNullException("request", "HttpRequest cannot be null.");
            }

            return CreateAuthTicket(request.LogonUserIdentity, mayWrite);
        }

        /// <summary>
        /// Creates an encrypted forms authentication ticket for the specified user logon token.
        /// The user logon token should represent a logged-on user (e.g., from LogonUser).
        /// </summary>
        /// <param name="userLogonToken"></param>
        /// <param name="mayWrite"></param>
        /// <returns></returns>
        public static AuthTicketResult CreateAuthTicket(IntPtr userLogonToken, bool mayWrite = false) {
            var foundGroupIds = new List<string>();
            using (var logonUserIdentity = new WindowsIdentity(userLogonToken)) {
                return CreateAuthTicket(logonUserIdentity, mayWrite);
            }
        }

        /// <summary>
        /// Creates an encrypted forms authentication ticket for the specified Windows identity.
        /// The Windows identity should reprent a logged-on user.
        /// <br /><br />
        /// If you have a user logon token (e.g., from LogonUser), use the CreateAuthTicket(IntPtr) overload instead.
        /// </summary>
        /// <param name="logonUserIdentity"></param>
        /// <param name="mayWrite"></param>
        /// <returns></returns>
        public static AuthTicketResult CreateAuthTicket(WindowsIdentity logonUserIdentity, bool mayWrite = false) {
            var userSid = logonUserIdentity.User.Value;
            var username = logonUserIdentity.Name.Split('\\').Last(); // get the username from the LogonUserIdentity, which is in DOMAIN\username format
            var domain = logonUserIdentity.Name.Contains("\\") ? logonUserIdentity.Name.Split('\\')[0] : Environment.MachineName; // get the domain from the username, or use machine name if no domain

            // attempt to get the user's full/display name
            string fullName = null;
            try {
                fullName = NetUserInformation.GetFullName(domain == Environment.MachineName ? null : domain, username);
            }
            catch (Exception) { }

            // parse the groups from the user identity
            var groupInformation = new List<GroupInformation>();
            foreach (var group in logonUserIdentity.Groups) {
                var groupSid = group.Value;
                var displayName = groupSid;

                // attempt to translate the SID to an NTAccount (e.g., DOMAIN\GroupName)
                try {
                    var ntAccount = (NTAccount)group.Translate(typeof(NTAccount));
                    displayName = ntAccount.Value.Split('\\').Last(); // get the group name from the NTAccount
                }
                catch (IdentityNotMappedException) {
                    // identity cannot be mapped - use SID as display name
                }
                catch (SystemException) {
                    // cannot communicate with the domain controller - use SID as display name
                }

                groupInformation.Add(new GroupInformation(displayName, groupSid));
            }

            // LogonUser always adds the User group, so we need to exclude it
            // and then check for the membership separately
            groupInformation.RemoveAll(g => g.Sid == "S-1-5-32-545");

            // check the local machine for whether the user is a member
            // of the Users group and add it if needed
            if (NetUserInformation.IsUserLocalUser(userSid)) {
                groupInformation.Add(new GroupInformation("S-1-5-32-545"));
            }

            // check the local machine for whether the user is a local administrator
            // and add the local Administrators group if needed
            if (!groupInformation.Any(g => g.Sid == "S-1-5-32-544")) {
                if (NetUserInformation.IsUserLocalAdministrator(userSid)) {
                    groupInformation.Add(new GroupInformation("S-1-5-32-544"));
                }
            }

            // exclude any excluded special identity groups
            foreach (var excludedGroup in UserInformation.ExcludedSpecialIdentityGroups) {
                groupInformation.RemoveAll(g => g.Sid == excludedGroup.Sid);
            }

            var userInfo = new UserInformation(userSid, username, domain, fullName, groupInformation.ToArray());
            return CreateAuthTicket(userInfo, mayWrite);
        }

        /// <summary>
        /// Creates an encrypted forms authentication ticket for the specified user.
        /// </summary>
        /// <param name="userInfo"></param>
        /// <param name="mayWrite"></param>
        /// <returns></returns>
        public static AuthTicketResult CreateAuthTicket(UserInformation userInfo, bool mayWrite = false) {
            var version = 1;
            var issueDate = DateTime.Now;
            var expirationDate = DateTime.Now.AddMinutes(1);
            var isPersistent = false;
            var userData = "";

            var WRITE_SESSION_TIMEOUT_MINUTES = 5;
            if (mayWrite) {
                userData = "may-write=1"; // indicate that this is a session where the user is allowed to write changes
                expirationDate = DateTime.Now.AddMinutes(WRITE_SESSION_TIMEOUT_MINUTES); // limit the session to a short time period
            }

            if (System.Configuration.ConfigurationManager.AppSettings["UserCache.Enabled"] == "true") {
                var dbHelper = new UserCacheDatabaseHelper();
                dbHelper.StoreUser(userInfo.Sid, userInfo.Username, userInfo.Domain, userInfo.FullName, userInfo.Groups.ToList());
            }

            var tkt = new FormsAuthenticationTicket(version, userInfo.Domain + "\\" + userInfo.Username, issueDate, expirationDate, isPersistent, userData);
            var token = FormsAuthentication.Encrypt(tkt);
            return new AuthTicketResult(token, expirationDate);
        }

        public class AuthTicketResult {
            public string Token { get; private set; }
            public DateTime ExpirationDate { get; private set; }

            public AuthTicketResult(string token, DateTime expirationDate) {
                Token = token;
                ExpirationDate = expirationDate;
            }
        }

        public HttpCookie CreateAuthTicketCookie(string encryptedToken) {
            var combinedCookieNameAndValue = cookieName + "=" + encryptedToken;

            // the cookie name+value length must be less than 4096 bytes
            if (combinedCookieNameAndValue.Length >= 4096) {
                throw new Exception("Cookie name and value length exceeds 4096 bytes.");
            }

            // create the cookie
            var authCookie = new HttpCookie(cookieName, encryptedToken) {
                Path = VirtualPathUtility.ToAbsolute("~/") // set the path to the application root
            };
            return authCookie;
        }

        public FormsAuthenticationTicket GetAuthTicket(HttpRequest request) {
            if (request == null) {
                throw new ArgumentNullException("request", "HttpRequest cannot be null.");
            }

            // if there is an Authorization header, always use it instead of the cookie
            // (even if the Authorization header contains invalid authorization)
            var shouldUseBearerAuth = false;
            if (request.Headers != null && request.Headers["Authorization"] != null) {
                var authHeader = request.Headers["Authorization"];
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
                    shouldUseBearerAuth = true;
                }
            }

            if (!shouldUseBearerAuth && request.Cookies == null) {
                throw new ArgumentNullException("request", "Cookies collection or Authorization header (Bearer) must be present.");
            }

            string token;
            if (shouldUseBearerAuth) {
                // get the token from the Authorization header
                var authHeader = request.Headers["Authorization"];
                token = authHeader.Substring("Bearer ".Length).Trim();
            }
            else if (request.Cookies[cookieName] != null) {
                var cookieValue = request.Cookies[cookieName].Value;
                token = cookieValue;
            }
            else {
                // if no cookie or bearer token is present, there is no token
                return null;
            }

            // decrypt the value and return it
            try {
                // decrypt may throw an exception if the cookieValue or authHeader is invalid
                var authTicket = FormsAuthentication.Decrypt(token);
                return authTicket;
            }
            catch {
                return null;
            }
        }

        public UserInformation GetUserInformation(HttpRequest request) {
            if (request == null) {
                throw new ArgumentNullException("request", "HttpRequest cannot be null.");
            }

            var authTicket = GetAuthTicket(request);
            if (authTicket == null) {
                return null;
            }

            // end if the auth ticket is expired
            if (authTicket.Expiration < DateTime.Now) {
                return null;
            }

            // use a request-based cache to avoid repeated lookups during the same request
            var context = request.RequestContext.HttpContext;
            const string contextKey = "UserInformation";

            // if the user information is already in the request context, return it
            if (context.Items[contextKey] is UserInformation) {
                return context.Items[contextKey] as UserInformation;
            }

            // get the username and domain from the auth ticket (we used DOMAIN\username for ticket name)
            var parts = authTicket.Name.Split('\\');
            var username = parts.Length > 1 ? parts[1] : parts[0]; // the part after the backslash is the username
            var domain = parts.Length > 1 ? parts[0] : Environment.MachineName; // the part before the backslash is the domain, or use machine name if no domain

            // throw an exception if username or domain is null or empty
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(domain)) {
                throw new ArgumentException("Username or domain cannot be null or empty.");
            }

            // if the account is the anonymous account, return those details
            if ((domain == "NT AUTHORITY" && username == "IUSR") || (domain == "IIS APPPOOL" && username == "raweb") || (domain == "RAWEB" && username == "anonymous")) {
                var userInfo = new UserInformation("S-1-4-447-1", username, domain, "Anonymous User", UserInformation.IncludedSpecialIdentityGroups);
                context.Items[contextKey] = userInfo; // store in request context
                return userInfo;
            }

            // if the user cache is enabled, attempt to get the user from the cache first,
            // but only if the user information is not stale
            if (System.Configuration.ConfigurationManager.AppSettings["UserCache.Enabled"] == "true") {
                var dbHelper = new UserCacheDatabaseHelper();
                var cachedUserInfo = dbHelper.GetUser(null, username, domain);

                if (cachedUserInfo != null) {
                    // store in request context
                    context.Items[contextKey] = cachedUserInfo;

                    // return the cached user information immediately
                    return cachedUserInfo;
                }
            }

            // otherwise, attempt to get the latest user information using principal contexts,
            // but fall back to the cache with no staleness restrictions if an error occurs
            // TODO: if we ever enable the user cache by default, we should not bypass the stale check and instead suggest that those who need something similar set their UserCache.StaleWhileRevalidate value to a massive number
            try {
                var userInfo = GetUserInformationFromPrincipalContext(username, domain);

                // store the user information in the request context
                context.Items[contextKey] = userInfo;

                return userInfo;
            }
            catch (Exception) {
                // fall back to the cache if an error occurs and the user cache is enabled
                // (e.g., the principal context for the domain cannot currently be accessed)
                if (System.Configuration.ConfigurationManager.AppSettings["UserCache.Enabled"] == "true") {
                    var dbHelper = new UserCacheDatabaseHelper();
                    var cachedUserInfo = dbHelper.GetUser(null, username, domain, 315576000); // 10 years max age to effectively disable staleness
                    context.Items[contextKey] = cachedUserInfo; // store in request context
                    return cachedUserInfo;
                }
                return null;
            }
        }

        public UserInformation GetUserInformationFromPrincipalContext(string username, string domain) {
            // get the principal context for the domain or machine
            var domainIsMachine = string.IsNullOrEmpty(domain) || domain.Trim() == Environment.MachineName;
            PrincipalContext principalContext;
            if (domainIsMachine) {
                // if the domain is empty or the same as the machine name, use the machine context
                domain = Environment.MachineName;
                principalContext = new PrincipalContext(ContextType.Machine);
            }
            else {
                // if the domain is specified, use the domain context
                principalContext = new PrincipalContext(ContextType.Domain, domain);
            }

            // get the user principal (PrincipalSearcher is much faster than UserPrincipal.FindByIdentity)
            var user = new UserPrincipal(principalContext);
            user.SamAccountName = username;
            var userSearcher = new PrincipalSearcher(user);
            user = userSearcher.FindOne() as UserPrincipal;

            // if the user is not found, return null early
            if (user == null) {
                return null;
            }

            // get the user SID
            var userSid = user.Sid.ToString();

            // get the full name of the user
            var fullName = user.DisplayName ?? user.Name ?? user.SamAccountName;

            // get all groups of which the user is a member (checks all domains and local machine groups)
            var groupInformation = UserInformation.GetAllUserGroups(user);

            // clean up
            if (principalContext != null) {
                try {
                    principalContext.Dispose();
                }
                catch (Exception ex) {
                    // log the exception if needed
                    System.Diagnostics.Debug.WriteLine("Error disposing PrincipalContext: " + ex.Message);
                }
            }
            if (user != null) {
                try {
                    user.Dispose();
                }
                catch (Exception ex) {
                    // log the exception if needed
                    System.Diagnostics.Debug.WriteLine("Error disposing UserPrincipal: " + ex.Message);
                }
            }

            var userInfo = new UserInformation(
                userSid,
                username,
                domain,
                fullName,
                groupInformation.ToArray()
            );

            // update the cache with the user information
            if (System.Configuration.ConfigurationManager.AppSettings["UserCache.Enabled"] == "true") {
                var dbHelper = new UserCacheDatabaseHelper();
                dbHelper.StoreUser(userInfo);
            }

            return userInfo;
        }

        public UserInformation GetUserInformationSafe(HttpRequest request) {
            try {
                return GetUserInformation(request);
            }
            catch (Exception) {
                return null; // return null if an error occurs
            }
        }
    }

    public class UserInformation {
        public string Username { get; private set; }
        public string Domain { get; private set; }
        public string Sid { get; private set; }
        public string FullName { get; set; }
        public GroupInformation[] Groups { get; set; }
        /// <summary>
        /// Indicates whether the user is allowed to perform actions that
        /// entail changing configurations, adding/editing/deleting resources,
        /// or other actions that modify the state of the system.
        /// </summary>
        public bool HasWriteAccess { get; private set; }
        public bool IsAnonymousUser {
            get {
                return this.Sid == "S-1-4-447-1";
            }
        }
        public bool IsRemoteDesktopUser {
            get {
                return this.Groups.Any(g => g.Sid == "S-1-5-32-555");
            }
        }
        public bool IsLocalAdministrator {
            get {
                return this.Groups.Any(g => g.Sid == "S-1-5-32-544");
            }
        }

        public UserInformation(string sid, string username, string domain, string fullName, GroupInformation[] groups, bool mayWrite = false) {
            Sid = sid;
            Username = username;
            Domain = domain;

            if (string.IsNullOrEmpty(fullName)) {
                FullName = username; // default to username if full name is not provided
            }
            else {
                FullName = fullName;
            }

            Groups = groups;

            HasWriteAccess = mayWrite;
        }

        /// <summary>
        /// Gets the local group memberships for a user.
        /// </summary>
        /// <param name="de">The directory entry for the user.</param>
        /// <param name="userSid">The sid of the user in string form.</param>
        /// <param name="userGroupsSids">The optional array of string sids representing groups that the user belongs to. Use this when searching local groups after finding domain groups.</param>
        /// <returns>A list of group information</returns>
        /// <exception cref="ArgumentNullException"></exception>
        private static List<GroupInformation> GetLocalGroupMemberships(DirectoryEntry de, string userSid, string[] userGroupsSids = null) {
            if (de == null) {
                throw new ArgumentNullException("de", "DirectoryEntry cannot be null.");
            }
            if (string.IsNullOrEmpty(userSid)) {
                throw new ArgumentNullException("userSid", "User SID cannot be null or empty.");
            }
            if (userGroupsSids == null) {
                userGroupsSids = new string[0];
            }

            // seach the local machine for groups that contain the user's SID
            var localGroups = new List<GroupInformation>();
            var localMachinePath = "WinNT://" + Environment.MachineName + ",computer";
            try {
                using (var machineEntry = new DirectoryEntry(localMachinePath)) {
                    foreach (DirectoryEntry machineChildEntry in machineEntry.Children) {
                        // skip entries that are not groups
                        if (machineChildEntry.SchemaClassName != "Group") {
                            continue;
                        }

                        // skip if there are no members of the group
                        var members = machineChildEntry.Invoke("Members") as System.Collections.IEnumerable;
                        if (members == null || !members.Cast<object>().Any()) {
                            continue;
                        }

                        // get the sid of the group
                        var groupSidBytes = (byte[])machineChildEntry.Properties["objectSid"].Value;
                        var groupSid = new SecurityIdentifier(groupSidBytes, 0).ToString();

                        // check the SIDs of each member in the group (this gets user and group SIDs)
                        foreach (var member in members) {
                            using (var memberEntry = new DirectoryEntry(member)) {

                                var sidBytes = (byte[])memberEntry.Properties["objectSid"].Value;
                                var groupMemberSid = new SecurityIdentifier(sidBytes, 0).ToString();

                                // add the group to the list if:
                                // - the group member SID matches the user's SID (the user is a member of the group)
                                // - the group member SID is in the user's groups SIDs (the user is a member of a group that is a member of this group)
                                if (groupMemberSid == userSid || userGroupsSids.Contains(groupMemberSid)) {
                                    localGroups.Add(new GroupInformation(machineChildEntry.Name, groupSid));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception) {
            }

            // ensure that the local groups include the special identity groups that Windows typically adds
            foreach (var specialGroup in IncludedSpecialIdentityGroups) {
                if (!localGroups.Any(g => g.Sid == specialGroup.Sid)) {
                    localGroups.Add(specialGroup);
                }
            }

            // ensure that excluded special identity groups are not included in the local groups
            foreach (var excludedGroup in ExcludedSpecialIdentityGroups) {
                localGroups.RemoveAll(g => g.Sid == excludedGroup.Sid);
            }

            return localGroups;
        }

        // see: https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/understand-special-identities-groups#everyone
        public static GroupInformation[] IncludedSpecialIdentityGroups = new GroupInformation[] {
            new GroupInformation("Everyone", "S-1-1-0"), // all authenticated and guest users are part of Everyone
            new GroupInformation("Authenticated Users", "S-1-5-11"), // all authenticated users are implicitly a member of this group
        };

        // see: https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/understand-special-identities-groups
        // and https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/understand-security-identifiers
        public static GroupInformation[] ExcludedSpecialIdentityGroups = new GroupInformation[] {
            new GroupInformation("Anonymous Logon", "S-1-5-7"),
            new GroupInformation("Attested Key Property", "S-1-18-6"),
            new GroupInformation("Authentication Authority Asserted Identity", "S-1-18-1"),
            new GroupInformation("Batch", "S-1-5-3"),
            new GroupInformation("Console Logon", "S-1-2-1"),
            new GroupInformation("Creator Group", "S-1-3-1"),
            new GroupInformation("Creator Owner", "S-1-3-0"),
            new GroupInformation("Dialup", "S-1-5-1"),
            new GroupInformation("Digest Authentication", "S-1-5-64-21"),
            new GroupInformation("Enterprise Domain Controllers", "S-1-5-9"),
            // new GroupInformation("Enterprise Read-only Domain Controllers", "S-1-5-21-<RootDomain>-498"),
            new GroupInformation("Fresh Public Key Identity", "S-1-18-3"),
            new GroupInformation("Interactive", "S-1-5-4"),
            new GroupInformation("IUSR", "S-1-5-17"),
            new GroupInformation("Key Trust", "S-1-18-4"),
            new GroupInformation("Local Service", "S-1-5-19"),
            new GroupInformation("LocalSystem", "S-1-5-18"),
            new GroupInformation("Local account", "S-1-5-113"),
            new GroupInformation("Local account and member of Administrators group", "S-1-5-114"),
            new GroupInformation("MFA Key Property", "S-1-18-5"),
            new GroupInformation("Network", "S-1-5-2"),
            new GroupInformation("Network Service", "S-1-5-20"),
            new GroupInformation("NTLM Authentication", "S-1-5-64-10"),
            new GroupInformation("Other Organization", "S-1-5-1000"),
            new GroupInformation("Owner Rights", "S-1-3-4"),
            new GroupInformation("Principal Self", "S-1-5-10"),
            new GroupInformation("Proxy", "S-1-5-8"),
            // new GroupInformation("Read-only Domain Controllers", "S-1-5-21-<domain>-521"),
            new GroupInformation("Remote Interactive Logon", "S-1-5-14"),
            new GroupInformation("Restricted", "S-1-5-12"),
            new GroupInformation("SChannel Authentication", "S-1-5-64-14"),
            new GroupInformation("Service", "S-1-5-6"),
            new GroupInformation("Service Asserted Identity", "S-1-18-2"),
            new GroupInformation("Terminal Server User", "S-1-5-13"),
            new GroupInformation("This Organization", "S-1-5-15"),
            new GroupInformation("Window Manager\\Window Manager Group", "S-1-5-90")
        };

        /// <summary>
        /// Searches a directory entry that represents a domain for groups that match the specified filter.
        /// </summary>
        /// <param name="searchRoot">A DirectoryEntry. It must be for a domain.</param>
        /// <param name="filter">A filter that can be used with a DirectorySearcher.</param>
        /// <returns>A list of found groups.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        private static List<GroupInformation> FindDomainGroups(DirectoryEntry searchRoot, string filter) {
            if (searchRoot == null) {
                throw new ArgumentNullException("searchRoot", "DirectoryEntry cannot be null.");
            }
            if (string.IsNullOrEmpty(filter)) {
                throw new ArgumentNullException("filter", "Filter cannot be null or empty.");
            }

            var propertiesToLoad = new[] { "msDS-PrincipalName", "objectSid", "distinguishedName" };

            var foundGroups = new List<GroupInformation>();
            var directorySearcher = new DirectorySearcher(searchRoot, filter, propertiesToLoad);

            using (var results = directorySearcher.FindAll()) {
                foreach (SearchResult result in results) {
                    // get the group name and SID from the properties
                    var groupName = result.Properties["msDS-PrincipalName"][0].ToString();
                    var groupDistinguishedName = result.Properties["distinguishedName"][0].ToString();
                    var groupSidBytes = (byte[])result.Properties["objectSid"][0];
                    var groupSid = new SecurityIdentifier(groupSidBytes, 0).ToString();

                    // add the group to the found groups
                    foundGroups.Add(new GroupInformation(groupName, groupSid, groupDistinguishedName));
                }
            }

            return foundGroups;
        }

        /// <summary>
        /// Searches a directory entry that represents a domain for groups that match the specified filter.
        /// <br />
        /// Exceptions are caught and an empty list is returned instead of throwing an exception.
        /// </summary>
        /// <param name="searchRoot">A DirectoryEntry. It must be for a domain.</param>
        /// <param name="filter">A filter that can be used with a DirectorySearcher.</param>
        /// <returns>A list of found groups.</returns>
        private static List<GroupInformation> FindDomainGroupsSafe(DirectoryEntry searchRoot, string filter) {
            try {
                return FindDomainGroups(searchRoot, filter);
            }
            catch (Exception) {
                return new List<GroupInformation>();
            }
        }

        /// <summary>
        /// Gets all groups for a user across all domains in the forest.
        /// <br />
        /// This method find all domains in the forest of the user's domain and then
        /// searches for groups where the user is a direct member or an indirect member via group membership.
        /// <br />
        /// This method also searches for group membership in externally trusted domains found in
        /// the Foreign Security Principals container.
        /// <br />
        /// Each domain in the forest (or each trusted foreign domain) is wrapped in a try-catch block
        /// to ensure that if one domain fails to be queried, it does not affect the others.
        /// <br />
        /// Queries will fail if the application pool is not running with credentials that are accepted
        /// by the domain.
        /// </summary>
        /// <param name="de"></param>
        /// <param name="userSid"></param>
        /// <returns>A list of found groups.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        private static List<GroupInformation> GetUserGroupsForAllDomains(DirectoryEntry de, string userSid) {
            if (de == null) {
                throw new ArgumentNullException("de", "DirectoryEntry cannot be null.");
            }
            if (string.IsNullOrEmpty(userSid)) {
                throw new ArgumentNullException("userSid", "User SID cannot be null or empty.");
            }

            // track the found groups (may include netbios names in the group names)
            var foundGroups = new List<GroupInformation>();
            var searchedDomains = new HashSet<string>();

            // ensure the properties we want are loaded
            de.RefreshCache(new[] { "canonicalName", "objectSid", "distinguishedName", "primaryGroupID" });
            var userCanonicalName = de.Properties["canonicalName"].Value as string;
            var userDistinguishedName = de.Properties["distinguishedName"].Value as string;
            var primaryGroupId = (int)de.Properties["primaryGroupID"].Value;

            // extract the user's domain from the canonicalName property and use it to get the domain and forest
            if (string.IsNullOrEmpty(userCanonicalName)) {
                throw new Exception("Canonical name is not available for the user's directory entry.");
            }
            var domainName = userCanonicalName.Split('/')[0];
            var domainDirectoryContext = new DirectoryContext(DirectoryContextType.Domain, domainName);
            var userDomain = System.DirectoryServices.ActiveDirectory.Domain.GetDomain(domainDirectoryContext);
            var forest = userDomain.Forest;

            // this may fail if the raweb application pool is not running with credentials
            // that can query the domains in the forest
            try {
                using (var searchRoot = new DirectoryEntry("LDAP://" + userDomain.Name)) {

                    // construct the user's primary group SID
                    searchRoot.RefreshCache(new[] { "objectSid" });
                    var objectSidBytes = (byte[])searchRoot.Properties["objectSid"].Value;
                    var domainSid = new SecurityIdentifier(objectSidBytes, 0).ToString();
                    var userPrimaryGroupSid = domainSid + "-" + primaryGroupId;

                    // search for the primary group using the primary group SID
                    // and add it to the found groups
                    var filter = "(&(objectClass=group)(objectSid=" + userPrimaryGroupSid + "))";
                    var found = FindDomainGroupsSafe(searchRoot, filter);
                    foundGroups.AddRange(found);

                    // search domains in the user's domain forest
                    forest.Domains
                        .Cast<Domain>()
                        .Where(domain => !searchedDomains.Contains(domain.Name))
                        .ToList()
                        .ForEach(domain => {
                            // add this domain to the searched domains so we do not search it again
                            searchedDomains.Add(domain.Name);

                            // search the directory for groups where the user is a direct member
                            // and add them to the found groups
                            filter = "(&(objectClass=group)(member=" + userDistinguishedName + "))";
                            found = FindDomainGroupsSafe(searchRoot, filter);
                            foundGroups.AddRange(found);

                            // search the directory for groups where the user is an indirect member via group membership
                            // and add them to the found groups
                            var groupDistinguishedNames = foundGroups
                                .Select(g => g.EscapedDN)
                                .Where(dn => !string.IsNullOrEmpty(dn))
                                .ToArray();
                            if (groupDistinguishedNames.Length > 0) {
                                filter = "(&(objectClass=group)(|" + string.Join("", groupDistinguishedNames.Select(dn => "(member=" + dn + ")")) + "))";
                                found = FindDomainGroupsSafe(searchRoot, filter);
                                foundGroups.AddRange(found);
                            }
                        });
                }
            }
            catch (Exception) {
            }

            // also search any externally trusted domains from Foreign Security Principals
            var trusts = forest.GetAllTrustRelationships();
            var groupsFoundInTrusts = trusts
                .Cast<TrustRelationshipInformation>()
                .Where(trust => !searchedDomains.Contains(trust.TargetName)) // do not search domains we have already searched
                .Where(trust => trust.TrustDirection != TrustDirection.Outbound) // ignore outbound trusts
                .ToList()
                .SelectMany(trust => {
                    var foundGroupsInTrust = new List<GroupInformation>();

                    // this will fail if the raweb application pool is not running with credentials
                    // that have access to this domain from the foreign security principals
                    try {
                        using (var searchRoot = new DirectoryEntry("LDAP://" + trust.TargetName)) {
                            // construct the distinguished name for the foreign security principal
                            searchRoot.RefreshCache(new[] { "distinguishedName" });
                            var domainDistinguishedName = searchRoot.Properties["distinguishedName"].Value as string;
                            var foreignSecurityPrincipalDistinguishedName = "CN=" + userSid + ",CN=ForeignSecurityPrincipals," + domainDistinguishedName;

                            // search for groups where the user is a direct member
                            var filter = "(&(objectClass=group)(member=" + foreignSecurityPrincipalDistinguishedName + "))";
                            var found = FindDomainGroupsSafe(searchRoot, filter);
                            foundGroupsInTrust.AddRange(found);

                            // search  for groups where the user is an indirect member via group membership
                            var groupDistinguishedNames = foundGroups
                                .Select(g => g.EscapedDN)
                                .Where(dn => !string.IsNullOrEmpty(dn))
                                .ToArray();
                            if (groupDistinguishedNames.Length > 0) {
                                filter = "(&(objectClass=group)(|" + string.Join("", groupDistinguishedNames.Select(dn => "(member=" + dn + ")")) + "))";
                                found = FindDomainGroupsSafe(searchRoot, filter);
                                foundGroupsInTrust.AddRange(found);
                            }
                        }
                    }
                    catch (Exception) {
                    }

                    return foundGroupsInTrust;
                })
                .ToList();

            foundGroups = foundGroups
                // add groups found in foreign security principals
                .Concat(groupsFoundInTrusts)
                // remove the domain names from the group names
                .Select(g => {
                    // remove the domain name from the group name if it exists
                    var groupName = g.Name;
                    if (groupName.Contains("\\")) {
                        groupName = groupName.Split('\\').Last();
                    }
                    return new GroupInformation(groupName, g.Sid, g.DN);
                })
                .ToList();

            return foundGroups;
        }

        /// <summary>
        /// A helper method to get all groups for a user.
        /// <br />
        /// If the user is from the local machine, it will enumerate local machine groups.
        /// <br />
        /// If the user is from a domain, it will search all domains in the forest for groups
        /// where the user is a direct member or an indirect member via group membership.
        /// See GetUserGroupsForAllDomains for more details.
        /// </summary>
        /// <param name="user">A user principal</param>
        /// <returns>A list of found groups.</returns>
        public static List<GroupInformation> GetAllUserGroups(UserPrincipal user) {
            var de = user.GetUnderlyingObject() as DirectoryEntry;
            var userSid = user.Sid.ToString();

            // if the user is from the local machine instead of a domain, we need
            // to enumerate the local machine groups to find which groups contain
            // the user's SID
            var isLocalMachineUser = de.Path.StartsWith("WinNT://", StringComparison.OrdinalIgnoreCase);
            if (isLocalMachineUser) {
                var localGroups = GetLocalGroupMemberships(de, userSid);
                return localGroups;
            }

            // otherwise, we need to get the user's groups from the domain
            // and then also find local machine groups the contain the user's sid OR the user's groups SIDs
            var foundDomainGroups = GetUserGroupsForAllDomains(de, userSid);
            var foundLocalGroups = GetLocalGroupMemberships(de, userSid, foundDomainGroups.Select(g => g.Sid).ToArray());
            var allGroups = foundDomainGroups.Concat(foundLocalGroups);

            // remove duplicate groups by SID
            allGroups = allGroups.GroupBy(g => g.Sid).Select(g => g.First());

            return allGroups.ToList();
        }

        public override string ToString() {
            var str = new StringBuilder();

            str.Append("Username: ").Append(Username).Append("\n");

            str.Append("Domain: ").Append(Domain).Append("\n");

            str.Append("Groups: ");
            if (Groups != null && Groups.Length > 0) {
                foreach (var group in Groups) {
                    str.Append("\n").Append("  - ").Append(group.Name).Append(" (").Append(group.Sid).Append(")");
                }
            }
            else {
                str.Append("None");
            }

            return str.ToString();
        }
    }

    public class GroupInformation {
        public string Name { get; set; }
        public string Sid { get; set; }
        public string DN { get; set; }

        /// <summary>
        /// Escaped distinguished name for LDAP filters.
        /// </summary>
        public string EscapedDN {
            get {
                if (string.IsNullOrEmpty(DN)) {
                    return null;
                }

                // escape the distinguished name for LDAP filters
                var sb = new StringBuilder();
                foreach (var c in DN) {
                    switch (c) {
                        case '*': sb.Append(@"\2A"); break;
                        case '(': sb.Append(@"\28"); break;
                        case ')': sb.Append(@"\29"); break;
                        case '\\': sb.Append(@"\5C"); break;
                        default: sb.Append(c); break;
                    }
                }
                return sb.ToString();
            }
        }

        public GroupInformation(string name, string sid, string dn = null) {
            Name = name;
            Sid = sid;
            DN = dn;
        }

        public GroupInformation(GroupPrincipal groupPrincipal) {
            if (groupPrincipal == null) {
                throw new ArgumentNullException("groupPrincipal", "GroupPrincipal cannot be null.");
            }

            Name = groupPrincipal.Name;
            Sid = groupPrincipal.Sid.ToString();
            DN = groupPrincipal.DistinguishedName;
        }

        public GroupInformation(string sid) {
            Name = ResolveLocalizedGroupName(sid);
            Sid = sid;
            DN = null;
        }

        public static string ResolveLocalizedGroupName(SecurityIdentifier sid) {
            try {
                var account = (NTAccount)sid.Translate(typeof(NTAccount));
                var groupName = account.Value.Split('\\')[1]; // remove the machine name
                return groupName;
            }
            catch (Exception) {
                return sid.ToString(); // return the SID string if the name cannot be resolved
            }
        }

        public static string ResolveLocalizedGroupName(string sidString) {
            var sid = new SecurityIdentifier(sidString);
            return ResolveLocalizedGroupName(sid);
        }
    }

    public class ValidateCredentialsException : AuthenticationException {
        public ValidateCredentialsException(string message) : base(message) { }
    }

    public static class SignOn {
        /// <summary>
        /// Gets the current machine's domain. If the machine is not part of a domain, it returns the machine name.
        /// If the domain cannot be accessed, likely due to the machine either not being part of the domain
        /// or the network connection between the machine and the domain controller being unavailable, the machine
        /// name will be used instead.
        /// </summary>
        /// <returns>The domain name</returns>
        public static string GetDomainName() {
            try {
                return Domain.GetComputerDomain().Name;
            }
            catch (ActiveDirectoryObjectNotFoundException) {
                // if the domain cannot be found, attempt to get the domain from the registry
                var regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters");
                if (regKey == null) {
                    // if the registry key is not found, return the machine name
                    return Environment.MachineName;
                }

                using (regKey) {
                    // this either contains the machine's domain name or is empty if the machine is not part of a domain
                    var foundDomain = regKey.GetValue("Domain") as string;
                    if (string.IsNullOrEmpty(foundDomain)) {
                        // if the domain is not found, return the machine name
                        return Environment.MachineName;
                    }
                    return foundDomain;
                }
            }
            catch (Exception) {
                return Environment.MachineName;
            }
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool LogonUser(
            string lpszUsername,
            string lpszDomain,
            string lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            out IntPtr phToken
        );

        public const int LOGON32_LOGON_INTERACTIVE = 2;
        public const int LOGON32_PROVIDER_DEFAULT = 0;

        // see https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--1300-1699-
        public const int ERROR_LOGON_FAILURE = 1326; // incorrect username or password
        public const int ERROR_ACCOUNT_RESTRICTION = 1327; // account restrictions, such as logon hours or workstation restrictions, are preventing this user from logging on
        public const int ERROR_INVALID_LOGON_HOURS = 1328; // the user is not allowed to log on at this time
        public const int ERROR_INVALID_WORKSTATION = 1329; // the user is not allowed to log on to this workstation
        public const int ERROR_PASSWORD_EXPIRED = 1330; // the user's password has expired
        public const int ERROR_ACCOUNT_DISABLED = 1331; // the user account is disabled
        public const int ERROR_PASSWORD_MUST_CHANGE = 1907; // the user account password must change before signing in

        /// <summary>
        /// A safe handle for a user token obtained from LogonUser.
        /// <br /><br />
        /// Close the handle by calling Dispose() or using a using statement.
        /// <br />
        /// This ensures that the handle is properly closed when no longer needed.
        /// </summary>
        public sealed class UserToken : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid {
            private UserToken() : base(true) { }

            internal UserToken(IntPtr handle) : base(true) {
                SetHandle(handle);
            }

            protected override bool ReleaseHandle() {
                return CloseHandle(handle);
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CloseHandle(IntPtr hObject);
        }

        /// <summary>
        /// Validates the user credentials against the local machine or domain.
        /// <br />
        /// If the domain is not specified or is the same as the machine name, it validates against the local machine.
        /// If the domain is specified, it validates against the domain.
        /// </summary>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <param name="domain"></param>
        /// <returns>A pointer to the user from the credentials.</returns>
        public static UserToken ValidateCredentials(string username, string password, string domain) {
            if (string.IsNullOrEmpty(domain) || domain.Trim() == Environment.MachineName) {
                domain = "."; // for local machine
            }

            // if the user cache is not enabled, require the principal context to be accessible
            // because the GetUserInformation method will attempt to access the principal context
            // to get the user information, which will fail if the domain cannot be accessed
            // and the user cache is not enabled
            if (System.Configuration.ConfigurationManager.AppSettings["UserCache.Enabled"] != "true") {
                try {
                    // attempt to get the principal context for the domain or machine
                    PrincipalContext principalContext;
                    if (domain == ".") {
                        principalContext = new PrincipalContext(ContextType.Machine);
                    }
                    else {
                        principalContext = new PrincipalContext(ContextType.Domain, domain, null, ContextOptions.Negotiate | ContextOptions.Signing | ContextOptions.Sealing);
                    }

                    // dispose of the principal context once we have verified it can be accessed
                    principalContext.Dispose();
                }
                catch (Exception) {
                    throw new ValidateCredentialsException("login.server.unfoundDomain");
                }
            }

            IntPtr userToken;
            if (LogonUser(username, domain, password, LOGON32_LOGON_INTERACTIVE, LOGON32_PROVIDER_DEFAULT, out userToken)) {
                return new UserToken(userToken);
            }
            else {
                var errorCode = Marshal.GetLastWin32Error();
                switch (errorCode) {
                    case ERROR_LOGON_FAILURE:

                        // check if the domain can be resolved
                        if (domain != ".") {
                            try {
                                Domain.GetDomain(new DirectoryContext(DirectoryContextType.Domain, domain));
                            }
                            catch (ActiveDirectoryObjectNotFoundException) {
                                throw new ValidateCredentialsException("login.server.unfoundDomain");
                            }
                        }

                        throw new ValidateCredentialsException("login.incorrectUsernameOrPassword");
                    case ERROR_ACCOUNT_RESTRICTION:
                        throw new ValidateCredentialsException("login.server.accountRestrictionError");
                    case ERROR_INVALID_LOGON_HOURS:
                        throw new ValidateCredentialsException("login.server.invalidLogonHoursError");
                    case ERROR_INVALID_WORKSTATION:
                        throw new ValidateCredentialsException("login.server.invalidWorkstationError");
                    case ERROR_PASSWORD_EXPIRED:
                        throw new ValidateCredentialsException("login.server.passwordExpiredError");
                    case ERROR_ACCOUNT_DISABLED:
                        throw new ValidateCredentialsException("login.server.accountDisabledError");
                    case ERROR_PASSWORD_MUST_CHANGE:
                        throw new ValidateCredentialsException("login.server.passwordMustChange");
                    default:
                        throw new ValidateCredentialsException("An unknown error occurred: " + errorCode);
                }
            }
        }
    }

    public class NetUserInformation {
        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserGetInfo(
            string servername,
            string username,
            int level,
            out IntPtr bufptr);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetLocalGroupGetMembers(
            string serverName,
            string localGroupName,
            int level,
            out IntPtr bufptr,
            int prefmaxlen,
            out int entriesRead,
            out int totalEntries,
            IntPtr resumeHandle
        );

        [DllImport("Netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr Buffer);

        // USER_INFO_2 has the "usri2_full_name" field
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct USER_INFO_2 {
            public string usri2_name;
            public string usri2_password;
            public int usri2_password_age;
            public int usri2_priv;
            public string usri2_home_dir;
            public string usri2_comment;
            public int usri2_flags;
            public string usri2_script_path;
            public int usri2_auth_flags;
            public string usri2_full_name; // ← Display/Full name
            public string usri2_usr_comment;
            public string usri2_parms;
            public string usri2_workstations;
            public int usri2_last_logon;
            public int usri2_last_logoff;
            public int usri2_acct_expires;
            public int usri2_max_storage;
            public int usri2_units_per_week;
            public IntPtr usri2_logon_hours;
            public int usri2_bad_pw_count;
            public int usri2_num_logons;
            public string usri2_logon_server;
            public int usri2_country_code;
            public int usri2_code_page;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct LOCALGROUP_MEMBERS_INFO_2 {
            public IntPtr sid;
            public int sidUsage;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string domainAndName;
        }

#pragma warning disable IDE1006
        private static readonly int NERR_Success = 0;
#pragma warning restore IDE1006

        public static string GetFullName(string domain, string username) {
            var level = 2;
            IntPtr netUserInfoPointer;
            var server = string.IsNullOrEmpty(domain) ? null : @"\\" + domain;

            // attempt to get the user info
            var result = NetUserGetInfo(server, username, level, out netUserInfoPointer);
            if (result != NERR_Success)
                throw new System.ComponentModel.Win32Exception(result);

            // if there was user info, marshall the pointer to a USER_INFO_2 structure
            // and return the full name from the structure
            try {
                var info = (USER_INFO_2)Marshal.PtrToStructure(netUserInfoPointer, typeof(USER_INFO_2));
                return info.usri2_full_name;
            }
            finally {
                NetApiBufferFree(netUserInfoPointer);
            }
        }

        public static bool IsUserLocalAdministrator(string userSid) {
            return IsUserMemberOfLocalGroup(new SecurityIdentifier(userSid), new SecurityIdentifier("S-1-5-32-544"));
        }

        public static bool IsUserLocalUser(string userSid) {
            return IsUserMemberOfLocalGroup(new SecurityIdentifier(userSid), new SecurityIdentifier("S-1-5-32-545"));
        }

        public static bool IsUserMemberOfLocalGroup(SecurityIdentifier userSid, SecurityIdentifier groupSid) {
            var level = 2;
            IntPtr netLocalGroupMembersPointer;
            int entriesRead;
            int totalEntries;

            // resolve the SID to a localized name
            var groupName = GroupInformation.ResolveLocalizedGroupName(groupSid);

            // attempt to get the members for the group
            var result = NetLocalGroupGetMembers(
                null, // local machine
                groupName,
                level, // level 2 will give us LOCALGROUP_MEMBERS_INFO_2
                out netLocalGroupMembersPointer,
                -1,
                out entriesRead,
                out totalEntries,
                IntPtr.Zero
            );
            if (result != NERR_Success) {
                throw new System.ComponentModel.Win32Exception(result);
            }

            // if there was a response, marshall the pointer to an
            // array of LOCALGROUP_MEMBERS_INFO_2 structures and
            // check if the specified user SID is in the list of members
            try {
                var iter = netLocalGroupMembersPointer;
                var memberStructSize = Marshal.SizeOf(typeof(LOCALGROUP_MEMBERS_INFO_2));

                for (var i = 0; i < entriesRead; i++) {
                    var memberInfo = (LOCALGROUP_MEMBERS_INFO_2)Marshal.PtrToStructure(iter, typeof(LOCALGROUP_MEMBERS_INFO_2));
                    var memberSid = new SecurityIdentifier(memberInfo.sid);

                    // return early as soon as we find a match
                    if (memberSid.Equals(userSid)) {
                        return true;
                    }

                    iter = IntPtr.Add(iter, memberStructSize);
                }
            }
            finally {
                NetApiBufferFree(netLocalGroupMembersPointer);
            }

            return false;
        }
    }
}
