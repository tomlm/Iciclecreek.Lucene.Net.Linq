using System;
using System.Collections.Generic;

namespace Lucene.Net.Linq.Util
{
    /// <summary>
    /// Tiny generic registry that scans the declaring assembly for concrete
    /// subclasses of <typeparamref name="TItem"/> and lets each contribute one
    /// or more <typeparamref name="TKey"/> bindings via
    /// <see cref="RegisterForTypes"/>. Replaces the historical use of
    /// <c>Remotion.Linq.Utilities.RegistryBase</c>, which was removed in re-linq 2.x.
    /// </summary>
    internal abstract class RegistryBase<TRegistry, TKey, TItem>
        where TRegistry : RegistryBase<TRegistry, TKey, TItem>, new()
    {
        private readonly Dictionary<TKey, TItem> _items = new Dictionary<TKey, TItem>();

        public static TRegistry CreateDefault()
        {
            var registry = new TRegistry();
            var itemTypes = new List<Type>();
            foreach (var t in typeof(TRegistry).Assembly.GetTypes())
            {
                if (!t.IsClass || t.IsAbstract) continue;
                if (!typeof(TItem).IsAssignableFrom(t)) continue;
                itemTypes.Add(t);
            }
            registry.RegisterForTypes(itemTypes);
            return registry;
        }

        protected abstract void RegisterForTypes(IEnumerable<Type> itemTypes);

        public void Register(IEnumerable<TKey> keys, TItem item)
        {
            foreach (var key in keys) _items[key] = item;
        }

        public TItem GetItemExact(TKey key)
            => _items.TryGetValue(key, out var v) ? v : default;

        public abstract TItem GetItem(TKey key);
    }
}
