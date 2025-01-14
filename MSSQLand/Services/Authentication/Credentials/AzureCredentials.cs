using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    public class AzureCredentials : BaseCredentials
    {
        public override SqlConnection Authenticate(string sqlServer, string database, string username, string password, string domain = null)
        {
            var connectionString = $"Server={sqlServer}; Database={database}; TrustServerCertificate=False; Encrypt=True; User Id={username}; Password={password};";
            return CreateSqlConnection(connectionString);
        }
    }
}
