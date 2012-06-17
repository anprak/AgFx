using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System.Reflection
{
    public static class Extensions
    {
        public static bool IsAssignableFrom(this Type type, Type c)
        {
            return type.IsAssignableFrom(c);
        }

        public static bool IConvertibleIsAssignableFrom(this Type c)
        {
            return typeof(IConvertible).IsAssignableFrom(c);
        }

        public static bool IsInstanceOfType(this Type type, object o)
        {
            return type.IsInstanceOfType(o);
        }

        public static IEnumerable<PropertyInfo> GetPublicInstanceReadWriteProperties(this Type type)
        {
            return from p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                   where p.CanRead && p.CanWrite
                   select p;
        }
    }
}
