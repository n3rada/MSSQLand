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
            // Database is optional - if not specified, uses login's default database
            var connectionString = $"Server={sqlServer};{(string.IsNullOrEmpty(database) ? "" : $" Database={database};")} Integrated Security=False; User Id={username}; Password={password};";
            return CreateSqlConnection(connectionString, sqlServer);
        }
    }
}
