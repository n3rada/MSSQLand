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
            
            var connectionString = $"Server={sqlServer}; Database={database}; Authentication=Active Directory Password; User ID={username}; Password={password};";
            return CreateSqlConnection(connectionString);
        }
    }
}
