using System;
using System.Web.Http;

namespace RAWebServer.Api
{
    public partial class PoliciesController : ApiController
    {
        [HttpGet]
        [Route("{key}")]
        [RequireLocalAdministrator]
        public IHttpActionResult GetAppSetting(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return BadRequest("Key cannot be null or empty.");
            }

            var value = System.Configuration.ConfigurationManager.AppSettings[key];
            return Ok(new { key, value });
        }
    }
}
