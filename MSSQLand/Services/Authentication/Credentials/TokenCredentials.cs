using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    public class TokenCredentials : BaseCredentials
    {
        public TokenCredentials(Server server) : base(server) { }

        public override SqlConnection Authenticate(string username = null, string password = null, string domain = null)
        {
            // Connection string with Integrated Security (uses current token)
            var connectionString = $"Server={Server.GetConnectionTarget()}; Integrated Security=True;";
            return CreateSqlConnection(connectionString);
        }
    }
}
