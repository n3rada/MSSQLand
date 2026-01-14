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
    [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
    internal class WindowsIdentityImpersonation : IDisposable
    {
        private readonly SafeTokenHandle _handle;
        private readonly WindowsImpersonationContext _context;

        const int Logon32LogonNewCredentials = 9;

        internal WindowsIdentityImpersonation(string domain, string username, string password)
        {
            Logger.Trace($"LogonUser: {domain}\\{username} (LOGON32_LOGON_NEW_CREDENTIALS)");
            
            bool ok = LogonUser(username, domain, password, Logon32LogonNewCredentials, 0, out this._handle);
            if (!ok)
            {
                int errorCode = Marshal.GetLastWin32Error();
                string errorMessage = new Win32Exception(errorCode).Message;
                Logger.Trace($"LogonUser failed: error {errorCode} - {errorMessage}");
                throw new ApplicationException($"Impersonation failed for {domain}\\{username}: {errorMessage} (Win32 error {errorCode})");
            }

            Logger.Trace($"LogonUser succeeded, impersonating token");
            this._context = WindowsIdentity.Impersonate(this._handle.DangerousGetHandle());
            Logger.Trace($"Now impersonating: {WindowsIdentity.GetCurrent().Name}");
        }

        public void Dispose()
        {
            this._context.Dispose();
            this._handle.Dispose();
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
