using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    /// <summary>
    /// Windows Authentication using impersonation.
    /// NOTE: Only works when both client and server are in the same domain,
    /// or when authenticating to a SQL Server on the same machine.
    /// For remote machines with local accounts, use SQL Authentication (-c local) instead.
    /// </summary>
    public class WindowsCredentials : BaseCredentials
    {
        public override SqlConnection Authenticate(string sqlServer, string database, string username, string password, string domain)
        {
            // Determine if it's a domain or local account
            bool isLocalAccount = string.IsNullOrEmpty(domain) || domain == ".";
            
            // For local accounts, use the machine name or "."
            string effectiveDomain = isLocalAccount ? "." : domain;
            
            // Use impersonation to authenticate
            using (new WindowsIdentityImpersonation(effectiveDomain, username, password))
            {
                // Connection string with Integrated Security (uses impersonated token)
                var connectionString = $"Server={sqlServer}; Database={database}; Integrated Security=True;";
                return CreateSqlConnection(connectionString);
            }
        }
    }
}
