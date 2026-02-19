using MSSQLand.Models;
using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    public class TokenCredentials : BaseCredentials
    {
        public TokenCredentials(Server server) : base(server) { }

        /// <summary>
        /// Factory method for creating TokenCredentials instances.
        /// </summary>
        public static TokenCredentials Create(Server server) => new TokenCredentials(server);

        public override SqlConnection Authenticate(string username = null, string password = null, string domain = null)
        {
            // Connection string with Integrated Security (uses current token)
            var connectionString = $"Data Source={Server.GetConnectionTarget()}; Integrated Security=True;";
            return CreateSqlConnection(connectionString);
        }
    }
}
