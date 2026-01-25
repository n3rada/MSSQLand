using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    public class TokenCredentials : BaseCredentials
    {
        public override SqlConnection Authenticate(string sqlServer, string database, string username = null, string password = null, string domain = null)
        {
            // Database is optional - if not specified, uses login's default database
            var connectionString = $"Server={sqlServer};{(string.IsNullOrEmpty(database) ? "" : $" Database={database};")} Integrated Security=True;";
            return CreateSqlConnection(connectionString, sqlServer);
        }
    }
}
