using System.IO.Compression;
using System.Net;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using RAWeb.Server.Utilities;

namespace RAWeb.Server.Api;

/// <summary>
/// Downloads an external workspace's webfeed on behalf of the browser and streams it back
/// (along with every resource/icon file the feed references) as a single zip file.
///
/// This exists because a browser cannot perform Windows/NTLM authentication against an arbitrary
/// cross-origin server on its own. This follows the same handshake a real MS-TSWP client
/// performs (see <see cref="AuthenticateWorkspaceEndpoint"/> and <see cref="GetWorkspaceEndpoint"/>
/// in this same codebase, which implement the other side of it):
///
/// 1. GET the feed URL directly. If the caller isn't authenticated yet, the workspace redirects
///    to its own login-feed endpoint (whatever URL that happens to be for that deployment -- it
///    is discovered from the redirect, never assumed). That endpoint requires Windows
///    Authentication, so the credentials the frontend has stored for the workspace are used (via
///    <see cref="HttpClientHandler.Credentials"/>, which negotiates NTLM automatically if
///    challenged) to satisfy it, and <see cref="HttpClient"/> follows the redirect automatically.
/// 2. That login endpoint's response body (not a cookie!) is an encrypted auth ticket string.
/// 3. That string is attached as the value of a <c>.ASPXAUTH</c> cookie on a second request for
///    the original feed URL (and every resource/icon request afterward), which is what the
///    webfeed endpoint actually checks for to consider the caller authenticated.
///
/// If the first request doesn't get redirected at all (e.g. anonymous access is allowed, or
/// Windows Authentication was already satisfied directly on the feed URL itself), its response is
/// used as-is and no second request is made.
///
/// Only one external workspace can be requested per call; this endpoint does not accept a batch
/// of workspaces.
/// </summary>
internal static class GetExternalWorkspaceDownloadEndpoint {
  private const long MaxFileBytes = 15 * 1024 * 1024; // per-file cap
  private const long MaxTotalBytes = 60 * 1024 * 1024; // cap for the whole zip

  private static readonly Logger s_logger = new("external-workspace");

  internal static void Map(IEndpointRouteBuilder app) {
    app.MapPost("/api/external-workspace/download", Handle);
  }

