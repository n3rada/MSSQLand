using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    /// <summary>
    /// SQL Server authentication with encryption enabled by default.
    /// Works for both on-premises SQL Server and Azure SQL Database.
    /// </summary>
    public class LocalCredentials : BaseCredentials
    {
        public override SqlConnection Authenticate(string sqlServer, string database, string username, string password, string domain = null)
        {
            // Encrypt by default for security best practices
            // TrustServerCertificate=True allows self-signed certs (common in on-premises)
            var connectionString = $"Server={sqlServer}; Database={database}; Integrated Security=False; Encrypt=True; TrustServerCertificate=True; User Id={username}; Password={password};";
            return CreateSqlConnection(connectionString);
        }
    }
}
