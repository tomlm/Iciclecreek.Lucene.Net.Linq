using System;

namespace Lucene.Net.Linq.Search
{
    // See FieldComparator.cs for the TODO Lucene 4.8 port note.
    public abstract class NonGenericFieldComparator<T> : FieldComparator<T> where T : IComparable
    {
        protected NonGenericFieldComparator(int numHits, string field) : base(numHits, field) { }
    }
}
