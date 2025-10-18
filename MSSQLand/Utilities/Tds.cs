using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MSSQLand.Utilities
{
    #region Constants and Enums

    /// <summary>
    /// TDS Packet Types
    /// </summary>
    public enum TdsPacketType : byte
    {
        SqlBatch = 0x01,
        PreTdsLogin = 0x02,
        Rpc = 0x03,
        TabularResult = 0x04,
        Attention = 0x06,
        BulkLoadData = 0x07,
        TransactionManager = 0x0E,
        Login7 = 0x10,
        SSPI = 0x11,
        PreLogin = 0x12
    }

    /// <summary>
    /// TDS Packet Status
    /// </summary>
    [Flags]
    public enum TdsPacketStatus : byte
    {
        Normal = 0x00,
        EndOfMessage = 0x01,
        ResetConnection = 0x08,
        ResetSkipTrans = 0x10
    }

    /// <summary>
    /// TDS Encryption Types
    /// </summary>
    public enum TdsEncryption : byte
    {
        Off = 0x00,
        On = 0x01,
        NotSupported = 0x02,
        Required = 0x03
    }

    /// <summary>
    /// TDS Token Types
    /// </summary>
    public enum TdsTokenType : byte
    {
        AltMetadata = 0x88,
        AltRow = 0xD3,
        ColMetadata = 0x81,
        ColInfo = 0xA5,
        Done = 0xFD,
        DoneProc = 0xFE,
        DoneInProc = 0xFF,
        EnvChange = 0xE3,
        Error = 0xAA,
        Info = 0xAB,
        LoginAck = 0xAD,
        NbcRow = 0xD2,
        Offset = 0x78,
        Order = 0xA9,
        ReturnStatus = 0x79,
        ReturnValue = 0xAC,
        Row = 0xD1,
        SSPI = 0xED,
        TabName = 0xA4
    }

    /// <summary>
    /// TDS Environment Change Types
    /// </summary>
    public enum TdsEnvChangeType : byte
    {
        Database = 0x01,
        Language = 0x02,
        Charset = 0x03,
        PacketSize = 0x04,
        Unicode = 0x05,
        UnicodeDS = 0x06,
        Collation = 0x07,
        TransStart = 0x08,
        TransCommit = 0x09,
        Rollback = 0x0A,
        DTC = 0x0B
    }

    #endregion

    #region TDS Structures

    /// <summary>
    /// TDS Packet Header
    /// </summary>
    public class TdsPacket
    {
        public TdsPacketType Type { get; set; }
        public TdsPacketStatus Status { get; set; }
        public ushort Length { get; set; }
        public ushort SPID { get; set; }
        public byte PacketID { get; set; }
        public byte Window { get; set; }
        public byte[] Data { get; set; }

        public const int HeaderSize = 8;

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write((byte)Type);
                writer.Write((byte)Status);
                
                // Convert ushort to big-endian manually (IPAddress.HostToNetworkOrder doesn't handle ushort properly)
                byte[] lengthBytes = BitConverter.GetBytes(Length);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(lengthBytes);
                writer.Write(lengthBytes);
                
                // Same for SPID
                byte[] spidBytes = BitConverter.GetBytes(SPID);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(spidBytes);
                writer.Write(spidBytes);
                
                writer.Write(PacketID);
                writer.Write(Window);
                if (Data != null)
                    writer.Write(Data);
                return ms.ToArray();
            }
        }

        public static TdsPacket FromBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                var packet = new TdsPacket
                {
                    Type = (TdsPacketType)reader.ReadByte(),
                    Status = (TdsPacketStatus)reader.ReadByte(),
                    Length = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16()),
                    SPID = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16()),
                    PacketID = reader.ReadByte(),
                    Window = reader.ReadByte()
                };

                int dataLength = packet.Length - HeaderSize;
                if (dataLength > 0)
                {
                    packet.Data = reader.ReadBytes(dataLength);
                }

                return packet;
            }
        }
    }

    /// <summary>
    /// PreLogin Packet Structure
    /// </summary>
    public class TdsPreLogin
    {
        public byte[] Version { get; set; } = new byte[] { 0x08, 0x00, 0x01, 0x55, 0x00, 0x00 };
        public TdsEncryption Encryption { get; set; } = TdsEncryption.Off;
        public string Instance { get; set; } = "MSSQLServer";
        public uint ThreadID { get; set; } = 0;

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // Calculate offsets
                int baseOffset = 21; // Fixed header size
                int versionOffset = baseOffset;
                int encryptionOffset = versionOffset + Version.Length;
                byte[] instanceBytes = Encoding.ASCII.GetBytes(Instance + "\0");
                int instanceOffset = encryptionOffset + 1;
                int threadIDOffset = instanceOffset + instanceBytes.Length;

                // Write token headers
                // Version
                writer.Write((byte)0x00);
                writer.Write((ushort)IPAddress.HostToNetworkOrder((short)versionOffset));
                writer.Write((ushort)IPAddress.HostToNetworkOrder((short)Version.Length));

                // Encryption
                writer.Write((byte)0x01);
                writer.Write((ushort)IPAddress.HostToNetworkOrder((short)encryptionOffset));
                writer.Write((ushort)IPAddress.HostToNetworkOrder((short)1));

                // Instance
                writer.Write((byte)0x02);
                writer.Write((ushort)IPAddress.HostToNetworkOrder((short)instanceOffset));
                writer.Write((ushort)IPAddress.HostToNetworkOrder((short)instanceBytes.Length));

                // ThreadID
                writer.Write((byte)0x03);
                writer.Write((ushort)IPAddress.HostToNetworkOrder((short)threadIDOffset));
                writer.Write((ushort)IPAddress.HostToNetworkOrder((short)4));

                // End marker
                writer.Write((byte)0xFF);

                // Write data
                writer.Write(Version);
                writer.Write((byte)Encryption);
                writer.Write(instanceBytes);
                writer.Write(ThreadID);

                return ms.ToArray();
            }
        }

        public static TdsPreLogin FromBytes(byte[] data)
        {
            var prelogin = new TdsPreLogin();
            int offset = 0;

            // Parse tokens
            while (offset + 5 <= data.Length) // Need at least 5 bytes (token + offset + length)
            {
                byte token = data[offset++];
                if (token == 0xFF) break;

                if (offset + 4 > data.Length) break; // Need 4 more bytes for offset and length

                ushort tokenOffset = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(data, offset));
                offset += 2;
                ushort tokenLength = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(data, offset));
                offset += 2;

                // Validate that the data exists at the specified offset
                if (tokenOffset + tokenLength > data.Length)
                    continue;

                switch (token)
                {
                    case 0x00: // Version
                        if (tokenLength > 0 && tokenOffset + tokenLength <= data.Length)
                        {
                            prelogin.Version = new byte[tokenLength];
                            Array.Copy(data, tokenOffset, prelogin.Version, 0, tokenLength);
                        }
                        break;
                    case 0x01: // Encryption
                        if (tokenOffset < data.Length)
                        {
                            prelogin.Encryption = (TdsEncryption)data[tokenOffset];
                        }
                        break;
                    case 0x02: // Instance
                        if (tokenLength > 1 && tokenOffset + tokenLength <= data.Length)
                        {
                            prelogin.Instance = Encoding.ASCII.GetString(data, tokenOffset, tokenLength - 1);
                        }
                        break;
                    case 0x03: // ThreadID
                        if (tokenLength >= 4 && tokenOffset + 4 <= data.Length)
                        {
                            prelogin.ThreadID = BitConverter.ToUInt32(data, tokenOffset);
                        }
                        break;
                }
            }

            return prelogin;
        }
    }

    /// <summary>
    /// Login7 Packet Structure (matches impacket's TDS_LOGIN)
    /// </summary>
    public class TdsLogin7
    {
        public uint Length { get; set; }
        public uint TDSVersion { get; set; } = 0x71000001; // Big-endian 0x71 in impacket: '>L=0x71'
        public uint PacketSize { get; set; } = 32764; // Impacket default
        public uint ClientProgVer { get; set; } = 0x07000000; // Big-endian 7 in impacket: '>L=7'
        public uint ClientPID { get; set; }
        public uint ConnectionID { get; set; }
        public byte OptionFlags1 { get; set; } = 0xE0; // Impacket default
        public byte OptionFlags2 { get; set; } = 0x03; // TDS_INIT_LANG_FATAL | TDS_ODBC_ON
        public byte TypeFlags { get; set; }
        public byte OptionFlags3 { get; set; }
        public int ClientTimeZone { get; set; }
        public uint ClientLCID { get; set; }

        public string HostName { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";
        public string AppName { get; set; } = "";
        public string ServerName { get; set; } = "";
        public string CltIntName { get; set; } = "";
        public string Language { get; set; } = "";
        public string Database { get; set; } = "";
        public byte[] ClientID { get; set; } = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
        public byte[] SSPI { get; set; } = new byte[0];
        public string AtchDBFile { get; set; } = "";

        private byte[] EncryptPassword(string password)
        {
            byte[] passwordBytes = Encoding.Unicode.GetBytes(password);
            byte[] encrypted = new byte[passwordBytes.Length];
            for (int i = 0; i < passwordBytes.Length; i++)
            {
                encrypted[i] = (byte)(((passwordBytes[i] & 0x0F) << 4) | ((passwordBytes[i] & 0xF0) >> 4) ^ 0xA5);
            }
            return encrypted;
        }

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // Prepare data - encode as UTF-16LE (matching impacket's .encode('utf-16le'))
                byte[] hostNameBytes = Encoding.Unicode.GetBytes(HostName);
                byte[] userNameBytes = Encoding.Unicode.GetBytes(UserName);
                byte[] passwordBytes = string.IsNullOrEmpty(Password) ? new byte[0] : EncryptPassword(Password);
                byte[] appNameBytes = Encoding.Unicode.GetBytes(AppName);
                byte[] serverNameBytes = Encoding.Unicode.GetBytes(ServerName);
                byte[] cltIntNameBytes = Encoding.Unicode.GetBytes(CltIntName);
                byte[] languageBytes = Encoding.Unicode.GetBytes(Language);
                byte[] databaseBytes = Encoding.Unicode.GetBytes(Database);
                byte[] atchDBFileBytes = Encoding.Unicode.GetBytes(AtchDBFile);

                // Calculate offsets (matching impacket's getData logic)
                // index = 36+50 = 86 in impacket
                int index = 86;
                int hostNameOffset = index;
                
                index += hostNameBytes.Length;
                
                int userNameOffset = string.IsNullOrEmpty(UserName) ? 0 : index;
                index += userNameBytes.Length;
                
                int passwordOffset = string.IsNullOrEmpty(Password) ? 0 : index;
                index += passwordBytes.Length;
                
                int appNameOffset = index;
                int serverNameOffset = appNameOffset + appNameBytes.Length;
                int cltIntNameOffset = serverNameOffset + serverNameBytes.Length;
                int languageOffset = cltIntNameOffset + cltIntNameBytes.Length;
                int databaseOffset = languageOffset + languageBytes.Length;
                int sspiOffset = databaseOffset + databaseBytes.Length;
                int atchDBFileOffset = sspiOffset + SSPI.Length;

                // Write fixed header (matches impacket's structure)
                writer.Write((uint)0); // Length placeholder
                
                // TDSVersion: impacket uses '>L=0x71' (big-endian), but then converts it
                // The actual value should be 0x71000001 in little-endian
                writer.Write(TDSVersion);
                
                writer.Write(PacketSize);
                writer.Write(ClientProgVer);
                writer.Write(ClientPID);
                writer.Write(ConnectionID);
                writer.Write(OptionFlags1);
                writer.Write(OptionFlags2);
                writer.Write(TypeFlags);
                writer.Write(OptionFlags3);
                writer.Write(ClientTimeZone);
                writer.Write(ClientLCID);

                // Write offsets and lengths (characters, not bytes - divide by 2)
                writer.Write((ushort)hostNameOffset);
                writer.Write((ushort)(hostNameBytes.Length / 2));
                writer.Write((ushort)userNameOffset);
                writer.Write((ushort)(userNameBytes.Length / 2));
                writer.Write((ushort)passwordOffset);
                writer.Write((ushort)(passwordBytes.Length / 2));
                writer.Write((ushort)appNameOffset);
                writer.Write((ushort)(appNameBytes.Length / 2));
                writer.Write((ushort)serverNameOffset);
                writer.Write((ushort)(serverNameBytes.Length / 2));
                writer.Write((ushort)0); // Unused offset
                writer.Write((ushort)0); // Unused length
                writer.Write((ushort)cltIntNameOffset);
                writer.Write((ushort)(cltIntNameBytes.Length / 2));
                writer.Write((ushort)languageOffset);
                writer.Write((ushort)(languageBytes.Length / 2));
                writer.Write((ushort)databaseOffset);
                writer.Write((ushort)(databaseBytes.Length / 2));
                writer.Write(ClientID);
                writer.Write((ushort)sspiOffset);
                writer.Write((ushort)SSPI.Length);
                writer.Write((ushort)atchDBFileOffset);
                writer.Write((ushort)(atchDBFileBytes.Length / 2));

                // Write variable data
                writer.Write(hostNameBytes);
                writer.Write(userNameBytes);
                writer.Write(passwordBytes);
                writer.Write(appNameBytes);
                writer.Write(serverNameBytes);
                writer.Write(cltIntNameBytes);
                writer.Write(languageBytes);
                writer.Write(databaseBytes);
                writer.Write(SSPI);
                writer.Write(atchDBFileBytes);

                // Update length
                byte[] result = ms.ToArray();
                BitConverter.GetBytes((uint)result.Length).CopyTo(result, 0);

                return result;
            }
        }
    }

    #endregion

    #region TDS Client

    /// <summary>
    /// Simple TDS Client for MSSQL authentication (mimics impacket's tds.py)
    /// </summary>
    public class TdsClient : IDisposable
    {
        private TcpClient tcpClient;
        private NetworkStream networkStream;
        private int packetSize;
        private TlsContext tlsContext;
        private bool useTls = false;
        private TdsEncryption encryptionMode;

        public int PacketSize
        {
            get => packetSize;
            set => packetSize = value;
        }

        public TdsClient(string server, int port, int initialPacketSize = 4096)
        {
            packetSize = initialPacketSize;
            tcpClient = new TcpClient(server, port);
            networkStream = tcpClient.GetStream();
        }

        /// <summary>
        /// Send PreLogin packet
        /// </summary>
        public async Task<TdsPreLogin> PreLoginAsync()
        {
            var preLogin = new TdsPreLogin
            {
                ThreadID = (uint)new Random().Next(0, 65535)
            };

            await SendTdsAsync(TdsPacketType.PreLogin, preLogin.ToBytes());
            var response = await ReceiveTdsAsync();
            
            var preLoginResponse = TdsPreLogin.FromBytes(response.Data);
            encryptionMode = preLoginResponse.Encryption;
            
            return preLoginResponse;
        }

        /// <summary>
        /// Perform TLS handshake using MemoryBIO approach (mimics impacket's set_tls_context)
        /// This matches the Python implementation:
        /// - Creates in_bio and out_bio (MemoryBIO objects)
        /// - Wraps them with SSLContext
        /// - Performs handshake with SSLWantReadError handling
        /// - Sends/receives TLS data wrapped in TDS packets
        /// </summary>
        public async Task PerformTlsHandshakeAsync()
        {
            Logger.DebugNested("Creating TLS context with MemoryBIO...");
            
            // Create TLS context (mimics Python's ssl.MemoryBIO() and wrap_bio())
            tlsContext = new TlsContext();
            
            // Start the authentication process once
            Logger.DebugNested("Starting TLS authentication task...");
            tlsContext.StartHandshake();
            
            // Perform handshake loop (mimics impacket's while True: try: tls.do_handshake())
            bool handshakeComplete = false;
            int step = 0;
            
            while (!handshakeComplete)
            {
                step++;
                Logger.DebugNested($"TLS handshake step {step}...");
                
                try
                {
                    // This mimics: tls.do_handshake()
                    // On SSLWantReadError, we need to send data and receive response
                    var outgoingData = await tlsContext.PerformHandshakeStepAsync();
                    
                    if (outgoingData != null && outgoingData.Length > 0)
                    {
                        // We have data to send (mimics: data = out_bio.read(4096))
                        Logger.DebugNested($"Sending {outgoingData.Length} bytes of TLS handshake data in TDS_PRE_LOGIN packet");
                        
                        // Send it wrapped in TDS_PRE_LOGIN (mimics: self.sendTDS(TDS_PRE_LOGIN, data, 0))
                        await SendTdsRawAsync(TdsPacketType.PreLogin, outgoingData);
                        
                        // Receive server response (mimics: tds_packet = self.recvTDS(4096))
                        Logger.DebugNested("Receiving TLS handshake response...");
                        var tdsResponse = await ReceiveTdsRawAsync(4096);
                        var tlsResponseData = tdsResponse.Data;
                        
                        Logger.DebugNested($"Received {tlsResponseData.Length} bytes of TLS data");
                        
                        // Feed it to TLS context (mimics: in_bio.write(tls_data))
                        tlsContext.FeedIncomingData(tlsResponseData);
                    }
                    
                    // Check if handshake is complete
                    if (tlsContext.IsHandshakeComplete)
                    {
                        Logger.SuccessNested("TLS handshake completed successfully");
                        Logger.DebugNested($"SSL Protocol: {tlsContext.SslProtocol}");
                        handshakeComplete = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.DebugNested($"TLS handshake exception: {ex.Message}");
                    
                    // Check if we have outgoing data despite the exception
                    var outgoingData = tlsContext.GetOutgoingData();
                    if (outgoingData.Length > 0)
                    {
                        Logger.DebugNested($"Sending {outgoingData.Length} bytes after exception");
                        await SendTdsRawAsync(TdsPacketType.PreLogin, outgoingData);
                        
                        var tdsResponse = await ReceiveTdsRawAsync(4096);
                        tlsContext.FeedIncomingData(tdsResponse.Data);
                    }
                    else
                    {
                        throw;
                    }
                }
                
                // Safety: prevent infinite loop
                if (step > 10)
                {
                    throw new InvalidOperationException("TLS handshake did not complete after 10 steps");
                }
            }

            // Enable TLS mode
            useTls = true;
            
            // Adjust packet size for TLS (as per impacket: self.packetSize = 16 * 1024 - 1)
            packetSize = 16 * 1024 - 1;
            Logger.DebugNested($"Packet size adjusted to: {packetSize}");

            Logger.DebugNested("TLS context established");
        }

        /// <summary>
        /// Generate Channel Binding Token from tls-unique (mimics impacket's generate_cbt_from_tls_unique)
        /// </summary>
        public byte[] GenerateChannelBindingToken()
        {
            if (tlsContext == null || tlsContext.TlsUniqueChannelBinding == null || tlsContext.TlsUniqueChannelBinding.Length == 0)
            {
                // Return empty CBT if no TLS or tls-unique not available
                Logger.DebugNested("No tls-unique available, returning empty CBT");
                return new byte[0];
            }

            // Build channel binding structure as per MS-NLMP
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // Initiator address (8 bytes of 0)
                writer.Write(new byte[8]);
                
                // Acceptor address (8 bytes of 0)
                writer.Write(new byte[8]);
                
                // Application data
                byte[] appDataPrefix = Encoding.ASCII.GetBytes("tls-unique:");
                byte[] appDataRaw = new byte[appDataPrefix.Length + tlsContext.TlsUniqueChannelBinding.Length];
                Array.Copy(appDataPrefix, 0, appDataRaw, 0, appDataPrefix.Length);
                Array.Copy(tlsContext.TlsUniqueChannelBinding, 0, appDataRaw, appDataPrefix.Length, tlsContext.TlsUniqueChannelBinding.Length);
                
                // Length of application data (little-endian)
                writer.Write((uint)appDataRaw.Length);
                writer.Write(appDataRaw);
                
                // Compute MD5 hash of the channel binding structure
                byte[] channelBindingStruct = ms.ToArray();
                using (var md5 = MD5.Create())
                {
                    return md5.ComputeHash(channelBindingStruct);
                }
            }
        }

        /// <summary>
        /// Send Login7 packet
        /// </summary>
        public async Task SendLogin7Async(TdsLogin7 login)
        {
            await SendTdsAsync(TdsPacketType.Login7, login.ToBytes());
        }

        /// <summary>
        /// Disable TLS after Login7 (matching impacket: if resp['Encryption'] == TDS_ENCRYPT_OFF: self.tlsSocket = None)
        /// According to the specs, if encryption is not required, we must encrypt just the first Login packet
        /// </summary>
        public void DisableTlsAfterLogin()
        {
            if (tlsContext != null)
            {
                Logger.DebugNested("Disabling TLS (matching impacket: self.tlsSocket = None)");
                useTls = false;
                // Note: We keep tlsContext alive but stop using it
            }
        }

        /// <summary>
        /// Send SSPI packet
        /// </summary>
        public async Task SendSSPIAsync(byte[] sspiData)
        {
            await SendTdsAsync(TdsPacketType.SSPI, sspiData);
        }

        /// <summary>
        /// Send TDS packet - wrapper that handles TLS wrapping (mimics impacket's sendTDS + socketSendall)
        /// </summary>
        private async Task SendTdsAsync(TdsPacketType packetType, byte[] data, byte packetId = 1)
        {
            int offset = 0;
            int remaining = data.Length;
            int maxDataSize = packetSize - TdsPacket.HeaderSize;

            while (remaining > 0)
            {
                int chunkSize = Math.Min(remaining, maxDataSize);
                bool isLast = (remaining == chunkSize);

                var packet = new TdsPacket
                {
                    Type = packetType,
                    Status = isLast ? TdsPacketStatus.EndOfMessage : TdsPacketStatus.Normal,
                    Length = (ushort)(TdsPacket.HeaderSize + chunkSize),
                    SPID = 0,
                    PacketID = packetId,
                    Window = 0,
                    Data = new byte[chunkSize]
                };

                Array.Copy(data, offset, packet.Data, 0, chunkSize);

                byte[] packetBytes = packet.ToBytes();
                
                // Send via appropriate channel (TLS or plain)
                await SocketSendAllAsync(packetBytes);

                offset += chunkSize;
                remaining -= chunkSize;
                packetId++;
            }
        }

        /// <summary>
        /// Send raw TDS packet (used during TLS handshake)
        /// </summary>
        private async Task SendTdsRawAsync(TdsPacketType packetType, byte[] data, byte packetId = 0)
        {
            var packet = new TdsPacket
            {
                Type = packetType,
                Status = TdsPacketStatus.EndOfMessage,
                Length = (ushort)(TdsPacket.HeaderSize + data.Length),
                SPID = 0,
                PacketID = packetId,
                Window = 0,
                Data = data
            };

            byte[] packetBytes = packet.ToBytes();
            await networkStream.WriteAsync(packetBytes, 0, packetBytes.Length);
            await networkStream.FlushAsync();
        }

        /// <summary>
        /// Socket send wrapper that handles TLS encryption (mimics impacket's socketSendall)
        /// </summary>
        private async Task SocketSendAllAsync(byte[] data)
        {
            if (!useTls || tlsContext == null)
            {
                // Plain send (mimics: return self.socket.sendall(data))
                await networkStream.WriteAsync(data, 0, data.Length);
                await networkStream.FlushAsync();
            }
            else
            {
                // TLS send (mimics: tls_send)
                // self.tlsSocket.write(data)
                await tlsContext.WriteAsync(data);
                
                // encrypted = self.out_bio.read(4096)
                var encrypted = tlsContext.GetOutgoingData();
                
                if (encrypted.Length > 0)
                {
                    // self.socket.sendall(encrypted)
                    await networkStream.WriteAsync(encrypted, 0, encrypted.Length);
                    await networkStream.FlushAsync();
                }
            }
        }

        /// <summary>
        /// Receive TDS packet (mimics impacket's recvTDS)
        /// </summary>
        public async Task<TdsPacket> ReceiveTdsAsync()
        {
            var allData = new List<byte>();
            TdsPacket firstPacket = null;

            while (true)
            {
                // Read header
                byte[] header = new byte[TdsPacket.HeaderSize];
                await SocketRecvAsync(header, 0, TdsPacket.HeaderSize);

                var packet = TdsPacket.FromBytes(header);
                
                if (firstPacket == null)
                    firstPacket = packet;

                // Read data
                int dataLength = packet.Length - TdsPacket.HeaderSize;
                if (dataLength > 0)
                {
                    byte[] data = new byte[dataLength];
                    await SocketRecvAsync(data, 0, dataLength);
                    allData.AddRange(data);
                }

                if ((packet.Status & TdsPacketStatus.EndOfMessage) != 0)
                    break;
            }

            return new TdsPacket
            {
                Type = firstPacket.Type,
                Status = TdsPacketStatus.EndOfMessage,
                Length = (ushort)(TdsPacket.HeaderSize + allData.Count),
                SPID = firstPacket.SPID,
                PacketID = firstPacket.PacketID,
                Window = firstPacket.Window,
                Data = allData.ToArray()
            };
        }

        /// <summary>
        /// Receive raw TDS packet (used during TLS handshake)
        /// </summary>
        private async Task<TdsPacket> ReceiveTdsRawAsync(int maxSize)
        {
            byte[] header = new byte[TdsPacket.HeaderSize];
            int bytesRead = 0;
            
            while (bytesRead < TdsPacket.HeaderSize)
            {
                int read = await networkStream.ReadAsync(header, bytesRead, TdsPacket.HeaderSize - bytesRead);
                if (read == 0) throw new IOException("Connection closed");
                bytesRead += read;
            }

            var packet = TdsPacket.FromBytes(header);
            int dataLength = packet.Length - TdsPacket.HeaderSize;
            
            if (dataLength > 0)
            {
                byte[] data = new byte[dataLength];
                bytesRead = 0;
                
                while (bytesRead < dataLength)
                {
                    int read = await networkStream.ReadAsync(data, bytesRead, dataLength - bytesRead);
                    if (read == 0) throw new IOException("Connection closed");
                    bytesRead += read;
                }
                
                packet.Data = data;
            }

            return packet;
        }

        /// <summary>
        /// Socket receive wrapper that handles TLS decryption (mimics impacket's socketRecv)
        /// </summary>
        private async Task SocketRecvAsync(byte[] buffer, int offset, int count)
        {
            if (!useTls || tlsContext == null)
            {
                // Plain receive (mimics: return self.socket.recv(bufsize))
                int totalRead = 0;
                while (totalRead < count)
                {
                    int read = await networkStream.ReadAsync(buffer, offset + totalRead, count - totalRead);
                    if (read == 0) throw new IOException("Connection closed");
                    totalRead += read;
                }
            }
            else
            {
                // TLS receive (mimics: tls_recv)
                int totalRead = 0;
                
                while (totalRead < count)
                {
                    // Try to read decrypted data from TLS context
                    var decrypted = await tlsContext.ReadAsync(count - totalRead);
                    
                    if (decrypted.Length > 0)
                    {
                        Array.Copy(decrypted, 0, buffer, offset + totalRead, decrypted.Length);
                        totalRead += decrypted.Length;
                    }
                    else
                    {
                        // Need more encrypted data from network
                        // encrypted = self.socket.recv(bufsize)
                        byte[] encrypted = new byte[4096];
                        int encRead = await networkStream.ReadAsync(encrypted, 0, encrypted.Length);
                        
                        if (encRead == 0)
                            throw new IOException("Connection closed");
                        
                        // self.in_bio.write(encrypted)
                        byte[] actualEncrypted = new byte[encRead];
                        Array.Copy(encrypted, actualEncrypted, encRead);
                        tlsContext.FeedIncomingData(actualEncrypted);
                    }
                }
            }
        }

        /// <summary>
        /// Parse token stream for LoginAck (mimics impacket's parseReply)
        /// </summary>
        public bool ParseLoginResponse(byte[] data)
        {
            int offset = 0;
            bool loginSuccess = false;

            while (offset < data.Length)
            {
                if (offset >= data.Length) break;

                byte tokenType = data[offset++];
                try
                {
                    switch ((TdsTokenType)tokenType)
                    {
                        case TdsTokenType.LoginAck:
                            loginSuccess = true;
                            if (offset + 2 <= data.Length)
                            {
                                ushort length = BitConverter.ToUInt16(data, offset);
                                offset += 2 + length;
                            }
                            else
                            {
                                offset = data.Length;
                            }
                            break;

                        case TdsTokenType.Error:
                        case TdsTokenType.Info:
                            if (offset + 2 <= data.Length)
                            {
                                ushort msgLength = BitConverter.ToUInt16(data, offset);
                                offset += 2;
                                if (offset + msgLength <= data.Length)
                                {
                                    offset += msgLength;
                                }
                                else
                                {
                                    offset = data.Length;
                                }
                            }
                            else
                            {
                                offset = data.Length;
                            }
                            break;

                        case TdsTokenType.EnvChange:
                            if (offset + 2 <= data.Length)
                            {
                                ushort envLength = BitConverter.ToUInt16(data, offset);
                                offset += 2;
                                
                                int envStartOffset = offset;
                                
                                if (offset < data.Length)
                                {
                                    byte envType = data[offset++];
                                    
                                    // Parse packet size change (as per impacket)
                                    if (envType == (byte)TdsEnvChangeType.PacketSize && offset + 1 <= data.Length)
                                    {
                                        byte newValueLen = data[offset++];
                                        if (offset + newValueLen * 2 <= data.Length)
                                        {
                                            string newValue = Encoding.Unicode.GetString(data, offset, newValueLen * 2);
                                            if (int.TryParse(newValue, out int newPacketSize))
                                            {
                                                packetSize = newPacketSize;
                                            }
                                        }
                                    }
                                }
                                
                                // Move to end of EnvChange token
                                offset = envStartOffset + envLength;
                                if (offset > data.Length)
                                    offset = data.Length;
                            }
                            else
                            {
                                offset = data.Length;
                            }
                            break;

                        case TdsTokenType.Done:
                        case TdsTokenType.DoneProc:
                        case TdsTokenType.DoneInProc:
                            if (offset + 8 <= data.Length)
                            {
                                offset += 8; // Status(2) + CurCmd(2) + DoneRowCount(4)
                            }
                            else
                            {
                                offset = data.Length;
                            }
                            break;

                        case TdsTokenType.SSPI:
                            if (offset + 2 <= data.Length)
                            {
                                ushort sspiLength = BitConverter.ToUInt16(data, offset);
                                offset += 2;
                                if (offset + sspiLength <= data.Length)
                                {
                                    offset += sspiLength;
                                }
                                else
                                {
                                    offset = data.Length;
                                }
                            }
                            else
                            {
                                offset = data.Length;
                            }
                            break;

                        default:
                            // Unknown token - try to skip entire remaining buffer
                            offset = data.Length;
                            break;
                    }
                }
                catch
                {
                    // If any error occurs during parsing, stop
                    break;
                }
            }

            return loginSuccess;
        }

        /// <summary>
        /// Extract SSPI data from response (mimics impacket's challenge extraction)
        /// </summary>
        public byte[] ExtractSspiData(byte[] data)
        {
            // In impacket: serverChallenge = tds['Data'][3:]
            // This skips the first 3 bytes (token type + length)
            
            int offset = 0;
            
            while (offset < data.Length)
            {
                if (offset >= data.Length)
                    break;
                    
                byte tokenType = data[offset++];

                if ((TdsTokenType)tokenType == TdsTokenType.SSPI)
                {
                    if (offset + 2 <= data.Length)
                    {
                        ushort length = BitConverter.ToUInt16(data, offset);
                        offset += 2;
                        
                        if (offset + length <= data.Length)
                        {
                            byte[] sspiData = new byte[length];
                            Array.Copy(data, offset, sspiData, 0, length);
                            return sspiData;
                        }
                    }
                    // If we found SSPI token but couldn't read it, return null
                    return null;
                }
                else if ((TdsTokenType)tokenType == TdsTokenType.Error || 
                         (TdsTokenType)tokenType == TdsTokenType.Info)
                {
                    // Skip Error/Info tokens to look for SSPI
                    if (offset + 2 <= data.Length)
                    {
                        ushort length = BitConverter.ToUInt16(data, offset);
                        offset += 2 + length;
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    // Unknown token, stop searching
                    break;
                }
            }

            // If no SSPI token found, return the entire buffer as fallback
            // (mimics impacket's serverChallenge = tds['Data'][3:])
            if (data.Length > 3)
            {
                byte[] fallback = new byte[data.Length - 3];
                Array.Copy(data, 3, fallback, 0, fallback.Length);
                return fallback;
            }
            
            return data;
        }

        public void Dispose()
        {
            tlsContext?.Dispose();
            networkStream?.Dispose();
            tcpClient?.Dispose();
        }
    }

    #endregion
}

// Extension method for task cancellation
internal static class TaskExtensions
{
    public static async Task<T> WithCancellation<T>(this Task<T> task, System.Threading.CancellationToken cancellationToken)
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        using (cancellationToken.Register(s => ((System.Threading.Tasks.TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
        {
            if (task != await Task.WhenAny(task, tcs.Task))
                throw new System.OperationCanceledException(cancellationToken);
        }
        return await task;
    }
    
    public static async Task WithCancellation(this Task task, System.Threading.CancellationToken cancellationToken)
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        using (cancellationToken.Register(s => ((System.Threading.Tasks.TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
        {
            if (task != await Task.WhenAny(task, tcs.Task))
                throw new System.OperationCanceledException(cancellationToken);
        }
        await task;
    }
}
