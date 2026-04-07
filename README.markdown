## LINQ to Lucene.Net

[![Build status](https://ci.appveyor.com/api/projects/status/voelauhwvv1l8j2f)](https://ci.appveyor.com/project/chriseldredge/lucene-net-linq)

Lucene.Net.Linq is a .net library that enables LINQ queries to run natively on a Lucene.Net index.

### 4.0.0-alpha1 — Lucene.Net 4.8 / .NET 8 port

This branch ports the library from the original `Lucene.Net 3.0.3` /
`net40` baseline onto `Lucene.Net 4.8.0-beta00017` and SDK-style projects
multi-targeting `netstandard2.0;net8.0`. Highlights:

- **Lucene.Net 4.8.0-beta00017** for the index, query, analysis, and
  query-parser packages.
- **Remotion.Linq 2.2.0** for the LINQ provider plumbing.
- **Microsoft.Extensions.Logging.Abstractions** replaces `Common.Logging`.
  Wire your logger via `Lucene.Net.Linq.Util.Logging.LoggerFactory =
  myFactory;` (defaults to `NullLoggerFactory`).
- **Tests** moved from RhinoMocks/NUnit 2 to **NSubstitute / NUnit 4**.

#### Behaviour caveats

A handful of subsystems were stubbed during the Lucene 3 → 4.8 sweep
because the underlying Lucene API was removed or substantially reworked.
Each is marked with a `TODO Lucene 4.8 port` comment in source and the
corresponding test is `[Ignore]`'d:

- **Document-level boost** was removed in Lucene 4.8 (only field-level
  boost remains). `[DocumentBoost]` properties are silently ignored at
  index time. The read path returns `1.0f`.
- **Numeric-field boost** is dropped — Lucene 4.8 numeric fields don't
  index norms, so per-field boost on `Int32Field`/`Int64Field`/etc. has
  no effect.
- **Converter-based custom sort** (the `FieldComparator<T>` +
  `FieldCache_Fields.GetStrings` path) is disabled. Properties with a
  `TypeConverter` that need sorting fall back to a string `SortField`;
  mark them with `NativeSort=true` to opt in.
- **Term-vector retrieval** via `TermFreqVectorDocumentMapper<T>` is a
  stub. The Lucene 3 `ITermFreqVector` type is gone; the replacement
  is `IndexReader.GetTermVector` returning `Terms`.
- **`MergePolicyBuilder`** callback in `LuceneDataProviderSettings` now
  receives `null` instead of an `IndexWriter`. Lucene 4.8 requires the
  merge policy to be set on `IndexWriterConfig` *before* the writer is
  constructed; consumers that inspected the writer state must migrate.

If you depend on any of these and want them re-implemented properly,
search the source for `TODO Lucene 4.8 port` for the entry points.

* Automatically converts PONOs to Documents and back
* Add, delete and update documents in atomic transaction
* Unit of Work pattern automatically tracks and flushes updated documents
* Update/replace documents with \[Field(Key=true)\] to prevent duplicates
* Term queries
* Prefix queries
* Range queries and numeric range queries
* Complex boolean queries
* Native pagination using Skip and Take
* Support storing and querying NumericField
* Automatically convert complex types for storing, querying and sorting
* Custom boost functions using IQueryable<T>.Boost() extension method
* Sort by standard string, NumericField or any type that implements IComparable
* Sort by item.Score() extension method to sort by relevance
* Specify custom format for DateTime stored as strings
* Register cache-warming queries to be executed when IndexSearcher is being reloaded

## Available on NuGet Gallery

To install the [Lucene.Net.Linq package](http://nuget.org/packages/Lucene.Net.Linq),
run the following command in the [Package Manager Console](http://docs.nuget.org/docs/start-here/using-the-package-manager-console)

    PM> Install-Package Lucene.Net.Linq

## Examples

1. Using [fluent syntax](source/Lucene.Net.Linq.Tests/Samples/FluentConfiguration.cs) to configure mappings
1. Using [attributes](source/Lucene.Net.Linq.Tests/Samples/AttributeConfiguration.cs) to configure mappings
1. Specifying [document keys](source/Lucene.Net.Linq.Tests/Samples/DocumentKeys.cs)

## Note on Performance

Initial versions of the library include a query filter when your entites specify a document key or key field
in their mappings. The intention of this filter is to ensure that multiple entity types can be stored in a
single index without unexpected errors.

It has been pointed out that this query filter adds significant overhead to query performance and goes
against a best practive of using a different index for each type of document being stored.

To maintain backwards compatibility, the feature is left enabled by default, but it can now be disabled
by doing:

    luceneDataProvider.Settings.EnableMultipleEntities = false;

Future versions of this library may change the default behavior.

## Integration with OData

Lucene.Net.Linq supports both [WCF Data Services](http://msdn.microsoft.com/en-us/library/cc668792.aspx)
and [WebApi OData](http://www.asp.net/web-api/overview/odata-support-in-aspnet-web-api). These libraries
by default support a feature known as Null Propagation that adds null safety to LINQ Expressions to
avoid NullReferenceException from being thrown when operating on a property that may be null.

A simple expression like:

```c#
from doc in Documents where doc.Name.StartsWith("Sample") select doc;
```

Is translated into:

```c#
from doc in Documents where (doc != null && doc.Name != null
    && doc.Name.StartsWith("Sample")) select doc;
```

Null Propagation is designed to work with [LINQ To Objects](http://msdn.microsoft.com/en-ca/library/bb397919.aspx)
but is not required for LINQ providers such as Lucene.Net.Linq. Lucene.Net.Linq does its best to remove
these null-safety checks when translating a LINQ expression tree into a Lucene Query, but for best
performance it is recommended to simply turn the feature off, as in this example:

```c#
public class PackagesODataController : ODataController
{
    [EnableQuery(HandleNullPropagation = HandleNullPropagationOption.False)]
    public IQueryable<Package> Get()
    {
        return provider.AsQueryable<Package>();
    }
}
```

## Upcoming features / ideas / bugs / known issues

See Issues on the GitHub project page.

### Unsupported Characters in Indexed Properties

Some characters, even when using a KeywordAnalyzer or equivalent, will
not be handled correctly by Lucene.Net.Linq, such as `\`, `:`, `?` and `*`
because these characters have special meaning to Lucene's query parser.

This means if you want to index a DOS style path such as `c:\dos` and
later retrieve documents using the same term, it will not work properly.

These characters are perfectly fine for fields that will be analyzed
by a tokenizer that would remove them, but exact matching on the entire
value is not possible.

If exact matching is required, these characters should be replaced
with suitable substitutes that are not reserved by Lucene.
