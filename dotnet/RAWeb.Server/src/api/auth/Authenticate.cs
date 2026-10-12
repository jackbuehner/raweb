using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using RAWeb.Server.Utilities;

namespace RAWeb.Server.Api;

internal static class AuthenticateEndpoint {
  internal static void Map(IEndpointRouteBuilder app) {
    app.MapPost("/api/auth/authenticate", HandleAuthenticate);
    app.MapPost("/api/auth/authenticate/sudo", HandleSudoAuthenticate);
    app.MapGet("/api/auth/duo/callback", HandleDuoCallback);
    app.MapGet("/api/auth/logintc/callback", HandleLoginTCCallback);
    app.MapGet("/api/auth/login", HandleTestLoginForm);
  }

  private static IResult HandleAuthenticate(ValidateCredentialsBody body, HttpContext ctx) {
    if (ShouldAuthenticateAnonymously(body.Username)) {
      var ticket = AuthTicket.FromUserInformation(UserInformation.AnonymousUser, AuthTicketLevel.ReadOnlyUser);
      return CreateAuthCookieResponse(ctx, "anonymous", "RAWEB", ticket);
    }

    var credentials = new ParsedCredentials(body.Username, body.Password);

    try {
      // check if the username and password are valid for the domain
      using (var userToken = SignIn.ValidateCredentials(credentials.Username, credentials.Password, credentials.Domain)) {
        var maxLevel = IsSudoPolicyEnabled() ? AuthTicketLevel.ReadOnlyAdmin : AuthTicketLevel.ReadAndWriteAdmin;
        var ticket = AuthTicket.FromLogonToken(userToken.DangerousGetHandle(), maxLevel: maxLevel);

        // update the credentials to have the correct case for username and domain
        var userInfo = UserInformation.FromDownLevelLogonName(ticket.Name, ticket.UserData.Level);
        credentials.Username = userInfo!.Username;
        credentials.Domain = userInfo.Domain;

        // if MFA is enabled, redirect to the MFA prompt instead of setting the auth cookie directly
        var mfaResult = TriggerMultiFactorAuthenticationPrompt(ctx, credentials, body.ReturnUrl);
        if (mfaResult is not null) {
          return mfaResult;
        }

        return CreateAuthCookieResponse(ctx, credentials.Username, credentials.Domain, ticket);
      }
    }
    catch (ValidateCredentialsException ex) {
      return Results.Ok(
          new AuthResponse {
            Success = false,
            Error = ex.Message,
            Domain = credentials.Domain
          }
      );
    }
  }

  private static IResult HandleSudoAuthenticate(ValidateCredentialsBody body, HttpContext ctx) {
    if (ShouldAuthenticateAnonymously(body.Username)) {
      return Results.Ok(
          new AuthResponse {
            Success = false,
            Error = "login.sudoAnonymousBlocked",
            Domain = "RAWEB"
          }
      );
    }

    var credentials = new ParsedCredentials(body.Username, body.Password);

    try {
      // check if the username and password are valid for the domain
      using (var userToken = SignIn.ValidateCredentials(credentials.Username, credentials.Password, credentials.Domain)) {
        var ticket = AuthTicket.FromLogonToken(userToken.DangerousGetHandle(), maxLevel: AuthTicketLevel.ReadAndWriteAdmin);

        // since this is elevated, use a short-lived ticket/token
        ticket.Expiration = DateTime.UtcNow.AddHours(1);

        // update the credentials to have the correct case for username and domain
        var userInfo = UserInformation.FromDownLevelLogonName(ticket.Name, ticket.UserData.Level);
        credentials.Username = userInfo!.Username;
        credentials.Domain = userInfo.Domain;

        // only allow administrators
        if (!userInfo.IsLocalAdministrator) {
          throw new ValidateCredentialsException("login.sudoRequiresAdmin");
        }
        // confirm that the current user is the same as the user being authenticated for sudo
        var currentUser = UserInformation.FromHttpRequestSafe(ctx.Request);
        if (currentUser is null || currentUser.Domain != credentials.Domain || currentUser.Username != credentials.Username) {
          throw new ValidateCredentialsException("login.sudoUserMismatch");
        }

        return CreateAuthCookieResponse(ctx, credentials.Username, credentials.Domain, ticket, isSudo: true);
      }
    }
    catch (ValidateCredentialsException ex) {
      return Results.Ok(
          new AuthResponse {
            Success = false,
            Error = ex.Message,
            Domain = credentials.Domain
          }
      );
    }
  }

