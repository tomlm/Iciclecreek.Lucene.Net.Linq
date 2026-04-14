using Lucene.Net.Linq.Util;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lucene.Net.Linq.Mapping
{
    internal static class TypeUtils
    {
        public const string TYPE_FIELD = "_type_";
        public const string TYPES_FIELD = "_types_";

        /// <summary>
        /// Walks the BaseType chain from the given type up to (but not including) object.
        /// </summary>
        internal static IEnumerable<Type> GetTypeHierarchy(Type type)
        {
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                yield return current;
            }
        }

        static readonly Dictionary<string, Type> _cache = new();

        /// <summary>
        /// Resolve Type.FullName => Type
        /// </summary>
        /// <param name="fullName"></param>
        /// <returns></returns>
        internal static Type ResolveType(string fullName)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(fullName, out var t))
                    return t;

                foreach (var type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.DefinedTypes))
                {
                    _cache[type.FullName!] = type;
                }

                return _cache[fullName];
            }
        }
    }
}
