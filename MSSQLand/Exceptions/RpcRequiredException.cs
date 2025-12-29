using System;

namespace MSSQLand.Exceptions
{
    /// <summary>
    /// Exception thrown when a query requires RPC but RPC is not available on the linked server.
    /// </summary>
    public class RpcRequiredException : Exception
    {
        public string Query { get; }

        public RpcRequiredException(string query) 
            : base("This query requires RPC (Remote Procedure Call) which is not available or disabled on the linked server.")
        {
            Query = query;
        }

        public RpcRequiredException(string query, string message) 
            : base(message)
        {
            Query = query;
        }
    }
}
