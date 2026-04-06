// Stage 4 cleanup: this file used to provide GetCustomAttribute<T>/
// GetCustomAttributes<T> extension methods on MemberInfo. Those are now
// shipped by the BCL in System.Reflection.CustomAttributeExtensions, so
// the local helpers caused ambiguous-overload errors. Kept as an empty
// placeholder to avoid removing the file from the project; existing
// callers transparently use the BCL versions.
namespace Lucene.Net.Linq.Util
{
}
