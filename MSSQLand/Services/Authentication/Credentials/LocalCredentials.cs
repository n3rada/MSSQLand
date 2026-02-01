using MSSQLand.Exceptions;
using MSSQLand.Models;
using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    /// <summary>
    /// SQL Server authentication with encryption enabled by default.
    /// Works for both on-premises SQL Server and Azure SQL Database.
    /// </summary>
    public class LocalCredentials : BaseCredentials
    {
        public LocalCredentials(Server server) : base(server) { }

        /// <summary>
        /// Factory method for creating LocalCredentials instances.
        /// </summary>
        public static LocalCredentials Create(Server server) => new LocalCredentials(server);

        public override SqlConnection Authenticate(string username, string password, string domain = null)
        {
            // SQL Server local authentication uses plain username, not domain\username format
            // Domain accounts should use -c windows (which does NTLM negotiation) instead
            if (!string.IsNullOrEmpty(domain))
            {
                throw new InvalidCredentialException(
                    "local",
                    $"Domain parameter cannot be used with local SQL authentication."
                );
            }
            
            // Encrypt by default for security best practices
            // TrustServerCertificate=True allows self-signed certs (common in on-premises)
            var connectionString = $"Server={Server.GetConnectionTarget()}; Integrated Security=False; User Id={username}; Password={password};";
            return CreateSqlConnection(connectionString);
        }
    }
}
