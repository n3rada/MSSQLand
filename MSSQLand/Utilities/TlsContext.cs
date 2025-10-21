using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading.Tasks;

namespace MSSQLand.Utilities
{
    /// <summary>
    /// TLS context manager that wraps SslStream with MemoryBIO functionality
    /// to support TDS-wrapped TLS communication (mimics impacket's approach)
    /// </summary>
    public class TlsContext : IDisposable
    {
        private readonly MemoryBioStream bioStream;
        private readonly SslStream sslStream;
        private Task authenticationTask;
        private bool isHandshakeComplete = false;
        private byte[] tlsUniqueChannelBinding = null;

        public bool IsHandshakeComplete => isHandshakeComplete;
        public byte[] TlsUniqueChannelBinding => tlsUniqueChannelBinding;

        public TlsContext()
        {
            bioStream = new MemoryBioStream();
            sslStream = new SslStream(
                bioStream,
                false,
                (sender, cert, chain, errors) => true, // Accept all certificates
                null
            );
        }

        /// <summary>
        /// Start the TLS handshake - call this once at the beginning
        /// </summary>
        public void StartHandshake()
        {
            if (authenticationTask != null)
                throw new InvalidOperationException("Handshake already started");

            authenticationTask = sslStream.AuthenticateAsClientAsync(
                "SQLServer",
                null,
                SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls,
                false
            );
        }

        /// <summary>
        /// Perform TLS handshake step-by-step with external control over data exchange
        /// This mimics impacket's do_handshake() with SSLWantReadError handling
        /// </summary>
        /// <returns>Encrypted data to send, or null if handshake is complete</returns>
        public async Task<byte[]> PerformHandshakeStepAsync()
        {
            if (authenticationTask == null)
                throw new InvalidOperationException("Handshake not started - call StartHandshake() first");

            if (isHandshakeComplete)
                return null;

            // Give the authentication task time to process
            await Task.Delay(50);

            // Check if we have outgoing data (TLS handshake messages)
            var outgoingData = bioStream.GetOutgoingData();

            // Check if authentication is complete
            if (authenticationTask.IsCompleted)
            {
                try
                {
                    await authenticationTask; // This will throw if there was an error
                    
                    // Handshake complete
                    isHandshakeComplete = true;
                    
                    // Try to get tls-unique (not directly available in .NET Framework)
                    // For now, we'll leave it empty
                    tlsUniqueChannelBinding = new byte[0];
                }
                catch
                {
                    // If authentication failed, check if we have data to send anyway
                    if (outgoingData.Length == 0)
                        throw;
                }
            }

            return outgoingData.Length > 0 ? outgoingData : null;
        }

        /// <summary>
        /// Feed encrypted data received from server into the TLS context
        /// This mimics Python's in_bio.write()
        /// </summary>
        public void FeedIncomingData(byte[] data)
        {
            bioStream.FeedIncomingData(data);
        }

        /// <summary>
        /// Get encrypted data that should be sent to server
        /// This mimics Python's out_bio.read()
        /// </summary>
        public byte[] GetOutgoingData()
        {
            return bioStream.GetOutgoingData();
        }

        /// <summary>
        /// Write plaintext data to be encrypted
        /// </summary>
        public async Task WriteAsync(byte[] data)
        {
            if (!isHandshakeComplete)
                throw new InvalidOperationException("Handshake not complete");

            await sslStream.WriteAsync(data, 0, data.Length);
            await sslStream.FlushAsync();
        }

        /// <summary>
        /// Read decrypted plaintext data
        /// </summary>
        public async Task<byte[]> ReadAsync(int maxLength)
        {
            if (!isHandshakeComplete)
                throw new InvalidOperationException("Handshake not complete");

            var buffer = new byte[maxLength];
            int bytesRead = await sslStream.ReadAsync(buffer, 0, maxLength);

            if (bytesRead == 0)
                return new byte[0];

            if (bytesRead < maxLength)
            {
                var result = new byte[bytesRead];
                Array.Copy(buffer, result, bytesRead);
                return result;
            }

            return buffer;
        }

        /// <summary>
        /// Check if SSL is authenticated
        /// </summary>
        public bool IsAuthenticated => sslStream.IsAuthenticated;

        /// <summary>
        /// Get SSL protocol version
        /// </summary>
        public SslProtocols SslProtocol => sslStream.SslProtocol;

        public void Dispose()
        {
            sslStream?.Dispose();
            bioStream?.Dispose();
        }
    }
}
