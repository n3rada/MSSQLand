using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;
using System.Xml;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Display detailed technical information about a ConfigMgr deployment type.
    /// Shows detection method, install commands, requirements, content location, and full policy XML.
    /// 
    /// Use Case:
    /// When you need to understand exactly how an application deployment type works - what detection
    /// method is used, what command line is executed, what requirements must be met, and what the
    /// client-side policy looks like. Essential for troubleshooting detection issues, understanding
    /// deployment behavior, or preparing to modify/hijack deployment types.
    /// 
    /// Input: Deployment Type CI_ID (get from cm-trace, cm-applications, or database queries)
    /// 
    /// Information Displayed:
    /// - Deployment type metadata (CI_ID, CI_UniqueID, title, version, enabled/expired status)
    /// - Technology type (Script, MSI, App-V, AppX, etc.)
    /// - Parent application information
    /// - Install command line and execution context
    /// - Content location (source path) and file details
    /// - Detection method (parsed summary + full XML)
    /// - Requirements rules (OS version, computer name filters, etc.)
    /// - Exit codes (success, reboot, error handling)
    /// - Full Policy Platform XML (what the client receives in WMI)
    /// - Full SDM Package Digest XML (System Definition Model)
    /// 
    /// XML Documents Explained:
    /// - Policy Platform (CI_DocumentStore.Body): The compiled policy sent to clients, stored in
    ///   root\ccm\CIModels WMI namespace. This is what AppDiscovery.log and AppEnforce.log use.
    /// - SDM Package Digest (CI_ConfigurationItems.SDMPackageDigest): The System Definition Model
    ///   representation of the deployment type, used by the ConfigMgr console and policy compilation.
    /// 
    /// Examples:
    /// cm-dt 16891057
    /// cm-dt 16891057 --xml   (includes full XML output)
    /// 
    /// Typical Workflow:
    /// 1. Run cm-trace with GUID from log to get CI_ID
    /// 2. Run cm-dt with CI_ID to see technical details
    /// 3. Analyze detection method to understand why detection is failing
    /// 4. Check requirements to see if device meets deployment criteria
    /// 5. Review install command line for troubleshooting or modification
    /// </summary>
    internal class CMDeploymentType : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Deployment Type CI_ID (e.g., 16891057)")]
        private string _ciId = "";

        [ArgumentMetadata(LongName = "xml", Description = "Include full Policy Platform and SDM Package Digest XML output")]
        private bool _showXml = false;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _ciId = GetPositionalArgument(positional, 0, "")
                 ?? GetNamedArgument(named, "ci-id", null)
                 ?? GetNamedArgument(named, "id", null)
                 ?? "";

            if (string.IsNullOrWhiteSpace(_ciId))
            {
                throw new ArgumentException("Deployment Type CI_ID is required. Example: cm-dt 16891057");
            }

            if (!int.TryParse(_ciId, out _))
            {
                throw new ArgumentException($"Invalid CI_ID: {_ciId}. Must be a numeric CI_ID.");
            }

            string xmlStr = GetNamedArgument(named, "xml", null);
            if (!string.IsNullOrEmpty(xmlStr))
            {
                _showXml = bool.Parse(xmlStr);
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            int ciId = int.Parse(_ciId);

            Logger.TaskNested($"Retrieving deployment type details for CI_ID: {ciId}");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No ConfigMgr databases found");
                return null;
            }

            bool found = false;

            foreach (string db in databases)
            {
                string siteCode = CMService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");
                Logger.NewLine();

                // Get deployment type details
                string dtQuery = $@"
SELECT
    ci.CI_ID,
    ci.CI_UniqueID,
    ci.CIVersion,
    ci.IsEnabled,
    ci.IsExpired,
    ci.IsHidden,
    ci.DateCreated,
    ci.CreatedBy,
    ci.DateLastModified,
    ci.LastModifiedBy,
    ci.SourceSite,
    ci.SDMPackageDigest,
    lcp.Title,
    lcp.Description,
    lcp.Publisher,
    lcp.Version AS LocalizedVersion
FROM [{db}].dbo.CI_ConfigurationItems ci
LEFT JOIN [{db}].dbo.CI_LocalizedCIClientProperties lcp ON ci.CI_ID = lcp.CI_ID AND lcp.LocaleID = 1033
WHERE ci.CI_ID = {ciId} AND ci.CIType_ID = 21;";

                DataTable dtResult = databaseContext.QueryService.ExecuteTable(dtQuery);

                if (dtResult.Rows.Count == 0)
                {
                    Logger.Warning($"Deployment Type CI_ID {ciId} not found in {db}");
                    continue;
                }

                found = true;
                DataRow dt = dtResult.Rows[0];

                // Display basic information
                Logger.Success($"Deployment Type: {dt["Title"]}");
                Logger.SuccessNested($"CI_ID: {dt["CI_ID"]}");
                Logger.SuccessNested($"CI_UniqueID: {dt["CI_UniqueID"]}");
                Logger.SuccessNested($"CI Version: {dt["CIVersion"]} (increments with each revision)");
                Logger.SuccessNested($"Enabled: {dt["IsEnabled"]}");
                Logger.SuccessNested($"Expired: {dt["IsExpired"]}");
                Logger.SuccessNested($"Hidden: {dt["IsHidden"]}");

                if (dt["Description"] != DBNull.Value && !string.IsNullOrWhiteSpace(dt["Description"].ToString()))
                {
                    Logger.SuccessNested($"Description: {dt["Description"]}");
                }

                Logger.SuccessNested($"Created: {Convert.ToDateTime(dt["DateCreated"]):yyyy-MM-dd HH:mm:ss} UTC by {dt["CreatedBy"]}");
                Logger.SuccessNested($"Last Modified: {Convert.ToDateTime(dt["DateLastModified"]):yyyy-MM-dd HH:mm:ss} UTC by {dt["LastModifiedBy"]}");
                
                if (dt["SourceSite"] != DBNull.Value && !string.IsNullOrWhiteSpace(dt["SourceSite"].ToString()))
                {
                    Logger.SuccessNested($"Source Site: {dt["SourceSite"]}");
                }

                // Parse SDM Package Digest for details (parse once, query many times)
                string sdmXml = dt["SDMPackageDigest"].ToString();

                Logger.NewLine();
                Logger.Info("Technical Details");

                try
                {
                    XmlDocument sdmDoc = new XmlDocument();
                    sdmDoc.LoadXml(sdmXml);

                    XmlNamespaceManager sdmNs = new XmlNamespaceManager(sdmDoc.NameTable);
                    sdmNs.AddNamespace("p1", "http://schemas.microsoft.com/SystemCenterConfigurationManager/2009/AppMgmtDigest");

                    // Extract technology type
                    string technology = sdmDoc.SelectSingleNode("//p1:Technology", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(technology))
                    {
                        Logger.InfoNested($"Technology: {technology}");
                    }

                    string hosting = sdmDoc.SelectSingleNode("//p1:Hosting", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(hosting))
                    {
                        Logger.InfoNested($"Hosting: {hosting}");
                    }

                    // Extract install command
                    string installCmd = sdmDoc.SelectSingleNode("//p1:InstallCommandLine", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(installCmd))
                    {
                        Logger.InfoNested($"Install Command: {installCmd}");
                    }

                    // Extract uninstall settings
                    string uninstallSetting = sdmDoc.SelectSingleNode("//p1:UninstallSetting", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(uninstallSetting))
                    {
                        Logger.InfoNested($"Uninstall Setting: {uninstallSetting}");
                    }

                    string uninstallCmd = sdmDoc.SelectSingleNode("//p1:UninstallCommandLine", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(uninstallCmd))
                    {
                        Logger.InfoNested($"Uninstall Command: {uninstallCmd}");
                    }

                    string allowUninstall = sdmDoc.SelectSingleNode("//p1:AllowUninstall", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(allowUninstall))
                    {
                        Logger.InfoNested($"Allow Uninstall: {allowUninstall}");
                    }

                    // Extract execution context
                    string execContext = sdmDoc.SelectSingleNode("//p1:ExecutionContext", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(execContext))
                    {
                        Logger.InfoNested($"Execution Context: {execContext}");
                    }

                    string workingDir = sdmDoc.SelectSingleNode("//p1:Arg[@Name='WorkingDirectory']", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(workingDir))
                    {
                        Logger.InfoNested($"Working Directory: {workingDir}");
                    }

                    // Extract content location and settings
                    string contentLocation = sdmDoc.SelectSingleNode("//p1:Location", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(contentLocation))
                    {
                        Logger.InfoNested($"Content Location: {contentLocation}");
                    }

                    string onFastNetwork = sdmDoc.SelectSingleNode("//p1:OnFastNetwork", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(onFastNetwork))
                    {
                        Logger.InfoNested($"On Fast Network: {onFastNetwork}");
                    }

                    string onSlowNetwork = sdmDoc.SelectSingleNode("//p1:OnSlowNetwork", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(onSlowNetwork))
                    {
                        Logger.InfoNested($"On Slow Network: {onSlowNetwork}");
                    }

                    string peerCache = sdmDoc.SelectSingleNode("//p1:PeerCache", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(peerCache))
                    {
                        Logger.InfoNested($"Peer Cache: {peerCache}");
                    }

                    // Extract file name and size
                    XmlNode fileNode = sdmDoc.SelectSingleNode("//p1:File", sdmNs);
                    if (fileNode != null)
                    {
                        string fileName = fileNode.Attributes["Name"]?.Value;
                        string fileSize = fileNode.Attributes["Size"]?.Value;
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            string sizeInfo = !string.IsNullOrEmpty(fileSize) ? $" ({long.Parse(fileSize):N0} bytes)" : "";
                            Logger.InfoNested($"File: {fileName}{sizeInfo}");
                        }
                    }

                    // Extract execution parameters
                    string maxExecTime = sdmDoc.SelectSingleNode("//p1:Arg[@Name='MaxExecuteTime']", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(maxExecTime))
                    {
                        Logger.InfoNested($"Max Execution Time: {maxExecTime} minutes");
                    }

                    string runAs32Bit = sdmDoc.SelectSingleNode("//p1:Arg[@Name='RunAs32Bit']", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(runAs32Bit))
                    {
                        Logger.InfoNested($"Run as 32-bit: {runAs32Bit}");
                    }

                    string postInstallBehavior = sdmDoc.SelectSingleNode("//p1:Arg[@Name='PostInstallBehavior']", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(postInstallBehavior))
                    {
                        Logger.InfoNested($"Post-Install Behavior: {postInstallBehavior}");
                    }

                    string requiresElevation = sdmDoc.SelectSingleNode("//p1:Arg[@Name='RequiresElevatedRights']", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(requiresElevation))
                    {
                        Logger.InfoNested($"Requires Elevated Rights: {requiresElevation}");
                    }

                    string requiresUserInteraction = sdmDoc.SelectSingleNode("//p1:Arg[@Name='RequiresUserInteraction']", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(requiresUserInteraction))
                    {
                        Logger.InfoNested($"Requires User Interaction: {requiresUserInteraction}");
                    }

                    string requiresReboot = sdmDoc.SelectSingleNode("//p1:Arg[@Name='RequiresReboot']", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(requiresReboot))
                    {
                        Logger.InfoNested($"Requires Reboot: {requiresReboot}");
                    }

                    string userInteractionMode = sdmDoc.SelectSingleNode("//p1:Arg[@Name='UserInteractionMode']", sdmNs)?.InnerText;
                    if (!string.IsNullOrEmpty(userInteractionMode))
                    {
                        Logger.InfoNested($"User Interaction Mode: {userInteractionMode}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Could not parse technical details: {ex.Message}");
                }

                // Find parent application
                Logger.NewLine();
                Logger.Info("Parent Application");

                string parentQuery = $@"
SELECT 
    ci.CI_ID,
    ci.CI_UniqueID,
    COALESCE(lp.DisplayName, lcp.Title) AS ApplicationName
FROM [{db}].dbo.CI_ConfigurationItemRelations rel
INNER JOIN [{db}].dbo.CI_ConfigurationItems ci ON rel.FromCI_ID = ci.CI_ID
LEFT JOIN [{db}].dbo.v_LocalizedCIProperties lp ON ci.CI_ID = lp.CI_ID AND lp.LocaleID = 1033
LEFT JOIN [{db}].dbo.CI_LocalizedCIClientProperties lcp ON ci.CI_ID = lcp.CI_ID AND lcp.LocaleID = 1033
WHERE rel.ToCI_ID = {ciId} AND rel.RelationType = 9;";

                DataTable parentResult = databaseContext.QueryService.ExecuteTable(parentQuery);

                if (parentResult.Rows.Count > 0)
                {
                    Logger.InfoNested($"Application: {parentResult.Rows[0]["ApplicationName"]} (CI_ID: {parentResult.Rows[0]["CI_ID"]})");
                }
                else
                {
                    Logger.Warning("No parent application found");
                }

                // Display detection method summary
                Logger.NewLine();
                Logger.Info("Detection Method");

                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(sdmXml);

                    XmlNamespaceManager nsManager = new XmlNamespaceManager(doc.NameTable);
                    nsManager.AddNamespace("p1", "http://schemas.microsoft.com/SystemCenterConfigurationManager/2009/AppMgmtDigest");
                    nsManager.AddNamespace("dc", "http://schemas.microsoft.com/SystemsCenterConfigurationManager/2009/07/10/DesiredConfiguration");

                    // Check detection method type
                    XmlNode detectionMethodNode = doc.SelectSingleNode("//p1:DetectionMethod", nsManager);
                    if (detectionMethodNode != null)
                    {
                        string detectionType = detectionMethodNode.InnerText;
                        Logger.InfoNested($"Detection Type: {detectionType}");

                        if (detectionType == "Enhanced")
                        {
                            // Parse enhanced detection settings
                            XmlNodeList settingNodes = doc.SelectNodes("//p1:EnhancedDetectionMethod//dc:*", nsManager);
                            
                            foreach (XmlNode settingNode in settingNodes)
                            {
                                string settingType = settingNode.LocalName;
                                
                                switch (settingType)
                                {
                                    case "File":
                                        string filePath = settingNode.SelectSingleNode("dc:Path", nsManager)?.InnerText;
                                        string filter = settingNode.SelectSingleNode("dc:Filter", nsManager)?.InnerText;
                                        Logger.InfoNested($"\tFile Detection: {filePath}\\{filter}");
                                        break;
                                    case "Folder":
                                        string folderPath = settingNode.SelectSingleNode("dc:Path", nsManager)?.InnerText;
                                        string folderFilter = settingNode.SelectSingleNode("dc:Filter", nsManager)?.InnerText;
                                        Logger.InfoNested($"\tFolder Detection: {folderPath}\\{folderFilter}");
                                        break;
                                    case "RegistryKey":
                                        string regKey = settingNode.SelectSingleNode("dc:Key", nsManager)?.InnerText;
                                        Logger.InfoNested($"\tRegistry Key: {regKey}");
                                        break;
                                    case "RegistrySetting":
                                        string regSettingKey = settingNode.SelectSingleNode("dc:Key", nsManager)?.InnerText;
                                        string valueName = settingNode.SelectSingleNode("dc:ValueName", nsManager)?.InnerText;
                                        Logger.InfoNested($"\tRegistry Value: {regSettingKey}\\{valueName}");
                                        break;
                                }
                            }
                        }
                        else if (detectionType == "ProductCode")
                        {
                            string productCode = doc.SelectSingleNode("//p1:ProductCode", nsManager)?.InnerText;
                            Logger.InfoNested($"\tMSI Product Code: {productCode}");
                        }
                        else if (detectionType == "Custom")
                        {
                            Logger.InfoNested($"\tCustom script detection (see XML for details)");
                        }
                    }
                }
                catch
                {
                    Logger.Warning("Could not parse detection method details");
                }

                // Display requirements
                Logger.NewLine();
                Logger.Info("Requirements");

                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(sdmXml);

                    XmlNamespaceManager nsManager = new XmlNamespaceManager(doc.NameTable);
                    nsManager.AddNamespace("p1", "http://schemas.microsoft.com/SystemCenterConfigurationManager/2009/AppMgmtDigest");
                    nsManager.AddNamespace("rules", "http://schemas.microsoft.com/SystemsCenterConfigurationManager/2009/06/14/Rules");

                    XmlNodeList ruleNodes = doc.SelectNodes("//p1:Requirements/rules:Rule", nsManager);
                    
                    if (ruleNodes.Count > 0)
                    {
                        foreach (XmlNode ruleNode in ruleNodes)
                        {
                            string displayName = ruleNode.SelectSingleNode("rules:Annotation/rules:DisplayName/@Text", nsManager)?.Value;
                            string operatorType = ruleNode.SelectSingleNode("rules:Expression/rules:Operator", nsManager)?.InnerText;
                            
                            if (!string.IsNullOrEmpty(displayName))
                            {
                                Logger.InfoNested($"\t{displayName} (Operator: {operatorType})");
                            }
                            else
                            {
                                Logger.InfoNested($"\tRequirement with operator: {operatorType}");
                            }
                        }
                    }
                    else
                    {
                        Logger.InfoNested("No requirements configured");
                    }
                }
                catch
                {
                    Logger.Warning("Could not parse requirements");
                }

                // Display exit codes
                Logger.NewLine();
                Logger.Info("Exit Codes");

                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(sdmXml);

                    XmlNamespaceManager nsManager = new XmlNamespaceManager(doc.NameTable);
                    nsManager.AddNamespace("p1", "http://schemas.microsoft.com/SystemCenterConfigurationManager/2009/AppMgmtDigest");

                    XmlNodeList exitCodeNodes = doc.SelectNodes("//p1:ExitCode", nsManager);
                    
                    if (exitCodeNodes.Count > 0)
                    {
                        foreach (XmlNode exitCodeNode in exitCodeNodes)
                        {
                            string code = exitCodeNode.Attributes["Code"]?.Value;
                            string codeClass = exitCodeNode.Attributes["Class"]?.Value;
                            Logger.InfoNested($"\t{code}: {codeClass}");
                        }
                    }
                }
                catch
                {
                    Logger.Warning("Could not parse exit codes");
                }

                // Get document store information
                Logger.NewLine();
                Logger.Info("Policy Document Information");

                string docQuery = $@"