  /// <summary>
  /// A simple login form for testing the authentication endpoint
  /// during development. This is not intended for production use,
  /// but it may be helpful for quickly isolating login issues for
  /// users who are having issues with the web app login flow.
  /// </summary>
  private static IResult HandleTestLoginForm(HttpContext ctx) {
    var authUser = UserInformation.FromHttpRequestSafe(ctx.Request);

    return Results.Content(
      $$"""
      <!DOCTYPE html>
      <html>
        <head>
          <title>Sign In</title>
        </head>
        <body style="font-family: sans-serif; max-width: 400px; margin: auto; display: flex; flex-direction: column; align-items: stretch; justify-content: center; height: 100dvh; gap: 8px;">
          <div id="login" style="padding: 0 16px; border: 1px solid lightgray;">
            <h2>Sign In</h2>
            <p style="color: darkred;">This form is for testing the authentication endpoint. It is not intended for production use.</p>
            <form id="form">
              <label>Username: <input name="username" required /></label><br /><br />
              <label>Password: <input name="password" type="password" required /></label><br /><br />
              <button type="submit">Sign In</button>
              <p id="msg"></p>
            </form>
          </div>
          {{(authUser is not null
              ? $$"""
                <div style="padding: 4px 16px; border: 1px solid lightgray; font-size: 90%;">
                  <p>Currently authenticated as: {{authUser.Domain}}\{{authUser.Username}}</p>
                </div>
                """
              : ""
)}}
          <script>
            document.getElementById('form').onsubmit = async (event) => {
              event.preventDefault();
              const formData = new FormData(event.target);
              const res = await fetch('/api/auth/authenticate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ 
                  username: formData.get('username'),
                  password: formData.get('password'),
                  returnUrl: window.location.href,
                }),
              });
              const msg = document.getElementById('msg');
              if (res.ok) {
                const data = await res.json();
                msg.textContent = data.success ? 'Credentials validated for: ' + data.username : 'Error: ' + data.error;
                msg.style.color = data.success ? 'green' : 'red';
                if (data.mfa_redirect) {
                  const mfaSpanElem = document.createElement('span');
                  const mfaLinkElem = document.createElement('a');
                  mfaLinkElem.href = data.mfa_redirect;
                  mfaLinkElem.textContent = 'Click here to complete multi-factor authentication';
                  mfaSpanElem.appendChild(document.createElement('br'));
                  mfaSpanElem.appendChild(mfaLinkElem);
                  msg.appendChild(mfaSpanElem);
                }
              } else {
                msg.textContent = 'Authentication failed (' + res.status + ')';
                msg.style.color = 'red';
              }
            };
          </script>
        </body>
      </html>
      """,
      "text/html"
    );
  }

  /// <summary>
  /// Reads the response from Duo MFA and creates an authentication cookie if
  /// the response indicates a successful authentication and the state matches
  /// the MFA state cookie that was set when the sign-in started.
  /// </summary>
  /// <param name="state"></param>
  /// <param name="code"></param>
  /// <returns></returns>
  private static IResult HandleDuoCallback(HttpContext ctx, string state, string code) {
    // the user's identity and return URL come from the protected MFA state cookie,
    // which is bound to this browser and cannot be modified by the client
    if (!TryConsumeMfaState(ctx, state, out var mfaState)) {
      return Results.BadRequest(InvalidMfaStateMessage);
    }

    try {
      var duoPolicy = PoliciesManager.GetDuoMfaPolicyForDomain(mfaState.Domain, mfaState.Username);
      var redirectPath = ctx.Request.PathBase + duoPolicy!.RedirectPath; // append the path prefix for RAWeb
      var duoAuth = new DuoAuth(duoPolicy.ClientId, duoPolicy.SecretKey, duoPolicy.Hostname, redirectPath);

      var result = duoAuth.VerifyResponse(code, mfaState.State, mfaState.ReturnUrl);
      return CreateMfaAuthCookieResponse(ctx, mfaState, result.Username, result.ReturnUrl);
    }
    catch (Exception ex) {
      return Results.Ok(ex.Message);
    }
  }

