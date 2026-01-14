using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    public class EntraIDCredentials : BaseCredentials
    {
        public override SqlConnection Authenticate(string sqlServer, string database, string username, string password, string domain)
        {
            username = $"{username}@{domain}";
            
            // Azure SQL requires proper certificate validation
            TrustServerCertificate = false;

            // Azure SQL Database requires a database - default to master if not specified
            // Unlike on-premises SQL Server, Azure doesn't have login-level default databases
            if (string.IsNullOrEmpty(database))
                database = "master";

            var connectionString = $"Server={sqlServer}; Database={database}; Authentication=Active Directory Password; User ID={username}; Password={password};";
            return CreateSqlConnection(connectionString);
        }
    }
}