SELECT TOP 1 
    ds.Document_ID,
    ds.DocumentIdentifier,
    ds.DocumentType,
    ds.IsVersionLatest,
    ds.Body
FROM [{db}].dbo.CI_CIDocuments cid
INNER JOIN [{db}].dbo.CI_DocumentStore ds ON cid.Document_ID = ds.Document_ID
WHERE cid.CI_ID = {ciId} AND ds.IsVersionLatest = 1
ORDER BY ds.Document_ID DESC;";

                DataTable docResult = databaseContext.QueryService.ExecuteTable(docQuery);

                if (docResult.Rows.Count > 0)
                {
                    DataRow docRow = docResult.Rows[0];
                    int docType = Convert.ToInt32(docRow["DocumentType"]);
                    string docTypeName = docType switch
                    {
                        0 => "Desired Configuration (DCM)",
                        1 => "Compliance Settings",
                        2 => "Configuration Baseline",
                        3 => "Policy Platform (DCM Application/Deployment Type)",
                        _ => $"Unknown ({docType})"
                    };
                    
                    Logger.InfoNested($"Document ID: {docRow["Document_ID"]}");
                    Logger.InfoNested($"Document Identifier: {docRow["DocumentIdentifier"]}");
                    Logger.InfoNested($"Document Type: {docTypeName}");
                    Logger.InfoNested($"Is Latest Version: {docRow["IsVersionLatest"]} (only latest is sent to clients)");

                    // Show XML if requested
                    if (_showXml)
                    {
                        string policyXml = docRow["Body"].ToString();

                        Logger.NewLine();
                        Logger.Info("Policy Platform Document Body (WMI/MOF format)");
                        Logger.InfoNested("This is the compiled policy sent to clients and stored in root\\ccm\\CIModels");
                        Console.WriteLine(Misc.BeautifyXml(policyXml));

                        Logger.NewLine();
                        Logger.Info("SDM Package Digest (System Definition Model)");
                        Logger.InfoNested("This is the ConfigMgr console representation used for policy compilation");
                        Console.WriteLine(Misc.BeautifyXml(sdmXml));
                    }
                    else
                    {
                        Logger.NewLine();
                        Logger.Info("To view full Policy Platform and SDM XML, add --xml flag");
                    }
                }
                else
                {
                    Logger.Warning("No policy document found");
                }

                break; // Found it, no need to check other databases
            }

            if (!found)
            {
                Logger.Warning($"Deployment Type Configuration Item (CI_ID {ciId}) not found in any ConfigMgr database");
                Logger.WarningNested("Make sure this is a valid deployment type CI_ID (CIType_ID = 21)");
            }

            return null;
        }
    }
}