  /// <summary>
  /// Reads the response from LoginTC MFA and creates an authentication cookie if
  /// the response indicates a successful authentication and the state matches
  /// the MFA state cookie that was set when the sign-in started.
  /// </summary>
  /// <param name="state"></param>
  /// <param name="code"></param>
  /// <returns></returns>
  private static IResult HandleLoginTCCallback(HttpContext ctx, string state, string? code = null, string? error = null, string? error_description = null) {
    // the user's identity and return URL come from the protected MFA state cookie,
    // which is bound to this browser and cannot be modified by the client
    if (!TryConsumeMfaState(ctx, state, out var mfaState)) {
      return Results.BadRequest(InvalidMfaStateMessage);
    }

    try {
      var loginTcPolicy = PoliciesManager.GetLoginTCMfaPolicyForDomain(mfaState.Domain, mfaState.Username);
      var redirectPath = ctx.Request.PathBase + loginTcPolicy!.RedirectPath; // append the path prefix for RAWeb
      var loginTcAuth = new LoginTCAuth(loginTcPolicy.ClientId, loginTcPolicy.SecretKey, loginTcPolicy.Hostname, redirectPath);

      if (error == "access_denied" && error_description == "User cancelled authentication") {
        try {
          // redirect to the logout page
          var returnUri = new UriBuilder(mfaState.ReturnUrl) {
            Path = ctx.Request.PathBase + "/logoff"
          }.Uri;
          return Results.Redirect(returnUri.ToString());
        }
        catch (UriFormatException) {
          // if the return URL is not a valid absolute URI, use the regular error response instead of redirecting
        }
      }

      if (error is not null) {
        return Results.Ok(error_description);
      }

      var result = loginTcAuth.VerifyResponse(code!, mfaState.State, mfaState.ReturnUrl);
      return CreateMfaAuthCookieResponse(ctx, mfaState, result.Username, result.ReturnUrl);
    }
    catch (Exception ex) {
      return Results.Ok(ex.Message);
    }
  }

  private const string InvalidMfaStateMessage = "The multi-factor authentication request is invalid or has expired. Please sign in again.";

  /// <summary>
  /// Creates the authentication cookie after a successful MFA response.
  /// <br /><br />
  /// The user is the one whose password was validated when the MFA prompt was triggered (from the MFA state).
  /// The username reported by the MFA provider must match that user.
  /// </summary>
  private static IResult CreateMfaAuthCookieResponse(HttpContext ctx, MfaState mfaState, string mfaUsername, string? returnUrl) {
    if (!string.Equals(mfaUsername, mfaState.Username, StringComparison.OrdinalIgnoreCase)) {
      throw new InvalidOperationException("The multi-factor authentication response is for a different user.");
    }

    var userInfo = UserInformation.FromDownLevelLogonName(mfaState.Domain + "\\" + mfaState.Username, AuthTicketLevel.ReadOnlyUser);
    var adminLevel = IsSudoPolicyEnabled() ? AuthTicketLevel.ReadOnlyAdmin : AuthTicketLevel.ReadAndWriteAdmin;
    var ticket = AuthTicket.FromUserInformation(userInfo!, userInfo!.IsLocalAdministrator ? adminLevel : AuthTicketLevel.ReadOnlyUser);

    return CreateAuthCookieResponse(ctx, userInfo.Username, userInfo.Domain, ticket, returnUrl);
  }

  /// <summary>
  /// The user and return URL for an in-progress MFA sign-in.
  /// </summary>
  private sealed record MfaState(string State, string Domain, string Username, string ReturnUrl);

  private static readonly TimeSpan s_mfaStateLifetime = TimeSpan.FromMinutes(10);

  private static ITimeLimitedDataProtector GetMfaStateProtector(HttpContext ctx) {
    return ctx.RequestServices
      .GetRequiredService<IDataProtectionProvider>()
      .CreateProtector("RAWeb.MfaState")
      .ToTimeLimitedDataProtector();
  }

