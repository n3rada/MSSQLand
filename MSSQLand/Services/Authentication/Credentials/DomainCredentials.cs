using System;
using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    public class DomainCredentials : BaseCredentials
    {
        public override SqlConnection Authenticate(string sqlServer, string database, string username, string password, string domain)
        {
            using (new WindowsIdentityImpersonation(domain, username, password))
            {
                var connectionString = $"Server={sqlServer}; Database={database}; Integrated Security=True;";
                return CreateSqlConnection(connectionString);
            }
        }
    }
}
