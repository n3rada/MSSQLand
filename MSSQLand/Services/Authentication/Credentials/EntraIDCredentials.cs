using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    public class EntraIDCredentials : BaseCredentials
    {
        public EntraIDCredentials(Server server) : base(server) { }

        /// <summary>
        /// Factory method for creating EntraIDCredentials instances.
        /// </summary>
        public static EntraIDCredentials Create(Server server) => new EntraIDCredentials(server);

        public override SqlConnection Authenticate(string username, string password, string domain)
        {
            username = $"{username}@{domain}";
            
            // Azure SQL requires proper certificate validation
            TrustServerCertificate = false;

            // Azure SQL Database requires a database - default to master if not specified
            // Unlike on-premises SQL Server, Azure doesn't have login-level default databases
            string database = Server.Database ?? "master";

            var connectionString = $"Server={Server.GetConnectionTarget()}; Database={database}; Authentication=Active Directory Password; User ID={username}; Password={password};";
            return CreateSqlConnection(connectionString);
        }
    }
}
