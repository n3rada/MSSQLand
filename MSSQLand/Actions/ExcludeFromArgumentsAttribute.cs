// MSSQLand/Actions/ExcludeFromArgumentsAttribute.cs

using System;

namespace MSSQLand.Actions
{
    [AttributeUsage(AttributeTargets.Field)]
    public class ExcludeFromArgumentsAttribute : Attribute
    {
    }

}
