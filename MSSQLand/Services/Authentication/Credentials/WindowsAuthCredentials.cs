using MSSQLand.Utilities;
using System;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSSQLand.Services.Credentials
{
    /// <summary>
    /// Windows Authentication using TDS protocol with NTLM authentication.
    /// This implementation mimics impacket's mssqlclient.py with -windows-auth flag.
    /// </summary>
    public class WindowsAuthCredentials : BaseCredentials
    {
        private const int DefaultPort = 1433;
        private const int DefaultPacketSize = 32763;

        public override SqlConnection Authenticate(string sqlServer, string database, string username = null, string password = null, string domain = null)
        {
            try
            {
                // Parse server and port
                string server = sqlServer;
                int port = DefaultPort;
                if (sqlServer.Contains(","))
                {
                    var parts = sqlServer.Split(',');
                    server = parts[0];
                    port = int.Parse(parts[1]);
                }
                else if (sqlServer.Contains(":"))
                {
                    var parts = sqlServer.Split(':');
                    server = parts[0];
                    port = int.Parse(parts[1]);
                }

                Logger.Task($"Attempting Windows Authentication via TDS protocol");

                // Perform async authentication
                var result = AuthenticateAsync(server, port, database, username, password, domain).GetAwaiter().GetResult();

                if (result)
                {
                    IsAuthenticated = true;
                    // After successful TDS authentication, create a standard SqlConnection
                    string connectionString = BuildConnectionString(sqlServer, database, username, password, domain);
                    return CreateSqlConnection(connectionString);
                }
                else
                {
                    Logger.Error("Windows Authentication via TDS failed");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Windows Authentication error: {ex.Message}");
                Logger.DebugNested(ex.StackTrace);
                return null;
            }
        }

        private string BuildConnectionString(string sqlServer, string database, string username, string password, string domain)
        {
            // If credentials provided, use SQL auth as fallback
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                return $"Server={sqlServer}; Database={database}; User Id={username}; Password={password};";
            }
            else
            {
                // Use integrated security
                return $"Server={sqlServer}; Database={database}; Integrated Security=True;";
            }
        }

        private async Task<bool> AuthenticateAsync(string server, int port, string database, string username, string password, string domain)
        {
            TdsClient tdsClient = null;

            try
            {
                // 1. Establish TCP connection
                tdsClient = new TdsClient(server, port, DefaultPacketSize);
                Logger.DebugNested($"TCP connection established to {server}:{port}");

                // First things first, we need to announce to the MSSQL server sending a TDS_PRELOGIN
                Logger.DebugNested("Sending PreLogin packet");
                var resp = await tdsClient.PreLoginAsync();
                Logger.DebugNested($"PreLogin response received - Encryption: {resp.Encryption}");

                // If the MSSQL Server responds with a TDS_ENCRYPT_REQ or TDS_ENCRYPT_OFF
                // Then it means we need to setup a TLS context
                bool tlsEnabled = false;
                if (resp.Encryption == TdsEncryption.Required || 
                    resp.Encryption == TdsEncryption.Off)
                {
                    // Setup TLS for both TDS_ENCRYPT_REQ and TDS_ENCRYPT_OFF
                    // For TDS_ENCRYPT_OFF: only the first Login7 packet is encrypted, then we disable TLS
                    // For TDS_ENCRYPT_REQ: all communication is encrypted
                    Logger.DebugNested("Performing TLS handshake");
                    Logger.DebugNested($"Encryption mode: {resp.Encryption} - Setting up TLS context");
                    await tdsClient.PerformTlsHandshakeAsync();
                    tlsEnabled = true;
                    Logger.SuccessNested("TLS handshake completed");
                    Logger.DebugNested($"TLS enabled: {tlsEnabled}, Packet size adjusted to: {tdsClient.PacketSize}");
                }
                else
                {
                    Logger.DebugNested("TLS not required - proceeding without encryption");
                }

                // Get current credentials if not provided
                if (string.IsNullOrEmpty(username))
                {
                    username = Environment.UserName;
                    Logger.DebugNested($"Using current user: {username}");
                }
                if (string.IsNullOrEmpty(domain))
                {
                    domain = Environment.UserDomainName;
                    Logger.DebugNested($"Using current domain: {domain}");
                }

                // That part is used to compute the Version field for the NTLM_NEGOTIATE and NTLM_AUTHENTICATE messages
                var version = new NtlmVersion
                {
                    ProductMajorVersion = 10,
                    ProductMinorVersion = 0,
                    ProductBuild = 20348
                };
                Logger.DebugNested($"NTLM Version set: {version.ProductMajorVersion}.{version.ProductMinorVersion}.{version.ProductBuild}");

                Logger.DebugNested("Preparing Login7 packet");
                string hostName = GenerateRealisticWorkstationId();
                string appName = GenerateRealisticAppName();
                string cltIntName = appName; // Use same as app name
                
                Logger.DebugNested($"Generated names - HostName: {hostName}, AppName: {appName}, CltIntName: {cltIntName}");
                
                var login = new TdsLogin7
                {
                    HostName = hostName,
                    AppName = appName,
                    ServerName = server,
                    CltIntName = cltIntName,
                    ClientPID = (uint)new Random().Next(0, 1024),
                    PacketSize = (uint)tdsClient.PacketSize,
                    Database = database ?? "",
                    UserName = "",
                    Password = "",
                    Language = "",
                    AtchDBFile = ""
                };

                Logger.DebugNested($"Login7 - Server: {server}, Database: {database ?? "(default)"}, PacketSize: {login.PacketSize}");

                // These flags means:
                // TDS_INIT_LANG_FATAL: if we specify a language (let's say fr) and we want the MSSQL server to serve us french
                // But for a reason, it can't, then the connection is closed
                // TDS_ODBC_ON: specifies that we are a ODBC driver (but we are clearly not)
                login.OptionFlags2 = 0x03; // TDS_INIT_LANG_FATAL | TDS_ODBC_ON
                Logger.DebugNested($"OptionFlags2 initial value: 0x{login.OptionFlags2:X2} (TDS_INIT_LANG_FATAL | TDS_ODBC_ON)");

                // Among these fields, the following flag need to be set if we rely on a Windows Authentication (and not local mssql accounts)
                login.OptionFlags2 |= 0x80; // TDS_INTEGRATED_SECURITY_ON
                Logger.DebugNested($"OptionFlags2 after adding TDS_INTEGRATED_SECURITY_ON: 0x{login.OptionFlags2:X2}");

                // We send compute the first NTLM message (NTLMSSP_NEGOTIATE) asking for NTLMv2
                // Indeed NTLMv2 doesn't support CBT nor signing
                Logger.TaskNested("Generating NTLMSSP_NEGOTIATE message");
                var auth = new NtlmNegotiateMessage
                {
                    // Impacket: getNTLMSSPType1('', '', use_ntlmv2=True, version=self.version)
                    Flags = NtlmFlags.NegotiateUnicode |
                           NtlmFlags.RequestTarget |
                           NtlmFlags.NegotiateNTLM |
                           NtlmFlags.NegotiateExtendedSessionSecurity |
                           NtlmFlags.NegotiateTargetInfo |
                           NtlmFlags.Negotiate128 |
                           NtlmFlags.Negotiate56,
                    DomainName = "",
                    WorkstationName = "",
                    Version = version
                };

                Logger.DebugNested($"NTLM Flags: 0x{(uint)auth.Flags:X8}");
                Logger.DebugNested($"Domain: (empty), Workstation: (empty) - matching impacket getNTLMSSPType1");

                // We then fill the TDS_LOGIN["SSPI"] fields with the NTLMSSP_NEGOTIATE packet
                login.SSPI = auth.ToBytes();
                Logger.DebugNested($"NTLMSSP_NEGOTIATE length: {login.SSPI.Length} bytes");
                Logger.DebugNested($"NTLMSSP_NEGOTIATE (first 32 bytes): {BitConverter.ToString(login.SSPI.Take(Math.Min(32, login.SSPI.Length)).ToArray())}");

                // Send the NTLMSSP Negotiate or SQL Auth Packet
                Logger.TaskNested("Sending Login7 packet");
                Logger.DebugNested($"Login7 total size will be: ~{login.SSPI.Length + 86} bytes (86 header + {login.SSPI.Length} SSPI)");
                await tdsClient.SendLogin7Async(login);
                Logger.DebugNested("Login7 packet sent successfully");

                // According to the specs, if encryption is not required, we must encrypt just
                // the first Login packet :-o
                if (resp.Encryption == TdsEncryption.Off)
                {
                    Logger.DebugNested("Encryption was TDS_ENCRYPT_OFF - disabling TLS after Login7 (matching impacket: self.tlsSocket = None)");
                    tdsClient.DisableTlsAfterLogin();
                    tlsEnabled = false;
                    Logger.DebugNested($"TLS disabled, tlsEnabled flag: {tlsEnabled}");
                }

                // We then receive its response which is either
                // - A NTLMSSP_CHALLENGE packet
                // - The response for the local authentication from the MSSQL server
                Logger.TaskNested("Receiving NTLMSSP_CHALLENGE");
                var tds = await tdsClient.ReceiveTdsAsync();
                Logger.DebugNested($"Received TDS packet - Type: {tds.Type}, Length: {tds.Length}, Status: {tds.Status}");
                Logger.DebugNested($"TDS Data length: {tds.Data?.Length ?? 0} bytes");

                // Each TDS packet has a header so we extract the NTLMSSP_CHALLENGE from it
                // serverChallenge = tds['Data'][3:]
                Logger.DebugNested("Extracting NTLMSSP_CHALLENGE (matching impacket: tds['Data'][3:])");
                byte[] serverChallenge = tdsClient.ExtractSspiData(tds.Data);

                if (serverChallenge == null || serverChallenge.Length == 0)
                {
                    Logger.Error("Failed to extract NTLMSSP_CHALLENGE from server response");
                    Logger.DebugNested($"TDS Data (first 50 bytes): {BitConverter.ToString(tds.Data.Take(Math.Min(50, tds.Data.Length)).ToArray())}");
                    return false;
                }

                Logger.DebugNested($"NTLMSSP_CHALLENGE length: {serverChallenge.Length} bytes");
                Logger.DebugNested($"NTLMSSP_CHALLENGE (first 32 bytes): {BitConverter.ToString(serverChallenge.Take(Math.Min(32, serverChallenge.Length)).ToArray())}");

                // Parse NTLM Challenge
                Logger.DebugNested("Parsing NTLM Challenge message");
                var ntlmChallenge = NtlmChallengeMessage.FromBytes(serverChallenge);
                Logger.DebugNested($"Server Challenge (8 bytes): {BitConverter.ToString(ntlmChallenge.ServerChallenge)}");
                Logger.DebugNested($"Challenge Flags: 0x{(uint)ntlmChallenge.Flags:X8}");
                Logger.DebugNested($"Target Name: {ntlmChallenge.TargetName ?? "(null)"}");

                // Generate Client Challenge
                Logger.DebugNested("Generating random client challenge (8 bytes)");
                byte[] clientChallenge = NtlmHelper.GenerateClientChallenge(8);
                Logger.DebugNested($"Client Challenge: {BitConverter.ToString(clientChallenge)}");

                // We then compute the Channel Binding Token from the tls-unique value retrieved before
                byte[] channel_binding_value = new byte[0];
                if (tlsEnabled)
                {
                    Logger.DebugNested("TLS is enabled - generating Channel Binding Token from tls-unique");
                    channel_binding_value = tdsClient.GenerateChannelBindingToken();
                    Logger.DebugNested($"CBT generated: {BitConverter.ToString(channel_binding_value)}");
                }
                else
                {
                    Logger.DebugNested("TLS not enabled - using empty Channel Binding Token");
                }

                // Get target info and timestamp
                byte[] targetInfo = ntlmChallenge.TargetInfo?.ToBytes();
                byte[] timestamp = ntlmChallenge.TargetInfo?.Get(AvPairType.Timestamp);
                Logger.DebugNested($"Target Info length: {targetInfo?.Length ?? 0} bytes");
                Logger.DebugNested($"Timestamp from challenge: {(timestamp != null ? BitConverter.ToString(timestamp) : "(null)")}");

                // Get DNS hostname for SPN (service="MSSQLSvc" in impacket)
                string spn = null;
                if (ntlmChallenge.TargetInfo?.Contains(AvPairType.DnsComputerName) == true)
                {
                    byte[] dnsHostBytes = ntlmChallenge.TargetInfo.Get(AvPairType.DnsComputerName);
                    string dnsHost = Encoding.Unicode.GetString(dnsHostBytes);
                    spn = $"MSSQLSvc/{dnsHost}";
                    Logger.DebugNested($"SPN constructed: {spn}");
                }
                else
                {
                    Logger.DebugNested("No DNS hostname in TargetInfo - SPN will be null");
                }

                // Generate the NTLM ChallengeResponse AUTH
                Logger.DebugNested($"Computing NTLMv2 response for user: {domain}\\{username}");
                
                byte[] ntlmV2Response = NtlmHelper.ComputeNtlmV2Response(
                    username,
                    password,
                    domain,
                    ntlmChallenge.ServerChallenge,
                    targetInfo,
                    clientChallenge,
                    timestamp,
                    channel_binding_value,
                    spn
                );
                Logger.DebugNested($"NTLMv2 Response length: {ntlmV2Response.Length} bytes");
                Logger.DebugNested($"NTProofStr (first 16 bytes): {BitConverter.ToString(ntlmV2Response.Take(16).ToArray())}");

                byte[] lmV2Response = NtlmHelper.ComputeLmV2Response(
                    username,
                    password,
                    domain,
                    ntlmChallenge.ServerChallenge,
                    clientChallenge
                );
                Logger.DebugNested($"LMv2 Response length: {lmV2Response.Length} bytes");

                // Now we initiate the MIC field with 0's
                Logger.DebugNested("Creating NTLM Authenticate message with MIC initialized to zeros");
                var type3 = new NtlmAuthenticateMessage
                {
                    LmChallengeResponse = lmV2Response,
                    NtChallengeResponse = ntlmV2Response,
                    DomainName = domain?.ToUpper() ?? "",
                    UserName = username ?? "",
                    WorkstationName = Environment.MachineName.ToUpper(),
                    EncryptedRandomSessionKey = new byte[0],
                    Flags = NtlmFlags.NegotiateUnicode |
                           NtlmFlags.RequestTarget |
                           NtlmFlags.NegotiateNTLM |
                           NtlmFlags.NegotiateExtendedSessionSecurity |
                           NtlmFlags.NegotiateTargetInfo |
                           NtlmFlags.Negotiate128 |
                           NtlmFlags.Negotiate56,
                    Version = null,
                    MIC = new byte[16] // Now we initiate the MIC field with 0's
                };

                Logger.DebugNested($"Type3 Flags: 0x{(uint)type3.Flags:X8}");
                Logger.DebugNested($"Domain: {type3.DomainName}, User: {type3.UserName}, Workstation: {type3.WorkstationName}");

                // And we calculate the final MIC value based on the 3 NTLMSSP packets and the exportedSessionKey
                Logger.DebugNested("Computing exportedSessionKey (Session Base Key)");
                byte[] ntlmV2Hash = NtlmHelper.ComputeNtlmV2Hash(username, password, domain);
                Logger.DebugNested($"NTLMv2 Hash: {BitConverter.ToString(ntlmV2Hash)}");
                
                byte[] ntProofStr = new byte[16]; // First 16 bytes of ntlmV2Response
                Array.Copy(ntlmV2Response, 0, ntProofStr, 0, 16);
                
                byte[] exportedSessionKey = NtlmHelper.ComputeSessionBaseKey(ntlmV2Hash, ntProofStr);
                Logger.DebugNested($"Exported Session Key: {BitConverter.ToString(exportedSessionKey)}");

                Logger.DebugNested("Calculating MIC (HMAC-MD5 over negotiate + challenge + authenticate)");
                byte[] ntlm_negotiate_data = auth.ToBytes();
                byte[] ntlm_challenge_data = serverChallenge;
                byte[] ntlm_authenticate_data = type3.ToBytes();

                Logger.DebugNested($"MIC input lengths - Negotiate: {ntlm_negotiate_data.Length}, Challenge: {ntlm_challenge_data.Length}, Authenticate: {ntlm_authenticate_data.Length}");

                byte[] micData = new byte[ntlm_negotiate_data.Length + ntlm_challenge_data.Length + ntlm_authenticate_data.Length];
                Array.Copy(ntlm_negotiate_data, 0, micData, 0, ntlm_negotiate_data.Length);
                Array.Copy(ntlm_challenge_data, 0, micData, ntlm_negotiate_data.Length, ntlm_challenge_data.Length);
                Array.Copy(ntlm_authenticate_data, 0, micData, ntlm_negotiate_data.Length + ntlm_challenge_data.Length, ntlm_authenticate_data.Length);

                byte[] newmic = NtlmHelper.HmacMd5(exportedSessionKey, micData);
                Logger.DebugNested($"Computed MIC is: {BitConverter.ToString(newmic)}");
                
                type3.MIC = newmic;
                Logger.DebugNested("MIC set in Type3 message");

                byte[] authenticateBytes = type3.ToBytes();
                Logger.DebugNested($"NTLMSSP_AUTHENTICATE total length: {authenticateBytes.Length} bytes");
                Logger.DebugNested($"NTLMSSP_AUTHENTICATE (first 32 bytes): {BitConverter.ToString(authenticateBytes.Take(Math.Min(32, authenticateBytes.Length)).ToArray())}");

                // Finally we send the ntlmssp_authenticate packet inside a TDS_SSPI packet
                await tdsClient.SendSSPIAsync(authenticateBytes);
                Logger.DebugNested("NTLMSSP_AUTHENTICATE sent via TDS_SSPI packet");

                // And we receive the final response from the MSSQL server
                tds = await tdsClient.ReceiveTdsAsync();
                Logger.DebugNested($"Received final TDS packet - Type: {tds.Type}, Length: {tds.Length}, Status: {tds.Status}");
                Logger.DebugNested($"TDS Data length: {tds.Data?.Length ?? 0} bytes");
                Logger.DebugNested($"TDS Data (first 50 bytes): {BitConverter.ToString(tds.Data.Take(Math.Min(50, tds.Data.Length)).ToArray())}");

                // At this point we have received an authentication response from the server whether it is
                // via a WindowsAuth or a local MSSQL auth so we just have to parse the response
                Logger.DebugNested("Parsing login response tokens");
                bool loginSuccess = tdsClient.ParseLoginResponse(tds.Data);

                if (loginSuccess)
                {
                    Logger.Success("Authentication successful!");
                    Logger.DebugNested("TDS_LOGINACK_TOKEN found in response - login accepted by server");
                }
                else
                {
                    Logger.Error("Authentication failed");
                    Logger.DebugNested("No TDS_LOGINACK_TOKEN found in response - login rejected by server");
                }

                return loginSuccess;
            }
            catch (Exception ex)
            {
                Logger.Error($"TDS Authentication error: {ex.Message}");
                Logger.DebugNested($"Exception type: {ex.GetType().Name}");
                Logger.DebugNested(ex.StackTrace);
                if (ex.InnerException != null)
                {
                    Logger.DebugNested($"Inner exception: {ex.InnerException.Message}");
                }
                return false;
            }
            finally
            {
                tdsClient?.Dispose();
                Logger.DebugNested("TdsClient disposed");
            }
        }
    }
}
