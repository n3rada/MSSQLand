using MSSQLand.Utilities;
using System;
using System.Data.SqlClient;
using System.Reflection;
using System.Text;

namespace MSSQLand.Services.Credentials
{
    public abstract class BaseCredentials
    {

        private readonly int _connectTimeout = 15;

        /// <summary>
        /// Indicates whether the current authentication attempt was successful.
        /// </summary>
        public bool IsAuthenticated { get; protected set; } = false;

        /// <summary>
        /// Abstract method to be implemented by derived classes for unique authentication logic.
        /// </summary>
        public abstract SqlConnection Authenticate(string sqlServer, string database, string username = null, string password = null, string domain = null);

        /// <summary>
        /// Creates and opens a SQL connection with a specified connection string.
        /// </summary>
        /// <param name="connectionString">The SQL connection string.</param>
        /// <returns>An open <see cref="SqlConnection"/> object.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the connection could not be opened.</exception>
        /// <exception cref="SqlException">Thrown for SQL-related issues (e.g., network errors, authentication issues).</exception>
        protected SqlConnection CreateSqlConnection(string connectionString)
        {
            string appName = GenerateRealisticAppName();
            string workstationId = GenerateRealisticWorkstationId();

            connectionString = $"{connectionString.TrimEnd(';')}; Connect Timeout={_connectTimeout}; Application Name={appName}; Workstation Id={workstationId}";

            Logger.Task($"Trying to connect with {GetName()}");
            Logger.TaskNested($"Connection timeout: {_connectTimeout} seconds");
            Logger.DebugNested(connectionString);

            SqlConnection connection = new(connectionString);

            try
            {
                connection.Open();

                Logger.Success($"Connection opened successfully");
                Logger.SuccessNested($"Server: {connection.DataSource}");
                Logger.SuccessNested($"Database: {connection.Database}");
                Logger.SuccessNested($"Server Version: {connection.ServerVersion}");
                Logger.SuccessNested($"Client Workstation ID: {connection.WorkstationId}");
                Logger.SuccessNested($"Client Connection ID: {connection.ClientConnectionId}");

                return connection;
            }
            catch (SqlException ex)
            {
                Logger.Error($"SQL error while opening connection: {ex.Message}");
                connection.Dispose();
                return null;
            }
            catch (InvalidOperationException ex)
            {
                Logger.Error($"Invalid operation while opening connection: {ex.Message}");
                connection.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected error while opening connection: {ex.Message}");
                connection.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Generates a realistic application name with version.
        /// </summary>
        /// <returns>A realistic application name string.</returns>
        protected string GenerateRealisticAppName()
        {
            // Common patterns for SQL client applications
            string[] appPrefixes = new[]
            {
                "Microsoft SQL Server Management Studio",
                "SQLCMD",
                "SQL Server Data Tools",
                "Azure Data Studio",
                "SQL Server Integration Services",
                "Entity Framework Core",
                "ADO.NET",
                "Dapper",
                "SQL Server PowerShell Extensions"
            };

            // Version patterns that look realistic
            string[] versionPatterns = new[]
            {
                "15.0",    // SQL Server 2019
                "16.0",    // SQL Server 2022
                "18.0",    // SSMS 18.x
                "19.0",    // SSMS 19.x
                "1.45",    // Azure Data Studio style
                "6.0",     // .NET 6
                "7.0",     // .NET 7
                "8.0"      // .NET 8
            };

            Random random = new();
            string prefix = appPrefixes[random.Next(appPrefixes.Length)];
            string version = versionPatterns[random.Next(versionPatterns.Length)];
            
            // Add minor version and build number for extra realism
            int minor = random.Next(0, 10);
            int build = random.Next(1000, 9999);

            return $"{prefix} - {version}.{minor}.{build}";
        }

        /// <summary>
        /// Generates a realistic Windows workstation ID.
        /// Supports various patterns: DESKTOP-*, laptop/workstation names, domain-joined patterns.
        /// </summary>
        /// <returns>A randomly generated realistic workstation ID.</returns>
        protected string GenerateRealisticWorkstationId()
        {
            Random random = new();
            
            // Different workstation naming patterns used in corporate environments
            string[] patterns = new[]
            {
                GenerateDesktopPattern(random),      // DESKTOP-XXXXXXX (Windows default)
                GenerateLaptopPattern(random),       // LAPTOP-XXXXXXX or specific models
                GenerateWorkstationPattern(random),  // WKS-XXX-XXXX (corporate pattern)
            };

            return patterns[random.Next(patterns.Length)];
        }

        private string GenerateDesktopPattern(Random random)
        {
            const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            StringBuilder result = new("DESKTOP-", 15);
            
            // Generate 7 random alphanumeric characters
            for (int i = 0; i < 7; i++)
            {
                result.Append(chars[random.Next(chars.Length)]);
            }
            
            return result.ToString();
        }

        private string GenerateLaptopPattern(Random random)
        {
            string[] laptopPrefixes = new[] { "LAPTOP", "NB", "NOTEBOOK" };
            const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            
            string prefix = laptopPrefixes[random.Next(laptopPrefixes.Length)];
            StringBuilder result = new($"{prefix}-", 15);
            
            // Generate 7 random alphanumeric characters
            for (int i = 0; i < 7; i++)
            {
                result.Append(chars[random.Next(chars.Length)]);
            }
            
            return result.ToString();
        }

        private string GenerateWorkstationPattern(Random random)
        {
            string[] prefixes = new[] { "WKS", "WKSTN", "PC", "COMP" };
            string prefix = prefixes[random.Next(prefixes.Length)];
            
            // Pattern: WKS-XXX-XXXX or similar
            int deptNumber = random.Next(100, 999);
            int compNumber = random.Next(1000, 9999);
            
            return $"{prefix}-{deptNumber}-{compNumber}";
        }

        /// <summary>
        /// Returns the name of the class as a string.
        /// </summary>
        /// <returns>The name of the current class.</returns>
        public string GetName()
        {
            return GetType().Name;
        }

    }
}
