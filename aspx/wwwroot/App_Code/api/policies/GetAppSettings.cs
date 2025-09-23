using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Web.Http;

namespace RAWebServer.Api
{
    public partial class PoliciesController : ApiController
    {
        [HttpGet]
        [Route("")]
        [RequireLocalAdministrator]
        public IHttpActionResult GetAppSettings()
        {
            try
            {
                var appSettings = System.Configuration.ConfigurationManager.AppSettings;
                var settingsDict = new Dictionary<string, string?>();
                
                foreach (string key in appSettings.AllKeys)
                {
                    settingsDict[key] = appSettings[key];
                }

                return Ok(settingsDict);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
    }
}
