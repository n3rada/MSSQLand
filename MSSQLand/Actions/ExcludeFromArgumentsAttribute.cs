using System;

namespace MSSQLand.Utilities
{
    [AttributeUsage(AttributeTargets.Field)]
    public class ExcludeFromArgumentsAttribute : Attribute
    {
    }

}
