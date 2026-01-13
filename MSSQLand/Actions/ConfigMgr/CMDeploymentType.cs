// MSSQLand/Actions/ConfigMgr/CMDeploymentType.cs

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
        [ArgumentMetadata(Position = 0, Required = true, Description = "Deployment Type CI_ID (e.g., 16891057)")]
        private int _ciId = 0;

        [ArgumentMetadata(LongName = "xml", Description = "Include full Policy Platform and SDM Package Digest XML output")]
        private bool _showXml = false;

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving deployment type details for CI_ID: {_ciId}");

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
WHERE ci.CI_ID = {_ciId} AND ci.CIType_ID = 21;";

                DataTable dtResult = databaseContext.QueryService.ExecuteTable(dtQuery);

                if (dtResult.Rows.Count == 0)
                {
                    Logger.Warning($"Deployment Type CI_ID {_ciId} not found in {db}");
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

                // Parse SDM Package Digest for details (centralized parser)
                string sdmXml = dt["SDMPackageDigest"].ToString();
                var sdmInfo = CMService.ParseSDMPackageDigest(sdmXml, detailed: true);

                Logger.NewLine();
                Logger.Info("Technical Details");

                if (!string.IsNullOrEmpty(sdmInfo.Technology))
                    Logger.InfoNested($"Technology: {sdmInfo.Technology}");

                if (!string.IsNullOrEmpty(sdmInfo.Hosting))
                    Logger.InfoNested($"Hosting: {sdmInfo.Hosting}");

                if (!string.IsNullOrEmpty(sdmInfo.InstallCommand))
                    Logger.InfoNested($"Install Command: {sdmInfo.InstallCommand}");

                if (!string.IsNullOrEmpty(sdmInfo.UninstallSetting))
                    Logger.InfoNested($"Uninstall Setting: {sdmInfo.UninstallSetting}");

                if (!string.IsNullOrEmpty(sdmInfo.UninstallCommand))
                    Logger.InfoNested($"Uninstall Command: {sdmInfo.UninstallCommand}");

                if (!string.IsNullOrEmpty(sdmInfo.AllowUninstall))
                    Logger.InfoNested($"Allow Uninstall: {sdmInfo.AllowUninstall}");

                if (!string.IsNullOrEmpty(sdmInfo.ExecutionContext))
                    Logger.InfoNested($"Execution Context: {sdmInfo.ExecutionContext}");

                if (!string.IsNullOrEmpty(sdmInfo.WorkingDirectory))
                    Logger.InfoNested($"Working Directory: {sdmInfo.WorkingDirectory}");

                if (!string.IsNullOrEmpty(sdmInfo.ContentLocation))
                    Logger.InfoNested($"Content Location: {sdmInfo.ContentLocation}");

                if (!string.IsNullOrEmpty(sdmInfo.OnFastNetwork))
                    Logger.InfoNested($"On Fast Network: {sdmInfo.OnFastNetwork}");

                if (!string.IsNullOrEmpty(sdmInfo.OnSlowNetwork))
                    Logger.InfoNested($"On Slow Network: {sdmInfo.OnSlowNetwork}");

                if (!string.IsNullOrEmpty(sdmInfo.PeerCache))
                    Logger.InfoNested($"Peer Cache: {sdmInfo.PeerCache}");

                if (!string.IsNullOrEmpty(sdmInfo.FileName))
                {
                    string sizeInfo = !string.IsNullOrEmpty(sdmInfo.FileSize) 
                        ? $" ({long.Parse(sdmInfo.FileSize):N0} bytes)" 
                        : "";
                    Logger.InfoNested($"File: {sdmInfo.FileName}{sizeInfo}");
                }

                if (!string.IsNullOrEmpty(sdmInfo.MaxExecuteTime))
                    Logger.InfoNested($"Max Execution Time: {sdmInfo.MaxExecuteTime} minutes");

                if (!string.IsNullOrEmpty(sdmInfo.RunAs32Bit))
                    Logger.InfoNested($"Run as 32-bit: {sdmInfo.RunAs32Bit}");

                if (!string.IsNullOrEmpty(sdmInfo.PostInstallBehavior))
                    Logger.InfoNested($"Post-Install Behavior: {sdmInfo.PostInstallBehavior}");

                if (!string.IsNullOrEmpty(sdmInfo.RequiresElevatedRights))
                    Logger.InfoNested($"Requires Elevated Rights: {sdmInfo.RequiresElevatedRights}");

                if (!string.IsNullOrEmpty(sdmInfo.RequiresUserInteraction))
                    Logger.InfoNested($"Requires User Interaction: {sdmInfo.RequiresUserInteraction}");

                if (!string.IsNullOrEmpty(sdmInfo.RequiresReboot))
                    Logger.InfoNested($"Requires Reboot: {sdmInfo.RequiresReboot}");

                if (!string.IsNullOrEmpty(sdmInfo.UserInteractionMode))
                    Logger.InfoNested($"User Interaction Mode: {sdmInfo.UserInteractionMode}");

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
WHERE rel.ToCI_ID = {_ciId} AND rel.RelationType = 9;";

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
WHERE cid.CI_ID = {_ciId} AND ds.IsVersionLatest = 1
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
                Logger.Warning($"Deployment Type Configuration Item (CI_ID {_ciId}) not found in any ConfigMgr database");
                Logger.WarningNested("Make sure this is a valid deployment type CI_ID (CIType_ID = 21)");
            }

            return null;
        }
    }
}