  private static async Task<IResult> Handle(ExternalWorkspaceDownloadRequestBody body, HttpContext ctx) {
    // this endpoint fetches an arbitrary, user-supplied URL on the server's behalf,
    // so it must only be usable by users who are already authenticated to this RAWeb instance
    var userInfo = UserInformation.FromHttpRequestSafe(ctx.Request);
    if (userInfo is null) {
      s_logger.WriteLogline("Rejected: caller is not authenticated to this RAWeb instance.", writeToConsole: true);
      return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(body.Url) ||
        !Uri.TryCreate(body.Url, UriKind.Absolute, out var feedUri) ||
        (feedUri.Scheme != Uri.UriSchemeHttp && feedUri.Scheme != Uri.UriSchemeHttps)) {
      s_logger.WriteLogline($"Rejected: '{body.Url}' is not a valid HTTP(S) URL.", writeToConsole: true);
      return Results.BadRequest("The provided URL is not a valid HTTP(S) URL.");
    }

    s_logger.WriteLogline($"Starting download for {feedUri} (user: {userInfo.Domain}\\{userInfo.Username}).", writeToConsole: true);

    // NOT `using`: this endpoint returns Results.Stream(), whose callback runs after Handle()
    // itself has already returned. Disposing these here (as `using` would) would tear down the
    // HttpClient before the deferred callback below gets a chance to use it for resource
    // fetches, so disposal happens explicitly at the end of that callback instead.
    var handler = new HttpClientHandler {
      // we manage the .ASPXAUTH cookie ourselves (it comes from the login-feed response body,
      // not a Set-Cookie header), so let the handler leave the Cookie header alone
      UseCookies = false,
      // we resolve the login redirect ourselves and authenticate against it as its own,
      // isolated request -- letting HttpClient auto-follow the redirect and complete the NTLM
      // handshake as part of the same automatic chain does not reliably work
      AllowAutoRedirect = false,
    };
    if (!string.IsNullOrEmpty(body.Username)) {
      var (domain, username) = SplitDomainAndUsername(body.Username);

      // use the 2-arg constructor when there's no domain so NetworkCredential.Domain stays
      // null, not "" -- an explicit empty string is sent as a literal (empty) NTLM domain
      // field instead of signaling "authenticate against the target's local accounts",
      // which breaks NTLM auth for local (non-domain) accounts even with correct credentials
      var credential = string.IsNullOrEmpty(domain)
        ? new NetworkCredential(username, body.Password ?? "")
        : new NetworkCredential(username, body.Password ?? "", domain);

      s_logger.WriteLogline($"Using credentials for '{(string.IsNullOrEmpty(domain) ? "(no domain)" : domain)}\\{username}'.", writeToConsole: true);
      handler.Credentials = credential;
    }
    else {
      s_logger.WriteLogline("No credentials provided; requests will be sent anonymously.", writeToConsole: true);
    }

    var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

    // probe the feed URL first, without following redirects, so we can see exactly where the
    // workspace wants us to authenticate (never assumed/hardcoded)
    string? authToken = null;
    var (probeBytes, probeStatusCode, probeLocation, probeError) = await ProbeAsync(client, feedUri);
    if (probeError is not null) {
      s_logger.WriteLogline($"Initial feed request failed: {probeError}", writeToConsole: true);
      client.Dispose();
      handler.Dispose();
      return Results.Problem(probeError, statusCode: 502);
    }

    byte[] feedBytes;
    if (probeLocation is not null) {
      // the workspace redirected us to its login endpoint; authenticate against that exact URL
      // as its own isolated request (NTLM is negotiated within this single request/response cycle)
      var loginUri = probeLocation.IsAbsoluteUri ? probeLocation : new Uri(feedUri, probeLocation.ToString());
      s_logger.WriteLogline($"Redirected ({(int)probeStatusCode}) to login endpoint: {loginUri}", writeToConsole: true);

      var (tokenBytes, _, authError) = await FetchAsync(client, loginUri, null);
      if (tokenBytes is null) {
        s_logger.WriteLogline($"Login request failed: {authError}", writeToConsole: true);
        client.Dispose();
        handler.Dispose();
        return Results.Problem(authError ?? "Failed to authenticate with the external workspace.", statusCode: 502);
      }

      authToken = System.Text.Encoding.UTF8.GetString(tokenBytes).Trim();
      s_logger.WriteLogline($"Authenticated; received a {authToken.Length}-character token.", writeToConsole: true);

      var (bytes, _, feedError) = await FetchAsync(client, feedUri, authToken);
      if (bytes is null) {
        s_logger.WriteLogline($"Feed request (with auth token) failed: {feedError}", writeToConsole: true);
        client.Dispose();
        handler.Dispose();
        return Results.Problem(feedError ?? "Failed to fetch the external workspace feed.", statusCode: 502);
      }
      feedBytes = bytes;
    }
    else {
      // no redirect occurred: either anonymous access is allowed, or Windows Authentication
      // was already satisfied directly on the feed URL itself
      feedBytes = probeBytes!;
    }
    s_logger.WriteLogline($"Feed request succeeded; received {feedBytes.Length} bytes.", writeToConsole: true);

    List<string> referencedUrls;
    try {
      var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(feedBytes));
      referencedUrls = ExtractReferencedUrls(doc, feedUri);
      s_logger.WriteLogline($"Found {referencedUrls.Count} referenced resource/icon URL(s) in the feed.", writeToConsole: true);
    }
    catch (Exception ex) {
      s_logger.WriteLogline($"Failed to parse the feed as XML; continuing with no referenced resources: {ex.Message}", writeToConsole: true);
      referencedUrls = [];
    }

    ctx.Response.Headers.ContentDisposition = "attachment; filename=\"workspace.zip\"";

