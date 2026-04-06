// TODO Lucene 4.8 port: the converter-based custom sort path used the
// Lucene 3.x FieldComparator<T> + FieldCache_Fields.GetStrings APIs,
// neither of which exist in Lucene.Net 4.8 in the same shape. The classes
// in this folder are kept as compile-time placeholders so the rest of the
// library can build; they are not currently wired up. ReflectionFieldMapper
// .CreateSortField falls back to a string SortField when a converter is
// configured (see the comment there). Re-enable converter sort by porting
// to FieldComparer<T> + FieldCache.GetTerms in a future stage.

namespace Lucene.Net.Linq.Search
{
    public abstract class FieldComparator<T>
    {
        protected string field;
        protected T[] values;
        protected T[] currentReaderValues;
        protected T bottom;

        protected FieldComparator(int numHits, string field)
        {
            this.field = field;
            this.values = new T[numHits];
        }
    }
}
