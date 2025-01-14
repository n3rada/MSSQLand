using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    public class TokenCredentials : BaseCredentials
    {
        public override SqlConnection Authenticate(string sqlServer, string database, string username = null, string password = null, string domain = null)
        {
            var connectionString = $"Server={sqlServer}; Database={database}; Integrated Security=True;";
            return CreateSqlConnection(connectionString);
        }
    }
}