  /// <summary>
  /// Starts an MFA sign-in by storing the MFA provider's state value, the user, and the
  /// return URL in a protected, short-lived cookie in this browser.
  /// <br /><br />
  /// When the MFA provider redirects back, <see cref="TryConsumeMfaState"/> only accepts the
  /// response if its state matches the cookie. This prevents the user or domain from being
  /// changed in the callback and prevents MFA responses from being used in another browser.
  /// </summary>
  /// <param name="state">The state value from the MFA provider's authorization request.</param>
  private static void StartMfaState(HttpContext ctx, ParsedCredentials credentials, string returnUrl, string state) {
    // the values are separated by newlines, which cannot appear in the state, domain, or username,
    // and the return URL is last so that it may contain any characters
    var plaintext = string.Join('\n', state, credentials.Domain, credentials.Username, returnUrl);
    var protectedValue = GetMfaStateProtector(ctx).Protect(plaintext, s_mfaStateLifetime);

    ctx.Response.Cookies.Append(Constants.MfaStateCookieName, protectedValue, new CookieOptions {
      Path = ctx.Request.PathBase.HasValue ? ctx.Request.PathBase + "/" : "/",
      HttpOnly = true,
      Secure = ctx.Request.IsHttps,
      SameSite = SameSiteMode.Lax, // allow the browser to send the cookie when the MFA provider redirects back to the login page
      MaxAge = s_mfaStateLifetime
    });
  }

  /// <summary>
  /// Reads and removes the MFA state cookie. Returns false if the cookie is missing,
  /// invalid, expired, or does not match the state returned by the MFA provider.
  /// </summary>
  private static bool TryConsumeMfaState(HttpContext ctx, string? state, [NotNullWhen(true)] out MfaState? mfaState) {
    mfaState = null;

    var protectedValue = ctx.Request.Cookies[Constants.MfaStateCookieName];
    ctx.Response.Cookies.Delete(Constants.MfaStateCookieName, new CookieOptions {
      Path = ctx.Request.PathBase.HasValue ? ctx.Request.PathBase + "/" : "/"
    });

    if (string.IsNullOrEmpty(protectedValue) || string.IsNullOrEmpty(state)) {
      return false;
    }

    string plaintext;
    try {
      plaintext = GetMfaStateProtector(ctx).Unprotect(protectedValue, out _);
    }
    catch (CryptographicException) {
      return false; // the cookie was modified, is invalid for this MfaState, or has expired
    }

    var parts = plaintext.Split('\n', 4);
    if (parts.Length != 4) {
      return false;
    }

    var expectedState = Encoding.UTF8.GetBytes(parts[0]);
    var actualState = Encoding.UTF8.GetBytes(state);
    if (!CryptographicOperations.FixedTimeEquals(expectedState, actualState)) {
      return false;
    }

    mfaState = new MfaState(parts[0], parts[1], parts[2], parts[3]);
    return true;
  }

