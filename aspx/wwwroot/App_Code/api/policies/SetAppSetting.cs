using System;
using System.Web.Http;

namespace RAWebServer.Api
{
    public partial class PoliciesController : ApiController
    {
        [HttpPost]
        [Route("{key}")]
        [RequireLocalAdministrator]
        public IHttpActionResult SetAppSetting(string key, [FromBody] string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return BadRequest("Key cannot be null or empty.");
            }

            var shouldRemove = string.IsNullOrEmpty(value);

            try
            {
                var config = System.Web.Configuration.WebConfigurationManager.OpenWebConfiguration("~");

                if (shouldRemove)
                {
                    if (config.AppSettings.Settings[key] != null)
                    {
                        config.AppSettings.Settings.Remove(key);
                    }
                }
                else
                {
                    if (config.AppSettings.Settings[key] == null)
                    {
                        config.AppSettings.Settings.Add(key, value);
                    }
                    else
                    {
                        config.AppSettings.Settings[key].Value = value;
                    }
                }

                config.Save(System.Configuration.ConfigurationSaveMode.Modified);
                return Ok();
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
    }
}
