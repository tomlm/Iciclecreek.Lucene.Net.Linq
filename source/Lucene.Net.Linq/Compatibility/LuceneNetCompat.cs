// Compatibility shims for the Lucene.Net 3.x → 4.8 API migration.
//
// The single biggest source of churn in the Lucene.Net 3 → 4.8 rewrite is
// that Lucene.Net.Util.Version (a class with named constants) was replaced
// by Lucene.Net.Util.LuceneVersion (an enum). This library accepts a Version
// argument throughout its public surface (constructors of LuceneDataProvider,
// DocumentMapperBase, etc.). Rather than break that public API for every
// downstream consumer, we restore the original symbol with a struct that
// implicitly converts to/from LuceneVersion. The integer/version value the
// caller passes is ignored — per the Stage 1 decision to "accept Lucene 4.8
// defaults", every constant maps to LuceneVersion.LUCENE_48 internally.

using Lucene.Net.Util;

namespace Lucene.Net.Util
{
    /// <summary>
    /// Source-compat replacement for the removed Lucene.Net.Util.Version
    /// type. Implicitly converts to/from <see cref="LuceneVersion"/>; all
    /// historical version constants (LUCENE_29, LUCENE_30, ...) collapse to
    /// <see cref="LuceneVersion.LUCENE_48"/> at runtime.
    /// </summary>
    public readonly struct Version
    {
        private readonly LuceneVersion _value;

        public Version(LuceneVersion value) { _value = value; }

        public LuceneVersion Value => _value;

        public static implicit operator LuceneVersion(Version v) => v._value;
        public static implicit operator Version(LuceneVersion v) => new Version(v);

        public override string ToString() => _value.ToString();

        // Lucene 3.x version constants — every value collapses to 4.8.
        public static readonly Version LUCENE_24 = new Version(LuceneVersion.LUCENE_48);
        public static readonly Version LUCENE_29 = new Version(LuceneVersion.LUCENE_48);
        public static readonly Version LUCENE_30 = new Version(LuceneVersion.LUCENE_48);
        public static readonly Version LUCENE_CURRENT = new Version(LuceneVersion.LUCENE_48);
        public static readonly Version LUCENE_48 = new Version(LuceneVersion.LUCENE_48);
    }
}
