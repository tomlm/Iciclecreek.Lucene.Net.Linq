using Lucene.Net.Documents;
using Lucene.Net.Index;

namespace Lucene.Net.Linq.Mapping
{
    /// <summary>
    /// Translates the (StoreMode, IndexMode, TermVectorMode) triple from the
    /// Lucene 3.x mapping API onto a Lucene 4.8 <see cref="FieldType"/>.
    /// </summary>
    internal static class FieldTypeBuilder
    {
        public static FieldType Build(StoreMode store, IndexMode index, TermVectorMode termVector)
        {
            var ft = new FieldType
            {
                IsStored = store == StoreMode.Yes,
            };

            switch (index)
            {
                case IndexMode.NotIndexed:
                    ft.IsIndexed = false;
                    ft.IsTokenized = false;
                    ft.IndexOptions = IndexOptions.NONE;
                    break;
                case IndexMode.Analyzed:
                    ft.IsIndexed = true;
                    ft.IsTokenized = true;
                    ft.OmitNorms = false;
                    ft.IndexOptions = IndexOptions.DOCS_AND_FREQS_AND_POSITIONS;
                    break;
                case IndexMode.AnalyzedNoNorms:
                    ft.IsIndexed = true;
                    ft.IsTokenized = true;
                    ft.OmitNorms = true;
                    ft.IndexOptions = IndexOptions.DOCS_AND_FREQS_AND_POSITIONS;
                    break;
                case IndexMode.NotAnalyzed:
                    ft.IsIndexed = true;
                    ft.IsTokenized = false;
                    ft.OmitNorms = false;
                    ft.IndexOptions = IndexOptions.DOCS_AND_FREQS_AND_POSITIONS;
                    break;
                case IndexMode.NotAnalyzedNoNorms:
                    ft.IsIndexed = true;
                    ft.IsTokenized = false;
                    ft.OmitNorms = true;
                    ft.IndexOptions = IndexOptions.DOCS_AND_FREQS_AND_POSITIONS;
                    break;
            }

            switch (termVector)
            {
                case TermVectorMode.No:
                    ft.StoreTermVectors = false;
                    break;
                case TermVectorMode.Yes:
                    ft.StoreTermVectors = true;
                    break;
                case TermVectorMode.WithOffsets:
                    ft.StoreTermVectors = true;
                    ft.StoreTermVectorOffsets = true;
                    break;
                case TermVectorMode.WithPositions:
                    ft.StoreTermVectors = true;
                    ft.StoreTermVectorPositions = true;
                    break;
                case TermVectorMode.WithPositionsAndOffsets:
                    ft.StoreTermVectors = true;
                    ft.StoreTermVectorPositions = true;
                    ft.StoreTermVectorOffsets = true;
                    break;
            }

            ft.Freeze();
            return ft;
        }
    }
}
