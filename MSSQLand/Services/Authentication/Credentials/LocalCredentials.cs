using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    public class LocalCredentials : BaseCredentials
    {
        public override SqlConnection Authenticate(string sqlServer, string database, string username, string password, string domain = null)
        {
            var connectionString = $"Server={sqlServer}; Database={database}; Integrated Security=False; User Id={username}; Password={password};";
            return CreateSqlConnection(connectionString);
        }
    }
}
