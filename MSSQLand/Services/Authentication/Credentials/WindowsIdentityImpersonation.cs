using Microsoft.Win32.SafeHandles;
using MSSQLand.Utilities;
using System;
using System.ComponentModel;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Permissions;
using System.Security.Principal;

namespace MSSQLand.Services.Credentials
{
    /// <summary>
    /// Impersonates a Windows identity via LogonUser (type 9 / NewCredentials) for use in
    /// SqlConnection authentication. Type 9 is pass-through: the local thread identity does
    /// not change, but the supplied credentials are presented during remote network
    /// authentication (TDS handshake). This is the correct and only needed semantic here
    /// since this class exists exclusively to service remote SqlConnection calls.
    /// </summary>
    [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
    internal class WindowsIdentityImpersonation : IDisposable
    {
        private SafeTokenHandle _handle;
        private WindowsImpersonationContext _context;
        private bool _disposed;

        private const int Logon32LogonNewCredentials = 9;

        internal WindowsIdentityImpersonation(string domain, string username, string password)
        {
            Logger.Trace($"Impersonating as {domain}\\{username}");
            Logger.TraceNested($"with password: {password}");

            bool ok = LogonUser(username, domain, password, Logon32LogonNewCredentials, 0, out _handle);
            if (!ok)
            {
                int errorCode = Marshal.GetLastWin32Error();
                string errorMessage = new Win32Exception(errorCode).Message;
                Logger.Trace($"LogonUser failed: error {errorCode} - {errorMessage}");
                throw new ApplicationException($"Impersonation failed for {domain}\\{username}: {errorMessage} (Win32 error {errorCode})");
            }

            Logger.Trace($"LogonUser succeeded, impersonating token");
            _context = WindowsIdentity.Impersonate(_handle.DangerousGetHandle());

            // Type 9 is pass-through: local identity intentionally does not change.
            // Supplied credentials will be presented during the remote TDS authentication.
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _context?.Dispose();
            _context = null;

            _handle?.Dispose();
            _handle = null;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(
            string lpszUsername,
            string lpszDomain,
            string lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            out SafeTokenHandle phToken);

        private sealed class SafeTokenHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private SafeTokenHandle()
                : base(true) { }

            [DllImport("kernel32.dll")]
            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
            [SuppressUnmanagedCodeSecurity]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CloseHandle(IntPtr handle);

            protected override bool ReleaseHandle()
            {
                return CloseHandle(handle);
            }
        }
    }
}