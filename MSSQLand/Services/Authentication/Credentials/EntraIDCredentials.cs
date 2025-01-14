using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    public class EntraIDCredentials : BaseCredentials
    {
        public override SqlConnection Authenticate(string sqlServer, string database, string username, string password, string domain)
        {
            username = $"{username}@{domain}";
            var connectionString = $"Server={sqlServer}; Database={database}; Authentication=Active Directory Password; Encrypt=True; TrustServerCertificate=False; User ID={username}; Password={password};";
            return CreateSqlConnection(connectionString);
        }
    }
}