    return Results.Stream(
      async outputStream => {
        try {
          // ZipArchive.Dispose() writes the (small) central directory with a synchronous
          // Stream.Write call internally, which Kestrel disallows by default. Rather than
          // buffering the whole archive in memory to avoid it, just allow that one synchronous
          // write through -- every entry's actual content is still written with WriteAsync
          // below, so this keeps the response fully streamed instead of held in memory.
          var syncIOFeature = ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
          if (syncIOFeature is not null) {
            syncIOFeature.AllowSynchronousIO = true;
          }

          using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true);

          var feedEntry = archive.CreateEntry("feed.xml", CompressionLevel.Fastest);
          await using (var entryStream = feedEntry.Open()) {
            await entryStream.WriteAsync(feedBytes);
          }

          // this must be the exact same base used above (feedUri, not just its scheme+host) so
          // that the frontend resolves the feed's relative resource/icon URLs to the identical
          // absolute URLs used as keys in `Resources` below -- otherwise a relative (non-leading-
          // slash) reference resolves differently client-side and the blob URL lookup misses
          var manifest = new ExternalWorkspaceManifest { Origin = feedUri.ToString() };
          long totalBytes = feedBytes.Length;

          var index = 0;
          var succeededResources = 0;
          var failedResources = 0;
          foreach (var url in referencedUrls) {
            if (totalBytes >= MaxTotalBytes) {
              s_logger.WriteLogline($"Stopping early: reached the {MaxTotalBytes}-byte total zip size cap.", writeToConsole: true);
              break;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var resourceUri)) {
              s_logger.WriteLogline($"Skipping resource with unparsable URL: {url}");
              continue;
            }

            var (bytes, contentType) = await TryFetchResourceAsync(client, resourceUri, authToken);
            if (bytes is null) {
              failedResources++;
              s_logger.WriteLogline($"Failed to fetch resource: {resourceUri}");
              continue;
            }
            succeededResources++;

            totalBytes += bytes.Length;
            var entryName = $"resources/{index++}";
            manifest.Resources[url] = new ExternalWorkspaceManifestResource { Entry = entryName, ContentType = contentType };

            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            await using var entryFileStream = entry.Open();
            await entryFileStream.WriteAsync(bytes);
          }

          s_logger.WriteLogline($"Finished: {succeededResources} resource(s) fetched, {failedResources} failed.", writeToConsole: true);

          var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
          await using (var manifestStream = manifestEntry.Open()) {
            await System.Text.Json.JsonSerializer.SerializeAsync(manifestStream, manifest, WebApiJsonSerializerContext.Default.ExternalWorkspaceManifest);
          }
        }
        finally {
          client.Dispose();
          handler.Dispose();
        }
      },
      "application/zip"
    );
  }

  /// <summary>
  /// GETs <paramref name="feedUri"/> without following redirects, to discover whether (and
  /// where) the workspace wants the caller to authenticate. Returns the response body if the
  /// request succeeded outright (e.g. anonymous access, or Windows Authentication already
  /// satisfied directly on this URL), or the redirect <c>Location</c> if one was returned.
  /// </summary>
  private static async Task<(byte[]? bytes, HttpStatusCode statusCode, Uri? location, string? error)> ProbeAsync(HttpClient client, Uri feedUri) {
    s_logger.WriteLogline($"Probing: {feedUri}", writeToConsole: true);

    using var request = new HttpRequestMessage(HttpMethod.Get, feedUri);
    request.Headers.Add("Accept", "application/x-msts-radc+xml; radc_schema_version=2.0");
    request.Headers.Add("User-Agent", "TSWorkspace/2.0");

    try {
      using var response = await client.SendAsync(request);
      s_logger.WriteLogline($"Probe responded: {(int)response.StatusCode} {response.ReasonPhrase}", writeToConsole: true);

      if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest && response.Headers.Location is not null) {
        return (null, response.StatusCode, response.Headers.Location, null);
      }

      if (!response.IsSuccessStatusCode) {
        var challenges = string.Join(", ", response.Headers.WwwAuthenticate.Select(h => h.Scheme));
        var challengeSuffix = challenges.Length > 0 ? $" (WWW-Authenticate: {challenges})" : "";
        return (null, response.StatusCode, null, $"The external server responded with {(int)response.StatusCode} {response.ReasonPhrase}{challengeSuffix}");
      }

      var bytes = await response.Content.ReadAsByteArrayAsync();
      if (bytes.LongLength > MaxFileBytes) {
        return (null, response.StatusCode, null, "The response from the external server was too large.");
      }

      return (bytes, response.StatusCode, null, null);
    }
    catch (Exception ex) {
      s_logger.WriteLogline($"Exception while probing {feedUri}: {ex}", writeToConsole: true);
      return (null, 0, null, ex.Message);
    }
  }

  /// <summary>
  /// Requests <paramref name="uri"/> as a single, non-redirected request (NTLM, if the target
  /// challenges for it, is negotiated within this one request/response cycle via the client's
  /// configured <see cref="HttpClientHandler.Credentials"/>).
  /// </summary>
  private static async Task<(byte[]? bytes, Uri? finalUri, string? error)> FetchAsync(HttpClient client, Uri uri, string? authToken) {
    s_logger.WriteLogline($"Requesting: {uri}", writeToConsole: true);

    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
    request.Headers.Add("Accept", "application/x-msts-radc+xml; radc_schema_version=2.0");
    request.Headers.Add("User-Agent", "TSWorkspace/2.0");
    if (!string.IsNullOrEmpty(authToken)) {
      request.Headers.Add("Cookie", $"{Constants.DefaultAuthCookieName}={authToken}");
    }

    try {
      using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
      var finalUri = response.RequestMessage?.RequestUri;
      s_logger.WriteLogline($"Responded: {(int)response.StatusCode} {response.ReasonPhrase} (final URL: {finalUri})", writeToConsole: true);

      if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaxFileBytes) {
        return (null, finalUri, "The response from the external server was too large.");
      }

      if (!response.IsSuccessStatusCode) {
        var challenges = string.Join(", ", response.Headers.WwwAuthenticate.Select(h => h.Scheme));
        var challengeSuffix = challenges.Length > 0 ? $" (WWW-Authenticate: {challenges})" : "";
        return (null, finalUri, $"The external server responded with {(int)response.StatusCode} {response.ReasonPhrase}{challengeSuffix}");
      }

      var bytes = await response.Content.ReadAsByteArrayAsync();
      if (bytes.LongLength > MaxFileBytes) {
        return (null, finalUri, "The response from the external server was too large.");
      }

      return (bytes, finalUri, null);
    }
    catch (Exception ex) {
      s_logger.WriteLogline($"Exception while requesting {uri}: {ex}", writeToConsole: true);
      return (null, null, ex.Message);
    }
  }

  private static async Task<(byte[]? bytes, string? contentType)> TryFetchResourceAsync(HttpClient client, Uri uri, string? authToken) {
    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
    request.Headers.Add("User-Agent", "TSWorkspace/2.0");
    if (!string.IsNullOrEmpty(authToken)) {
      request.Headers.Add("Cookie", $"{Constants.DefaultAuthCookieName}={authToken}");
    }

    try {
      using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
      if (!response.IsSuccessStatusCode) {
        var bodySnippet = (await response.Content.ReadAsStringAsync()) is { Length: > 0 } body
          ? body[..Math.Min(body.Length, 300)]
          : "(empty body)";
        s_logger.WriteLogline($"Resource {uri} responded: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {bodySnippet}", writeToConsole: true);
        return (null, null);
      }
      if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaxFileBytes) {
        s_logger.WriteLogline($"Resource {uri} exceeds the per-file size cap; skipping.", writeToConsole: true);
        return (null, null);
      }

      var bytes = await response.Content.ReadAsByteArrayAsync();
      if (bytes.LongLength > MaxFileBytes) {
        s_logger.WriteLogline($"Resource {uri} exceeds the per-file size cap; skipping.", writeToConsole: true);
        return (null, null);
      }

      return (bytes, response.Content.Headers.ContentType?.ToString());
    }
    catch (Exception ex) {
      s_logger.WriteLogline($"Exception while requesting resource {uri}: {ex}", writeToConsole: true);
      return (null, null);
    }
  }

  /// <summary>
  /// Finds every RDP resource file and icon file URL referenced by the feed, resolved to
  /// absolute URLs. Element/attribute names are matched by local name only so that this works
  /// regardless of the feed's declared XML namespace.
  /// </summary>
  private static List<string> ExtractReferencedUrls(XDocument doc, Uri baseUri) {
    var urls = new List<string>();

    foreach (var el in doc.Descendants().Where(e => e.Name.LocalName == "ResourceFile")) {
      var urlAttr = el.Attribute("URL")?.Value;
      if (!string.IsNullOrEmpty(urlAttr) && Uri.TryCreate(baseUri, urlAttr, out var abs)) {
        urls.Add(abs.ToString());
      }
    }

    foreach (var el in doc.Descendants().Where(e => e.Attribute("FileURL") != null)) {
      var urlAttr = el.Attribute("FileURL")!.Value;
      if (Uri.TryCreate(baseUri, urlAttr, out var abs)) {
        urls.Add(abs.ToString());
      }
    }

    return urls.Distinct().ToList();
  }

  /// <summary>
  /// Splits a "DOMAIN\username" string into its domain and username parts. If there is no
  /// backslash, the domain is returned empty and the whole string is treated as the username.
  /// </summary>
  private static (string domain, string username) SplitDomainAndUsername(string input) {
    var separatorIndex = input.IndexOf('\\');
    if (separatorIndex < 0) {
      return ("", input);
    }

    return (input[..separatorIndex], input[(separatorIndex + 1)..]);
  }
}

public class ExternalWorkspaceDownloadRequestBody {
  public string? Url { get; set; }
  public string? Username { get; set; }
  public string? Password { get; set; }
}

public class ExternalWorkspaceManifest {
  [JsonPropertyName("origin")]
  public string Origin { get; set; } = "";

  [JsonPropertyName("resources")]
  public Dictionary<string, ExternalWorkspaceManifestResource> Resources { get; set; } = [];
}

public class ExternalWorkspaceManifestResource {
  [JsonPropertyName("entry")]
  public string Entry { get; set; } = "";

  [JsonPropertyName("contentType")]
  public string? ContentType { get; set; }
}
