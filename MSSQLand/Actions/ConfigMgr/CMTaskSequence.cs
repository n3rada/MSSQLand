using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Display detailed information about a specific ConfigMgr Task Sequence including all referenced content.
    /// 
    /// PackageID uniquely identifies a task sequence (1:1 relationship). Task sequences are packages 
    /// and each has a unique PackageID (e.g., PSC00001) that serves as the primary key.
    /// 
    /// Shows packages, drivers, applications, OS images, and boot images used in the task sequence.
    /// Use this to analyze what content is deployed by a specific task sequence.
    /// </summary>
    internal class CMTaskSequence : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Task Sequence PackageID (e.g., PSC002C0)")]
        private string _packageId = "";

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _packageId = GetPositionalArgument(positional, 0, "");

            if (string.IsNullOrEmpty(_packageId))
            {
                throw new ArgumentException("Task Sequence PackageID is required");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving task sequence details for: {_packageId}");

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

                // Get task sequence details (including Sequence XML)
                string tsQuery = $@"
SELECT 
    ts.PkgID AS PackageID,
    ts.Name,
    ts.Description,
    ts.Version,
    ts.Manufacturer,
    ts.Language,
    ts.SourceDate,
    ts.SourceVersion,
    ts.Source AS SourcePath,
    ts.StoredPkgPath,
    ts.LastRefresh AS LastRefreshTime,
    ts.BootImageID,
    bi.Name AS BootImageName,
    ts.TS_Type,
    ts.TS_Flags,
    ts.Sequence,
    (
        SELECT COUNT(*) 
        FROM [{db}].dbo.v_TaskSequenceReferencesInfo ref 
        WHERE ref.PackageID = ts.PkgID
    ) AS ReferencedContentCount
FROM [{db}].dbo.vSMS_TaskSequencePackage ts
LEFT JOIN [{db}].dbo.v_BootImagePackage bi ON ts.BootImageID = bi.PackageID
WHERE ts.PkgID = '{_packageId.Replace("'", "''")}'
";

                DataTable tsResult = databaseContext.QueryService.ExecuteTable(tsQuery);

                if (tsResult.Rows.Count == 0)
                {
                    Logger.Warning($"Task sequence '{_packageId}' not found");
                    continue;
                }

                DataRow tsRow = tsResult.Rows[0];
                string name = tsRow["Name"].ToString();
                string description = tsRow["Description"].ToString();
                
                // Sequence is stored as compressed binary (varbinary), need to decompress
                byte[] sequenceBinary = tsRow["Sequence"] as byte[];
                string sequenceXml = "";
                
                if (sequenceBinary != null && sequenceBinary.Length > 0)
                {
                    try
                    {
                        sequenceXml = DecompressSequence(sequenceBinary);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to decompress sequence data: {ex.Message}");
                    }
                }
                
                int refCount = tsRow["ReferencedContentCount"] != DBNull.Value ? Convert.ToInt32(tsRow["ReferencedContentCount"]) : 0;

                Logger.NewLine();
                Logger.Success($"Task Sequence: {name} ({_packageId})");
                if (!string.IsNullOrEmpty(description))
                {
                    Logger.Info($"Description: {description}");
                }
                Logger.Info($"Referenced Content Count: {refCount}");

                // Parse and display task sequence steps
                if (!string.IsNullOrEmpty(sequenceXml))
                {
                    Logger.NewLine();
                    Logger.Info("Task Sequence Steps (Execution Order)");
                    ParseAndDisplaySequenceSteps(sequenceXml);
                }

                Logger.NewLine();
                Logger.Info("Task Sequence Properties");
                
                // Remove Sequence XML from display (too large)
                tsResult.Columns.Remove("Sequence");
                Console.WriteLine(OutputFormatter.ConvertDataTable(tsResult));

                // Get referenced content
                if (refCount > 0)
                {
                    Logger.NewLine();
                    Logger.Info($"Referenced Content ({refCount} item(s))");

                    string refQuery = $@"
SELECT 
    ref.ReferencePackageID,
    ref.ReferencePackageType AS ReferencePackageTypeRaw,
    ref.ReferenceName AS ContentName,
    ref.ReferenceVersion AS Version,
    ref.ReferenceDescription AS Description,
    ref.ReferenceProgramName AS ProgramName
FROM [{db}].dbo.v_TaskSequenceReferencesInfo ref
WHERE ref.PackageID = '{_packageId.Replace("'", "''")}'
ORDER BY ref.ReferencePackageType, ref.ReferenceName;
";

                    DataTable refResult = databaseContext.QueryService.ExecuteTable(refQuery);
                    
                    // Add decoded ContentType column and remove raw numeric column
                    DataColumn decodedTypeColumn = refResult.Columns.Add("ContentType", typeof(string));
                    int packageTypeRawIndex = refResult.Columns["ReferencePackageTypeRaw"].Ordinal;
                    decodedTypeColumn.SetOrdinal(packageTypeRawIndex);

                    foreach (DataRow row in refResult.Rows)
                    {
                        row["ContentType"] = CMService.DecodePackageType(row["ReferencePackageTypeRaw"]);
                    }

                    // Remove raw numeric column
                    refResult.Columns.Remove("ReferencePackageTypeRaw");

                    Console.WriteLine(OutputFormatter.ConvertDataTable(refResult));

                    // Summary by content type
                    Logger.NewLine();
                    Logger.Info("Content Summary by Type");
                    string summaryQuery = $@"
SELECT 
    ref.ReferencePackageType AS ReferencePackageTypeRaw,
    COUNT(*) AS Count
FROM [{db}].dbo.v_TaskSequenceReferencesInfo ref
WHERE ref.PackageID = '{_packageId.Replace("'", "''")}'
GROUP BY ref.ReferencePackageType
ORDER BY COUNT(*) DESC;
";

                    DataTable summaryResult = databaseContext.QueryService.ExecuteTable(summaryQuery);
                    
                    // Add decoded ContentType column and remove raw numeric column
                    DataColumn decodedSummaryTypeColumn = summaryResult.Columns.Add("ContentType", typeof(string));
                    int summaryPackageTypeRawIndex = summaryResult.Columns["ReferencePackageTypeRaw"].Ordinal;
                    decodedSummaryTypeColumn.SetOrdinal(summaryPackageTypeRawIndex);

                    foreach (DataRow row in summaryResult.Rows)
                    {
                        row["ContentType"] = CMService.DecodePackageType(row["ReferencePackageTypeRaw"]);
                    }

                    // Remove raw numeric column
                    summaryResult.Columns.Remove("ReferencePackageTypeRaw");

                    Console.WriteLine(OutputFormatter.ConvertDataTable(summaryResult));
                }
                else
                {
                    Logger.Warning("No referenced content found");
                }

                // Get deployments/advertisements for this task sequence
                Logger.NewLine();
                Logger.Info("Task Sequence Deployments");

                string deploymentsQuery = $@"
SELECT 
    adv.AdvertisementID,
    adv.AdvertisementName,
    adv.CollectionID,
    c.Name AS CollectionName,
    c.MemberCount,
    adv.PresentTime,
    adv.ExpirationTime,
    CASE 
        WHEN adv.AdvertFlags & 0x00000020 = 0x00000020 THEN 'Required'
        ELSE 'Available'
    END AS DeploymentType,
    CASE 
        WHEN adv.AdvertFlags & 0x00000400 = 0x00000400 THEN 'Yes'
        ELSE 'No'
    END AS AllowUsersToRunIndependently,
    CASE 
        WHEN adv.AdvertFlags & 0x00008000 = 0x00008000 THEN 'Yes'
        ELSE 'No'
    END AS RerunBehavior
FROM [{db}].dbo.v_Advertisement adv
LEFT JOIN [{db}].dbo.v_Collection c ON adv.CollectionID = c.CollectionID
WHERE adv.PackageID = '{_packageId.Replace("'", "''")}'
ORDER BY adv.PresentTime DESC;";

                DataTable deploymentsResult = databaseContext.QueryService.ExecuteTable(deploymentsQuery);
                
                if (deploymentsResult.Rows.Count > 0)
                {
                    Console.WriteLine(OutputFormatter.ConvertDataTable(deploymentsResult));
                    Logger.Success($"Task sequence deployed to {deploymentsResult.Rows.Count} collection(s)");
                    
                    // Show total potential reach
                    int totalMembers = 0;
                    foreach (DataRow row in deploymentsResult.Rows)
                    {
                        if (row["MemberCount"] != DBNull.Value)
                            totalMembers += Convert.ToInt32(row["MemberCount"]);
                    }
                    Logger.Info($"Total devices potentially targeted: {totalMembers}");
                    Logger.InfoNested("Use 'cm-collection <CollectionID>' to see which devices are in each collection");
                }
                else
                {
                    Logger.Warning("Task sequence not deployed to any collections");
                }

                // Get deployment status summary
                Logger.NewLine();
                Logger.Info("Deployment Status Summary");

                string statusQuery = $@"
SELECT 
    ds.SoftwareName,
    ds.CollectionID,
    c.Name AS CollectionName,
    ds.DeploymentIntent AS DeploymentIntentRaw,
    ds.NumberSuccess,
    ds.NumberInProgress,
    ds.NumberErrors,
    ds.NumberUnknown,
    ds.DeploymentTime,
    ds.ModificationTime
FROM [{db}].dbo.v_DeploymentSummary ds
LEFT JOIN [{db}].dbo.v_Collection c ON ds.CollectionID = c.CollectionID
WHERE ds.PackageID = '{_packageId.Replace("'", "''")}'
    AND ds.FeatureType = 7
ORDER BY ds.DeploymentTime DESC;";

                DataTable statusResult = databaseContext.QueryService.ExecuteTable(statusQuery);
                
                if (statusResult.Rows.Count > 0)
                {
                    // Add decoded DeploymentIntent column and remove raw numeric column
                    DataColumn decodedIntentColumn = statusResult.Columns.Add("Intent", typeof(string));
                    int deploymentIntentRawIndex = statusResult.Columns["DeploymentIntentRaw"].Ordinal;
                    decodedIntentColumn.SetOrdinal(deploymentIntentRawIndex);

                    foreach (DataRow row in statusResult.Rows)
                    {
                        row["Intent"] = CMService.DecodeDeploymentIntent(row["DeploymentIntentRaw"]);
                    }

                    // Remove raw numeric column
                    statusResult.Columns.Remove("DeploymentIntentRaw");

                    Console.WriteLine(OutputFormatter.ConvertDataTable(statusResult));
                }
                else
                {
                    Logger.Info("No deployment status information available");
                }

                break; // Found task sequence, no need to check other databases
            }

            return null;
        }

        private string DecompressSequence(byte[] compressedData)
        {
            // ConfigMgr stores task sequences as GZip-compressed XML
            // Safety check: if compressed data is > 10MB, likely corrupt or wrong column
            if (compressedData.Length > 10 * 1024 * 1024)
            {
                throw new InvalidDataException($"Sequence data too large ({compressedData.Length / 1024 / 1024}MB) - possible data corruption");
            }
            
            // Skip first 4 bytes (size header) if present
            int offset = 0;
            
            // Check for GZip magic number (0x1F 0x8B)
            if (compressedData.Length > 2 && compressedData[0] == 0x1F && compressedData[1] == 0x8B)
            {
                offset = 0; // Already GZip format
            }
            else if (compressedData.Length > 6)
            {
                // Might have 4-byte size header before GZip data
                offset = 4;
            }

            try
            {
                using (var inputStream = new MemoryStream(compressedData, offset, compressedData.Length - offset))
                using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
                using (var outputStream = new MemoryStream())
                {
                    gzipStream.CopyTo(outputStream);
                    byte[] decompressed = outputStream.ToArray();
                    
                    // Safety check: decompressed XML > 50MB is suspicious
                    if (decompressed.Length > 50 * 1024 * 1024)
                    {
                        throw new InvalidDataException($"Decompressed sequence too large ({decompressed.Length / 1024 / 1024}MB) - possible bomb");
                    }
                    
                    return Encoding.UTF8.GetString(decompressed);
                }
            }
            catch
            {
                // If GZip fails, try as plain UTF-8 (some older versions)
                try
                {
                    return Encoding.UTF8.GetString(compressedData);
                }
                catch
                {
                    // Try Unicode
                    return Encoding.Unicode.GetString(compressedData);
                }
            }
        }

        private void ParseAndDisplaySequenceSteps(string sequenceXml)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(sequenceXml);

                // Task sequence steps are in /sequence/group or /sequence/step nodes
                XmlNodeList steps = doc.SelectNodes("//sequence//*[@type]");
                
                if (steps == null || steps.Count == 0)
                {
                    Logger.Warning("No steps found in task sequence");
                    return;
                }

                DataTable stepsTable = new DataTable();
                stepsTable.Columns.Add("Step", typeof(int));
                stepsTable.Columns.Add("Type", typeof(string));
                stepsTable.Columns.Add("Name", typeof(string));
                stepsTable.Columns.Add("Description", typeof(string));
                stepsTable.Columns.Add("Disabled", typeof(string));
                stepsTable.Columns.Add("ContinueOnError", typeof(string));
                stepsTable.Columns.Add("Details", typeof(string));

                int stepNumber = 1;
                ProcessStepsRecursive(steps, stepsTable, ref stepNumber, 0);

                Console.WriteLine(OutputFormatter.ConvertDataTable(stepsTable));
                Logger.Success($"Total steps: {stepsTable.Rows.Count}");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to parse task sequence steps: {ex.Message}");
            }
        }

        private void ProcessStepsRecursive(XmlNodeList nodes, DataTable table, ref int stepNumber, int depth)
        {
            foreach (XmlNode node in nodes)
            {
                if (node.Attributes == null) continue;

                string type = node.Attributes["type"]?.Value ?? "";
                string name = node.Attributes["name"]?.Value ?? "";
                string description = node.Attributes["description"]?.Value ?? "";
                string disabled = node.Attributes["disable"]?.Value == "true" ? "Yes" : "No";
                string continueOnError = node.Attributes["continueOnError"]?.Value == "true" ? "Yes" : "No";

                // Decode common step types
                string decodedType = DecodeStepType(type);
                
                // Extract relevant details based on step type
                string details = ExtractStepDetails(node, type);

                // Add indentation for nested groups
                string indent = new string(' ', depth * 2);
                string displayName = indent + name;

                table.Rows.Add(stepNumber++, decodedType, displayName, description, disabled, continueOnError, details);

                // Process child steps for groups
                if (type == "SMS_TaskSequence_Group")
                {
                    XmlNodeList children = node.SelectNodes("*[@type]");
                    if (children != null && children.Count > 0)
                    {
                        ProcessStepsRecursive(children, table, ref stepNumber, depth + 1);
                    }
                }
            }
        }

        private string DecodeStepType(string type)
        {
            return type switch
            {
                "SMS_TaskSequence_Group" => "Group",
                "SMS_TaskSequence_PartitionDiskAction" => "Partition Disk",
                "SMS_TaskSequence_ApplyOperatingSystemAction" => "Apply OS Image",
                "SMS_TaskSequence_ApplyWindowsSettingsAction" => "Apply Windows Settings",
                "SMS_TaskSequence_ApplyNetworkSettingsAction" => "Apply Network Settings",
                "SMS_TaskSequence_SetVariableAction" => "Set Variable",
                "SMS_TaskSequence_RunCommandLineAction" => "Run Command Line",
                "SMS_TaskSequence_InstallSoftwareAction" => "Install Package",
                "SMS_TaskSequence_InstallApplicationAction" => "Install Application",
                "SMS_TaskSequence_DriverAction" => "Auto Apply Drivers",
                "SMS_TaskSequence_RebootAction" => "Restart Computer",
                "SMS_TaskSequence_JoinDomainWorkgroupAction" => "Join Domain/Workgroup",
                "SMS_TaskSequence_EnableBitLockerAction" => "Enable BitLocker",
                "SMS_TaskSequence_PreProvisionBitLockerAction" => "Pre-provision BitLocker",
                "SMS_TaskSequence_RunPowerShellScriptAction" => "Run PowerShell Script",
                "SMS_TaskSequence_DownloadPackageContentAction" => "Download Package Content",
                "SMS_TaskSequence_CaptureWindowsSettingsAction" => "Capture Windows Settings",
                "SMS_TaskSequence_CaptureNetworkSettingsAction" => "Capture Network Settings",
                "SMS_TaskSequence_ConnectNetworkFolderAction" => "Connect to Network Folder",
                "SMS_TaskSequence_ConditionVariable" => "Condition: Variable",
                _ => type.Replace("SMS_TaskSequence_", "").Replace("Action", "")
            };
        }

        private string ExtractStepDetails(XmlNode node, string type)
        {
            try
            {
                switch (type)
                {
                    case "SMS_TaskSequence_InstallSoftwareAction":
                        var pkgId = node.SelectSingleNode("defaultVarList/variable[@property='PackageID']")?.Attributes?["value"]?.Value;
                        var program = node.SelectSingleNode("defaultVarList/variable[@property='ProgramName']")?.Attributes?["value"]?.Value;
                        return !string.IsNullOrEmpty(pkgId) ? $"Package: {pkgId}, Program: {program}" : "";

                    case "SMS_TaskSequence_RunCommandLineAction":
                        var cmdLine = node.SelectSingleNode("defaultVarList/variable[@property='CommandLine']")?.Attributes?["value"]?.Value;
                        return !string.IsNullOrEmpty(cmdLine) ? $"Command: {cmdLine}" : "";

                    case "SMS_TaskSequence_RunPowerShellScriptAction":
                        var scriptName = node.SelectSingleNode("defaultVarList/variable[@property='ScriptName']")?.Attributes?["value"]?.Value;
                        var sourcePkg = node.SelectSingleNode("defaultVarList/variable[@property='SourcePackageID']")?.Attributes?["value"]?.Value;
                        return !string.IsNullOrEmpty(scriptName) ? $"Script: {scriptName}, Package: {sourcePkg}" : "";

                    case "SMS_TaskSequence_SetVariableAction":
                        var varName = node.SelectSingleNode("defaultVarList/variable[@property='VariableName']")?.Attributes?["value"]?.Value;
                        var varValue = node.SelectSingleNode("defaultVarList/variable[@property='VariableValue']")?.Attributes?["value"]?.Value;
                        return !string.IsNullOrEmpty(varName) ? $"{varName} = {varValue}" : "";

                    case "SMS_TaskSequence_ApplyOperatingSystemAction":
                        var imageId = node.SelectSingleNode("defaultVarList/variable[@property='ImagePackageID']")?.Attributes?["value"]?.Value;
                        return !string.IsNullOrEmpty(imageId) ? $"Image: {imageId}" : "";

                    case "SMS_TaskSequence_DriverAction":
                        var driverPkg = node.SelectSingleNode("defaultVarList/variable[@property='DriverPackageID']")?.Attributes?["value"]?.Value;
                        return !string.IsNullOrEmpty(driverPkg) ? $"Driver Package: {driverPkg}" : "";

                    default:
                        return "";
                }
            }
            catch
            {
                return "";
            }
        }
    }
}