  /// <summary>
  /// Triggers the multi-factor authentication prompt if at least one MFA provider is configured.
  /// <br /><br />
  /// If no MFA providers are configured, this method returns null.
  /// </summary>
  /// <param name="credentials"></param>
  /// <param name="returnUrl">The absolute URL that the web app should go to after MFA is completed.</param>
  /// <returns></returns>
  private static IResult? TriggerMultiFactorAuthenticationPrompt(HttpContext ctx, ParsedCredentials credentials, string? returnUrl) {
    // if Duo MFA is enabled, redirect to Duo authorization endpoint
    var duoPolicy = PoliciesManager.GetDuoMfaPolicyForDomain(credentials.Domain, credentials.Username);
    if (duoPolicy is not null) {
      try {
        var redirectPath = ctx.Request.PathBase + duoPolicy.RedirectPath; // append the path prefix for RAWeb
        var duoAuth = new DuoAuth(duoPolicy.ClientId, duoPolicy.SecretKey, duoPolicy.Hostname, redirectPath);
        duoAuth.DoHealthCheck();
        var authorizationRequest = duoAuth.GetRequestAuthorizationEndpoint(credentials.Username, returnUrl ?? "");
        StartMfaState(ctx, credentials, returnUrl ?? "", authorizationRequest.State);
        return Results.Ok(
          new AuthResponse {
            Success = true,
            Username = credentials.Username,
            MfaRedirect = authorizationRequest.Url,
            Domain = credentials.Domain
          }
        );
      }
      catch (Exception ex) {
        System.Diagnostics.Debug.WriteLine("Duo health check or authorization request failed: " + ex.Message);
        return Results.Ok(
          new AuthResponse {
            Success = false,
            Error = ex.Message,
            Domain = credentials.Domain
          }
        );
      }
    }

    // if LoginTC MFA is enabled, redirect to LoginTC authorization endpoint
    var loginTcPolicy = PoliciesManager.GetLoginTCMfaPolicyForDomain(credentials.Domain, credentials.Username);
    if (loginTcPolicy is not null) {
      try {
        var redirectPath = ctx.Request.PathBase + loginTcPolicy.RedirectPath; // append the path prefix for RAWeb
        var loginTcAuth = new LoginTCAuth(loginTcPolicy.ClientId, loginTcPolicy.SecretKey, loginTcPolicy.Hostname, redirectPath);
        loginTcAuth.DoPing();
        var authorizationRequest = loginTcAuth.GetRequestAuthorizationEndpoint(credentials.Username, returnUrl ?? "");
        StartMfaState(ctx, credentials, returnUrl ?? "", authorizationRequest.State);
        return Results.Ok(
          new AuthResponse {
            Success = true,
            Username = credentials.Username,
            MfaRedirect = authorizationRequest.Url,
            Domain = credentials.Domain
          }
        );
      }
      catch (Exception ex) {
        System.Diagnostics.Debug.WriteLine("LoginTC ping or authorization request failed: " + ex.Message);
        return Results.Ok(
          new AuthResponse {
            Success = false,
            Error = ex.Message,
            Domain = credentials.Domain
          }
        );
      }
    }

    return null;
  }

  private static bool ShouldAuthenticateAnonymously(string? username) {
    var anonSetting = PoliciesManager.RawPolicies["App.Auth.Anonymous"];
    return anonSetting == "always" || (anonSetting == "allow" && username == "RAWEB\\anonymous");
  }

  private static bool IsSudoPolicyEnabled() {
    return PoliciesManager.RawPolicies["App.Auth.Sudo.Enabled"] != "false";
  }

  private class ParsedCredentials {
    public string Domain { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }

    public ParsedCredentials(string? username, string? password) {
      Password = password ?? "";

      // if the username contains a domain, split it to get the username and domain separately
      if (username?.Contains('\\') == true) {
        var parts = username.Split(['\\'], 2);
        Domain = parts[0]; // the part before the backslash is the domain
        Username = parts[1]; // the part after the backslash is the username
      }
      else {
        Domain = SignIn.GetDomainName();
        Username = username ?? "";
      }
    }
  }

  private static IResult CreateAuthCookieResponse(HttpContext ctx, string username, string domain, AuthTicket ticket, string? returnUrl = null, bool isSudo = false) {
    var cookiePath = ctx.Request.PathBase.HasValue ? ctx.Request.PathBase + "/" : "/"; // set the path to the application root
    var cookie = ticket.ToCookie(cookiePath, cookieName: isSudo ? Constants.SudoAuthCookieName : null);

    ctx.Response.Cookies.Append(cookie.Name, cookie.Value, new CookieOptions {
      Path = cookie.Path,
      HttpOnly = cookie.HttpOnly,
      Secure = cookie.Secure,
      Expires = cookie.Expires == DateTime.MinValue ? null : (DateTimeOffset?)cookie.Expires
    });

    if (!string.IsNullOrEmpty(returnUrl)) {
      return Results.Redirect(returnUrl);
    }

    return Results.Ok(
      new AuthResponse {
        Success = true,
        Username = username,
        Domain = domain
      }
    );
  }
}

public class ValidateCredentialsBody {
  public string? Username { get; set; }
  public string? Password { get; set; }
  [JsonPropertyName("returnUrl")] public string? ReturnUrl { get; set; } // nullable
}

public class AuthResponse {
  [JsonPropertyName("success")]
  public bool Success { get; init; }

  [JsonPropertyName("username"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Username { get; init; }

  [JsonPropertyName("domain")]
  public string? Domain { get; init; }

  [JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Error { get; init; }

  [JsonPropertyName("mfa_redirect"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? MfaRedirect { get; init; }
}
