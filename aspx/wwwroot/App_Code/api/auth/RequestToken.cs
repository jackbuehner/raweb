using System.Net;
using System.Web.Http;
using RAWebServer.Utilities;

namespace RAWebServer.Api {
  public partial class AuthController : ApiController {
    public class TokenRequestBody : ValidateCredentialsBody {
      public TokenType TokenType { get; set; }

      public TokenRequestBody() {
        TokenType = TokenType.Standard;
      }
    }

    [HttpPost]
    [Route("token")]
    public IHttpActionResult RequestToken([FromBody] TokenRequestBody body) {
      if (ShouldAuthenticateAnonymously(null)) {
        var anonEncryptedToken = AuthCookieHandler.CreateAuthTicket(s_anonUserInfo);
        return Content(HttpStatusCode.OK, new {
          success = true,
          username = "anonymous",
          domain = "RAWEB",
          token = anonEncryptedToken.Token,
        });
      }


      var credentials = new ParsedCredentialsBody(body.Username, body.Password);

      try {
        // check if the username and password are valid for the domain
        using (var userToken = SignOn.ValidateCredentials(credentials.Username, credentials.Password, credentials.Domain)) {
          var encryptedToken = AuthCookieHandler.CreateAuthTicket(userToken.DangerousGetHandle(), mayWrite: body.TokenType == TokenType.WriteSession);
          return Content(HttpStatusCode.OK, new {
            success = true,
            username = credentials.Username,
            domain = credentials.Domain,
            token = encryptedToken.Token,
            expires = encryptedToken.ExpirationDate,
          });
        }
      }
      catch (ValidateCredentialsException ex) {
        return Content(HttpStatusCode.Unauthorized, new {
          success = false,
          username = credentials.Username,
          domain = credentials.Domain,
          error = ex.Message,
        });
      }
    }

    public enum TokenType {
      Standard = 0,
      WriteSession = 1,
    }
  }
}
