using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MSSQLand.Utilities
{
    #region NTLM Constants

    /// <summary>
    /// NTLM Negotiate Flags
    /// </summary>
    [Flags]
    public enum NtlmFlags : uint
    {
        NegotiateUnicode = 0x00000001,
        NegotiateOEM = 0x00000002,
        RequestTarget = 0x00000004,
        NegotiateSign = 0x00000010,
        NegotiateSeal = 0x00000020,
        NegotiateDatagram = 0x00000040,
        NegotiateLMKey = 0x00000080,
        NegotiateNTLM = 0x00000200,
        NegotiateAnonymous = 0x00000800,
        NegotiateOEMDomainSupplied = 0x00001000,
        NegotiateOEMWorkstationSupplied = 0x00002000,
        NegotiateAlwaysSign = 0x00008000,
        TargetTypeDomain = 0x00010000,
        TargetTypeServer = 0x00020000,
        NegotiateExtendedSessionSecurity = 0x00080000,
        NegotiateIdentify = 0x00100000,
        RequestNonNTSessionKey = 0x00400000,
        NegotiateTargetInfo = 0x00800000,
        NegotiateVersion = 0x02000000,
        Negotiate128 = 0x20000000,
        NegotiateKeyExch = 0x40000000,
        Negotiate56 = 0x80000000
    }

    /// <summary>
    /// NTLM Message Types
    /// </summary>
    public enum NtlmMessageType : uint
    {
        Negotiate = 0x00000001,
        Challenge = 0x00000002,
        Authenticate = 0x00000003
    }

    /// <summary>
    /// AV_PAIR Types for Target Info
    /// </summary>
    public enum AvPairType : ushort
    {
        EOL = 0x0000,
        NbComputerName = 0x0001,
        NbDomainName = 0x0002,
        DnsComputerName = 0x0003,
        DnsDomainName = 0x0004,
        DnsTreeName = 0x0005,
        Flags = 0x0006,
        Timestamp = 0x0007,
        SingleHost = 0x0008,
        TargetName = 0x0009,
        ChannelBindings = 0x000A
    }

    #endregion

    #region NTLM Structures

    /// <summary>
    /// NTLM Version structure
    /// </summary>
    public class NtlmVersion
    {
        public byte ProductMajorVersion { get; set; } = 10;
        public byte ProductMinorVersion { get; set; } = 0;
        public ushort ProductBuild { get; set; } = 20348;
        public byte[] Reserved { get; set; } = new byte[3];
        public byte NTLMRevisionCurrent { get; set; } = 0x0F;

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(ProductMajorVersion);
                writer.Write(ProductMinorVersion);
                writer.Write(ProductBuild);
                writer.Write(Reserved);
                writer.Write(NTLMRevisionCurrent);
                return ms.ToArray();
            }
        }

        public static NtlmVersion FromBytes(byte[] data)
        {
            if (data.Length < 8) return null;

            return new NtlmVersion
            {
                ProductMajorVersion = data[0],
                ProductMinorVersion = data[1],
                ProductBuild = BitConverter.ToUInt16(data, 2),
                Reserved = new byte[] { data[4], data[5], data[6] },
                NTLMRevisionCurrent = data[7]
            };
        }
    }

    /// <summary>
    /// NTLM AV_PAIRS structure
    /// </summary>
    public class NtlmAvPairs
    {
        private readonly System.Collections.Generic.Dictionary<AvPairType, byte[]> pairs =
            new System.Collections.Generic.Dictionary<AvPairType, byte[]>();

        public void Add(AvPairType type, byte[] value)
        {
            pairs[type] = value;
        }

        public byte[] Get(AvPairType type)
        {
            return pairs.ContainsKey(type) ? pairs[type] : null;
        }

        public bool Contains(AvPairType type)
        {
            return pairs.ContainsKey(type);
        }

        public void Remove(AvPairType type)
        {
            pairs.Remove(type);
        }

        public byte[] ToBytes()
        {
            // Remove EOL if present
            pairs.Remove(AvPairType.EOL);

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                foreach (var pair in pairs)
                {
                    writer.Write((ushort)pair.Key);
                    writer.Write((ushort)pair.Value.Length);
                    writer.Write(pair.Value);
                }

                // End with EOL
                writer.Write((ushort)AvPairType.EOL);
                writer.Write((ushort)0);

                return ms.ToArray();
            }
        }

        public static NtlmAvPairs FromBytes(byte[] data)
        {
            var avPairs = new NtlmAvPairs();
            int offset = 0;

            while (offset + 4 <= data.Length)
            {
                AvPairType type = (AvPairType)BitConverter.ToUInt16(data, offset);
                offset += 2;
                ushort length = BitConverter.ToUInt16(data, offset);
                offset += 2;

                if (type == AvPairType.EOL)
                    break;

                if (offset + length <= data.Length)
                {
                    byte[] value = new byte[length];
                    Array.Copy(data, offset, value, 0, length);
                    avPairs.Add(type, value);
                    offset += length;
                }
                else
                {
                    break;
                }
            }

            return avPairs;
        }
    }

    #endregion

    #region NTLM Messages

    /// <summary>
    /// NTLM Negotiate Message (Type 1)
    /// </summary>
    public class NtlmNegotiateMessage
    {
        public string Signature { get; set; } = "NTLMSSP\0";
        public NtlmMessageType MessageType { get; set; } = NtlmMessageType.Negotiate;
        public NtlmFlags Flags { get; set; }
        public string DomainName { get; set; } = "";
        public string WorkstationName { get; set; } = "";
        public NtlmVersion Version { get; set; }

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // Signature
                writer.Write(Encoding.ASCII.GetBytes(Signature));

                // Message Type
                writer.Write((uint)MessageType);

                // Flags
                writer.Write((uint)Flags);

                // Domain fields
                byte[] domainBytes = Encoding.ASCII.GetBytes(DomainName.ToUpper());
                writer.Write((ushort)domainBytes.Length);
                writer.Write((ushort)domainBytes.Length);

                // Workstation fields
                byte[] workstationBytes = Encoding.ASCII.GetBytes(WorkstationName.ToUpper());
                writer.Write((ushort)workstationBytes.Length);
                writer.Write((ushort)workstationBytes.Length);

                // Calculate offsets
                int baseOffset = 32; // Fixed header size without version
                if (Version != null)
                {
                    baseOffset = 40; // With version
                }

                int domainOffset = baseOffset;
                int workstationOffset = domainOffset + domainBytes.Length;

                // Write offsets
                writer.Write((uint)domainOffset);
                writer.Write((uint)workstationOffset);

                // Version (if present)
                if (Version != null)
                {
                    writer.Write(Version.ToBytes());
                }

                // Payload
                writer.Write(domainBytes);
                writer.Write(workstationBytes);

                return ms.ToArray();
            }
        }
    }

    /// <summary>
    /// NTLM Challenge Message (Type 2)
    /// </summary>
    public class NtlmChallengeMessage
    {
        public string Signature { get; set; }
        public NtlmMessageType MessageType { get; set; }
        public string TargetName { get; set; }
        public NtlmFlags Flags { get; set; }
        public byte[] ServerChallenge { get; set; }
        public byte[] Reserved { get; set; }
        public NtlmAvPairs TargetInfo { get; set; }
        public NtlmVersion Version { get; set; }

        public static NtlmChallengeMessage FromBytes(byte[] data)
        {
            if (data.Length < 48)
                throw new ArgumentException("Challenge message too short");

            var message = new NtlmChallengeMessage
            {
                Signature = Encoding.ASCII.GetString(data, 0, 8),
                MessageType = (NtlmMessageType)BitConverter.ToUInt32(data, 8)
            };

            // Target Name
            ushort targetNameLen = BitConverter.ToUInt16(data, 12);
            uint targetNameOffset = BitConverter.ToUInt32(data, 16);
            if (targetNameOffset > 0 && targetNameOffset + targetNameLen <= data.Length)
            {
                message.TargetName = Encoding.Unicode.GetString(data, (int)targetNameOffset, targetNameLen);
            }

            // Flags
            message.Flags = (NtlmFlags)BitConverter.ToUInt32(data, 20);

            // Server Challenge
            message.ServerChallenge = new byte[8];
            Array.Copy(data, 24, message.ServerChallenge, 0, 8);

            // Reserved
            message.Reserved = new byte[8];
            Array.Copy(data, 32, message.Reserved, 0, 8);

            // Target Info
            ushort targetInfoLen = BitConverter.ToUInt16(data, 40);
            uint targetInfoOffset = BitConverter.ToUInt32(data, 44);
            if (targetInfoOffset > 0 && targetInfoOffset + targetInfoLen <= data.Length)
            {
                byte[] targetInfoBytes = new byte[targetInfoLen];
                Array.Copy(data, (int)targetInfoOffset, targetInfoBytes, 0, targetInfoLen);
                message.TargetInfo = NtlmAvPairs.FromBytes(targetInfoBytes);
            }

            // Version (if present)
            if ((message.Flags & NtlmFlags.NegotiateVersion) != 0 && data.Length >= 56)
            {
                byte[] versionBytes = new byte[8];
                Array.Copy(data, 48, versionBytes, 0, 8);
                message.Version = NtlmVersion.FromBytes(versionBytes);
            }

            return message;
        }
    }

    /// <summary>
    /// NTLM Authenticate Message (Type 3)
    /// </summary>
    public class NtlmAuthenticateMessage
    {
        public string Signature { get; set; } = "NTLMSSP\0";
        public NtlmMessageType MessageType { get; set; } = NtlmMessageType.Authenticate;
        public byte[] LmChallengeResponse { get; set; }
        public byte[] NtChallengeResponse { get; set; }
        public string DomainName { get; set; }
        public string UserName { get; set; }
        public string WorkstationName { get; set; }
        public byte[] EncryptedRandomSessionKey { get; set; }
        public NtlmFlags Flags { get; set; }
        public NtlmVersion Version { get; set; }
        public byte[] MIC { get; set; }

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // Signature
                writer.Write(Encoding.ASCII.GetBytes(Signature));

                // Message Type
                writer.Write((uint)MessageType);

                // Encode strings
                byte[] domainBytes = Encoding.Unicode.GetBytes(DomainName ?? "");
                byte[] userBytes = Encoding.Unicode.GetBytes(UserName ?? "");
                byte[] workstationBytes = Encoding.Unicode.GetBytes(WorkstationName ?? "");
                byte[] lmBytes = LmChallengeResponse ?? new byte[0];
                byte[] ntBytes = NtChallengeResponse ?? new byte[0];
                byte[] sessionKeyBytes = EncryptedRandomSessionKey ?? new byte[0];

                // Calculate base offset
                int baseOffset = 64; // Fixed header size
                if (Version != null)
                    baseOffset += 8;
                if (MIC != null)
                    baseOffset += 16;

                // Calculate offsets
                int lmOffset = baseOffset;
                int ntOffset = lmOffset + lmBytes.Length;
                int domainOffset = ntOffset + ntBytes.Length;
                int userOffset = domainOffset + domainBytes.Length;
                int workstationOffset = userOffset + userBytes.Length;
                int sessionKeyOffset = workstationOffset + workstationBytes.Length;

                // LM Challenge Response
                writer.Write((ushort)lmBytes.Length);
                writer.Write((ushort)lmBytes.Length);
                writer.Write((uint)lmOffset);

                // NT Challenge Response
                writer.Write((ushort)ntBytes.Length);
                writer.Write((ushort)ntBytes.Length);
                writer.Write((uint)ntOffset);

                // Domain Name
                writer.Write((ushort)domainBytes.Length);
                writer.Write((ushort)domainBytes.Length);
                writer.Write((uint)domainOffset);

                // User Name
                writer.Write((ushort)userBytes.Length);
                writer.Write((ushort)userBytes.Length);
                writer.Write((uint)userOffset);

                // Workstation
                writer.Write((ushort)workstationBytes.Length);
                writer.Write((ushort)workstationBytes.Length);
                writer.Write((uint)workstationOffset);

                // Encrypted Random Session Key
                writer.Write((ushort)sessionKeyBytes.Length);
                writer.Write((ushort)sessionKeyBytes.Length);
                writer.Write((uint)sessionKeyOffset);

                // Flags
                writer.Write((uint)Flags);

                // Version
                if (Version != null)
                {
                    writer.Write(Version.ToBytes());
                }

                // MIC
                if (MIC != null)
                {
                    writer.Write(MIC);
                }

                // Payload
                writer.Write(lmBytes);
                writer.Write(ntBytes);
                writer.Write(domainBytes);
                writer.Write(userBytes);
                writer.Write(workstationBytes);
                writer.Write(sessionKeyBytes);

                return ms.ToArray();
            }
        }
    }

    #endregion

    #region NTLM Helper Functions

    /// <summary>
    /// NTLM Helper functions for authentication
    /// </summary>
    public static class NtlmHelper
    {
        private static readonly byte[] KnownDesInput = Encoding.ASCII.GetBytes("KGS!@#$%");

        /// <summary>
        /// Compute NTLM hash (NT hash) from password
        /// </summary>
        public static byte[] ComputeNtHash(string password)
        {
            if (string.IsNullOrEmpty(password))
                password = "";

            byte[] passwordBytes = Encoding.Unicode.GetBytes(password);

            using (var md4 = MD4.Create())
            {
                return md4.ComputeHash(passwordBytes);
            }
        }

        /// <summary>
        /// Compute LM hash from password
        /// </summary>
        public static byte[] ComputeLmHash(string password)
        {
            if (string.IsNullOrEmpty(password))
                return new byte[16];

            // LM hash only supports ASCII characters
            try
            {
                password = password.ToUpper();
                byte[] passwordBytes = Encoding.ASCII.GetBytes(password.PadRight(14, '\0').Substring(0, 14));

                byte[] lmHash = new byte[16];
                Array.Copy(DesEncrypt(passwordBytes, 0, KnownDesInput), 0, lmHash, 0, 8);
                Array.Copy(DesEncrypt(passwordBytes, 7, KnownDesInput), 0, lmHash, 8, 8);

                return lmHash;
            }
            catch
            {
                // If password contains non-ASCII characters, return default LM hash
                return new byte[] { 0xAA, 0xD3, 0xB4, 0x35, 0xB5, 0x14, 0x04, 0xEE, 
                                   0xAA, 0xD3, 0xB4, 0x35, 0xB5, 0x14, 0x04, 0xEE };
            }
        }

        /// <summary>
        /// Compute NTLMv2 hash
        /// </summary>
        public static byte[] ComputeNtlmV2Hash(string username, string password, string domain)
        {
            byte[] ntHash = ComputeNtHash(password);
            string identity = (username?.ToUpper() ?? "") + (domain?.ToUpper() ?? "");
            byte[] identityBytes = Encoding.Unicode.GetBytes(identity);

            using (var hmac = new HMACMD5(ntHash))
            {
                return hmac.ComputeHash(identityBytes);
            }
        }

        /// <summary>
        /// Generate NTLMv2 Response
        /// </summary>
        public static byte[] ComputeNtlmV2Response(
            string username,
            string password,
            string domain,
            byte[] serverChallenge,
            byte[] targetInfo,
            byte[] clientChallenge,
            byte[] timestamp = null,
            byte[] channelBindingValue = null,
            string servicePrincipalName = null)
        {
            // Generate NTLMv2 hash
            byte[] ntlmV2Hash = ComputeNtlmV2Hash(username, password, domain);

            // Parse and modify target info
            NtlmAvPairs avPairs = targetInfo != null ? NtlmAvPairs.FromBytes(targetInfo) : new NtlmAvPairs();

            // Add channel bindings if provided
            if (channelBindingValue != null && channelBindingValue.Length > 0)
            {
                avPairs.Add(AvPairType.ChannelBindings, channelBindingValue);
            }

            // Add target name (SPN) if provided
            if (!string.IsNullOrEmpty(servicePrincipalName))
            {
                avPairs.Add(AvPairType.TargetName, Encoding.Unicode.GetBytes(servicePrincipalName));
            }

            // Get or create timestamp
            byte[] time;
            if (timestamp != null && timestamp.Length == 8)
            {
                time = timestamp;
            }
            else if (avPairs.Contains(AvPairType.Timestamp))
            {
                time = avPairs.Get(AvPairType.Timestamp);
            }
            else
            {
                time = BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc());
                avPairs.Add(AvPairType.Timestamp, time);
            }

            byte[] modifiedTargetInfo = avPairs.ToBytes();

            // Build the blob
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write((byte)0x01); // RespType
                writer.Write((byte)0x01); // HiRespType
                writer.Write(new byte[2]); // Reserved1
                writer.Write(new byte[4]); // Reserved2
                writer.Write(time); // Timestamp
                writer.Write(clientChallenge); // ClientChallenge
                writer.Write(new byte[4]); // Reserved3
                writer.Write(modifiedTargetInfo); // TargetInfo

                byte[] blob = ms.ToArray();

                // Compute NTProofStr = HMAC_MD5(NTLMv2Hash, ServerChallenge + Blob)
                byte[] challengeBlob = new byte[serverChallenge.Length + blob.Length];
                Array.Copy(serverChallenge, 0, challengeBlob, 0, serverChallenge.Length);
                Array.Copy(blob, 0, challengeBlob, serverChallenge.Length, blob.Length);

                byte[] ntProofStr;
                using (var hmac = new HMACMD5(ntlmV2Hash))
                {
                    ntProofStr = hmac.ComputeHash(challengeBlob);
                }

                // NTLMv2 Response = NTProofStr + Blob
                byte[] response = new byte[ntProofStr.Length + blob.Length];
                Array.Copy(ntProofStr, 0, response, 0, ntProofStr.Length);
                Array.Copy(blob, 0, response, ntProofStr.Length, blob.Length);

                return response;
            }
        }

        /// <summary>
        /// Compute LMv2 Response
        /// </summary>
        public static byte[] ComputeLmV2Response(
            string username,
            string password,
            string domain,
            byte[] serverChallenge,
            byte[] clientChallenge)
        {
            byte[] ntlmV2Hash = ComputeNtlmV2Hash(username, password, domain);

            byte[] data = new byte[serverChallenge.Length + clientChallenge.Length];
            Array.Copy(serverChallenge, 0, data, 0, serverChallenge.Length);
            Array.Copy(clientChallenge, 0, data, serverChallenge.Length, clientChallenge.Length);

            byte[] hash;
            using (var hmac = new HMACMD5(ntlmV2Hash))
            {
                hash = hmac.ComputeHash(data);
            }

            byte[] response = new byte[hash.Length + clientChallenge.Length];
            Array.Copy(hash, 0, response, 0, hash.Length);
            Array.Copy(clientChallenge, 0, response, hash.Length, clientChallenge.Length);

            return response;
        }

        /// <summary>
        /// DES encrypt using 7-byte key
        /// </summary>
        private static byte[] DesEncrypt(byte[] key, int offset, byte[] data)
        {
            byte[] desKey = ExpandDesKey(key, offset);
            using (var des = DES.Create())
            {
                des.Mode = CipherMode.ECB;
                des.Padding = PaddingMode.None;
                using (var encryptor = des.CreateEncryptor(desKey, null))
                {
                    return encryptor.TransformFinalBlock(data, 0, data.Length);
                }
            }
        }

        /// <summary>
        /// Expand 7-byte key to 8-byte DES key
        /// </summary>
        private static byte[] ExpandDesKey(byte[] key, int offset)
        {
            byte[] expandedKey = new byte[8];
            byte[] keyBytes = new byte[7];

            int length = Math.Min(7, key.Length - offset);
            Array.Copy(key, offset, keyBytes, 0, length);

            expandedKey[0] = (byte)(((keyBytes[0] >> 1) & 0x7F) << 1);
            expandedKey[1] = (byte)(((keyBytes[0] & 0x01) << 6 | ((keyBytes[1] >> 2) & 0x3F)) << 1);
            expandedKey[2] = (byte)(((keyBytes[1] & 0x03) << 5 | ((keyBytes[2] >> 3) & 0x1F)) << 1);
            expandedKey[3] = (byte)(((keyBytes[2] & 0x07) << 4 | ((keyBytes[3] >> 4) & 0x0F)) << 1);
            expandedKey[4] = (byte)(((keyBytes[3] & 0x0F) << 3 | ((keyBytes[4] >> 5) & 0x07)) << 1);
            expandedKey[5] = (byte)(((keyBytes[4] & 0x1F) << 2 | ((keyBytes[5] >> 6) & 0x03)) << 1);
            expandedKey[6] = (byte)(((keyBytes[5] & 0x3F) << 1 | ((keyBytes[6] >> 7) & 0x01)) << 1);
            expandedKey[7] = (byte)((keyBytes[6] & 0x7F) << 1);

            return expandedKey;
        }

        /// <summary>
        /// Generate random client challenge
        /// </summary>
        public static byte[] GenerateClientChallenge(int length = 8)
        {
            byte[] challenge = new byte[length];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(challenge);
            }
            return challenge;
        }

        /// <summary>
        /// Compute Session Base Key for NTLMv2
        /// </summary>
        public static byte[] ComputeSessionBaseKey(byte[] ntlmV2Hash, byte[] ntProofStr)
        {
            using (var hmac = new HMACMD5(ntlmV2Hash))
            {
                return hmac.ComputeHash(ntProofStr);
            }
        }

        /// <summary>
        /// Compute HMAC-MD5
        /// </summary>
        public static byte[] HmacMd5(byte[] key, byte[] data)
        {
            using (var hmac = new HMACMD5(key))
            {
                return hmac.ComputeHash(data);
            }
        }
    }

    #endregion

    #region MD4 Implementation

    /// <summary>
    /// MD4 hash algorithm (required for NTLM)
    /// </summary>
    public class MD4 : HashAlgorithm
    {
        private uint[] _state = new uint[4];
        private byte[] _buffer = new byte[64];
        private uint[] _count = new uint[2];
        private byte[] _digest = new byte[16];

        public static new MD4 Create()
        {
            return new MD4();
        }

        public MD4()
        {
            Initialize();
        }

        public override void Initialize()
        {
            _state[0] = 0x67452301;
            _state[1] = 0xEFCDAB89;
            _state[2] = 0x98BADCFE;
            _state[3] = 0x10325476;
            _count[0] = 0;
            _count[1] = 0;
        }

        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            unchecked
            {
                int bufferIndex = (int)((_count[0] >> 3) & 0x3F);
                _count[0] += (uint)(cbSize << 3);
                if (_count[0] < (cbSize << 3))
                    _count[1]++;
                _count[1] += (uint)(cbSize >> 29);

                int partLen = 64 - bufferIndex;
                int i = 0;

                if (cbSize >= partLen)
                {
                    Array.Copy(array, ibStart, _buffer, bufferIndex, partLen);
                    Transform(_buffer, 0);

                    for (i = partLen; i + 63 < cbSize; i += 64)
                        Transform(array, ibStart + i);

                    bufferIndex = 0;
                }

                Array.Copy(array, ibStart + i, _buffer, bufferIndex, cbSize - i);
            }
        }

        protected override byte[] HashFinal()
        {
            byte[] bits = new byte[8];
            Encode(bits, _count);

            uint index = (_count[0] >> 3) & 0x3f;
            uint padLen = (index < 56) ? (56 - index) : (120 - index);
            byte[] padding = new byte[padLen];
            padding[0] = 0x80;

            HashCore(padding, 0, (int)padLen);
            HashCore(bits, 0, 8);
            Encode(_digest, _state);

            return _digest;
        }

        private void Transform(byte[] block, int offset)
        {
            uint a = _state[0], b = _state[1], c = _state[2], d = _state[3];
            uint[] x = new uint[16];
            Decode(x, block, offset);

            unchecked
            {
                // Round 1
                a = FF(a, b, c, d, x[0], 3);
                d = FF(d, a, b, c, x[1], 7);
                c = FF(c, d, a, b, x[2], 11);
                b = FF(b, c, d, a, x[3], 19);
                a = FF(a, b, c, d, x[4], 3);
                d = FF(d, a, b, c, x[5], 7);
                c = FF(c, d, a, b, x[6], 11);
                b = FF(b, c, d, a, x[7], 19);
                a = FF(a, b, c, d, x[8], 3);
                d = FF(d, a, b, c, x[9], 7);
                c = FF(c, d, a, b, x[10], 11);
                b = FF(b, c, d, a, x[11], 19);
                a = FF(a, b, c, d, x[12], 3);
                d = FF(d, a, b, c, x[13], 7);
                c = FF(c, d, a, b, x[14], 11);
                b = FF(b, c, d, a, x[15], 19);

                // Round 2
                a = GG(a, b, c, d, x[0], 3);
                d = GG(d, a, b, c, x[4], 5);
                c = GG(c, d, a, b, x[8], 9);
                b = GG(b, c, d, a, x[12], 13);
                a = GG(a, b, c, d, x[1], 3);
                d = GG(d, a, b, c, x[5], 5);
                c = GG(c, d, a, b, x[9], 9);
                b = GG(b, c, d, a, x[13], 13);
                a = GG(a, b, c, d, x[2], 3);
                d = GG(d, a, b, c, x[6], 5);
                c = GG(c, d, a, b, x[10], 9);
                b = GG(b, c, d, a, x[14], 13);
                a = GG(a, b, c, d, x[3], 3);
                d = GG(d, a, b, c, x[7], 5);
                c = GG(c, d, a, b, x[11], 9);
                b = GG(b, c, d, a, x[15], 13);

                // Round 3
                a = HH(a, b, c, d, x[0], 3);
                d = HH(d, a, b, c, x[8], 9);
                c = HH(c, d, a, b, x[4], 11);
                b = HH(b, c, d, a, x[12], 15);
                a = HH(a, b, c, d, x[2], 3);
                d = HH(d, a, b, c, x[10], 9);
                c = HH(c, d, a, b, x[6], 11);
                b = HH(b, c, d, a, x[14], 15);
                a = HH(a, b, c, d, x[1], 3);
                d = HH(d, a, b, c, x[9], 9);
                c = HH(c, d, a, b, x[5], 11);
                b = HH(b, c, d, a, x[13], 15);
                a = HH(a, b, c, d, x[3], 3);
                d = HH(d, a, b, c, x[11], 9);
                c = HH(c, d, a, b, x[7], 11);
                b = HH(b, c, d, a, x[15], 15);

                _state[0] += a;
                _state[1] += b;
                _state[2] += c;
                _state[3] += d;
            }
        }

        private static uint F(uint x, uint y, uint z) => (x & y) | (~x & z);
        private static uint G(uint x, uint y, uint z) => (x & y) | (x & z) | (y & z);
        private static uint H(uint x, uint y, uint z) => x ^ y ^ z;

        private static uint RotateLeft(uint x, int n) => (x << n) | (x >> (32 - n));

        private static uint FF(uint a, uint b, uint c, uint d, uint x, int s)
        {
            unchecked
            {
                return RotateLeft(a + F(b, c, d) + x, s);
            }
        }

        private static uint GG(uint a, uint b, uint c, uint d, uint x, int s)
        {
            unchecked
            {
                return RotateLeft(a + G(b, c, d) + x + 0x5A827999, s);
            }
        }

        private static uint HH(uint a, uint b, uint c, uint d, uint x, int s)
        {
            unchecked
            {
                return RotateLeft(a + H(b, c, d) + x + 0x6ED9EBA1, s);
            }
        }

        private static void Encode(byte[] output, uint[] input)
        {
            for (int i = 0, j = 0; j < output.Length; i++, j += 4)
            {
                output[j] = (byte)(input[i] & 0xff);
                output[j + 1] = (byte)((input[i] >> 8) & 0xff);
                output[j + 2] = (byte)((input[i] >> 16) & 0xff);
                output[j + 3] = (byte)((input[i] >> 24) & 0xff);
            }
        }

        private static void Decode(uint[] output, byte[] input, int offset)
        {
            for (int i = 0, j = 0; i < output.Length; i++, j += 4)
            {
                output[i] = (uint)(input[offset + j] |
                                  (input[offset + j + 1] << 8) |
                                  (input[offset + j + 2] << 16) |
                                  (input[offset + j + 3] << 24));
            }
        }
    }

    #endregion
}
