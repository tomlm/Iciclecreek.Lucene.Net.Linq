## LINQ to Lucene.Net modern

[![Build status](https://ci.appveyor.com/api/projects/status/voelauhwvv1l8j2f)](https://ci.appveyor.com/project/chriseldredge/lucene-net-linq)

Lucene.Net.LinqX is a .net library that enables LINQ queries to run natively on a Lucene.Net index.

### Lucene.Net 4.8 / .NET 8 port

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

#### New in the 4.8 port

- **DocValues opt-in** (`[Field(DocValues = true)]` /
  `[NumericField(DocValues = true)]`). Writes a parallel column-store
  field at index time so sorting, grouping and faceting read from a
  packed forward index instead of uninverting the inverted index via
  `FieldCache`. Defaults to `false` on both attributes — opt in per
  field where sort/group performance matters. Silently ignored on
  collection (`IEnumerable<T>`) properties because Lucene.Net 4.8
  beta lacks `SortedNumericDocValuesField`.
- **Multi-targeting**: the library builds for `netstandard2.0` and
  `net8.0`; the test project multi-targets `net48;net8.0` so the
  netstandard build is exercised at runtime on classic .NET Framework
  as well as modern .NET.

#### Features
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
* Retrieve per-document term vectors (terms and frequencies) via `TermFreqVectorDocumentMapper<T>` for fields indexed with `TermVector = TermVectorMode.Yes`

## Available on NuGet Gallery

To install the [Lucene.Net.LinqX package](http://nuget.org/packages/Lucene.Net.LinqX),
run the following command in the [Package Manager Console](http://docs.nuget.org/docs/start-here/using-the-package-manager-console)

    PM> Install-Package Lucene.Net.LinqX

## Examples

1. Using [fluent syntax](source/Lucene.Net.Linq.Tests/Samples/FluentConfiguration.cs) to configure mappings
1. Using [attributes](source/Lucene.Net.Linq.Tests/Samples/AttributeConfiguration.cs) to configure mappings
1. Specifying [document keys](source/Lucene.Net.Linq.Tests/Samples/DocumentKeys.cs)

## Upgrading from Lucene.Net.Linq 3.x

`Lucene.Net.LinqX` 4.x is source-compatible for the most common usage
shape — annotated POCOs, `LuceneDataProvider`, `OpenSession`, LINQ
queries — but the underlying Lucene 3 → 4.8 jump forces a few changes.

**Index files are not compatible.** Lucene 4.x cannot read 3.x segments
at all. Plan to reindex from your source of truth, or run a one-time
upgrade through Lucene's `IndexUpgrader` 3 → 4 path before swapping
libraries. Run reindexing in a separate utility against an empty
directory; do not point the new library at an old index.

**Step-by-step:**

1. Replace the package reference:
   ```xml
   <PackageReference Include="Lucene.Net.LinqX" Version="4.8.0-beta00017" />
   ```
   The package id changed from `Lucene.Net.Linq` to `Lucene.Net.LinqX`
   to disambiguate this fork from the dormant original.

2. Retarget. The library is `netstandard2.0;net8.0`. .NET Framework 4.8
   consumers are supported via netstandard2.0; net40–net46 consumers are
   not.

3. Update your `LuceneVersion` constants. Replace
   `Lucene.Net.Util.Version.LUCENE_30` with
   `Lucene.Net.Util.LuceneVersion.LUCENE_48` everywhere. The type was
   renamed in Lucene.Net 4.8.

4. Fix any direct `Lucene.Net.*` usage. The bulk of the porting effort
   is in the underlying Lucene 3 → 4.8 namespace and API churn, not in
   this library:
   - `Lucene.Net.QueryParsers` → `Lucene.Net.QueryParsers.Classic`
   - `Lucene.Net.Analysis.Standard.StandardAnalyzer` now lives in
     `Lucene.Net.Analysis.Standard`; many tokenizers moved into
     `Lucene.Net.Analysis.Core`.
   - `Field` constructors now take a `FieldType` instead of separate
     `Store`/`Index`/`TermVector` enums. The library still accepts
     `StoreMode` / `IndexMode` / `TermVectorMode` on `[Field]`; only
     direct `new Field(...)` callers need to change.
   - `IndexWriter` now requires an `IndexWriterConfig`:
     ```csharp
     var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
     var writer = new IndexWriter(directory, config);
     ```
   - `IndexReader.Open` → `DirectoryReader.Open`. Most session code
     doesn't touch this directly.

5. Remove `[DocumentBoost]` attributes and any code that sets
   `Document.Boost`. Document-level boost is gone in Lucene 4.8. If
   you depended on it, fold the boost into a field boost on a
   discriminator field, or apply it via a `CustomScoreQuery`.

6. Drop any boost set on `[NumericField]`. It's silently ignored
   because numeric fields don't index norms in 4.8.

7. If you wrote a `MergePolicyBuilder`, change its signature from
   the old delegate to `Func<MergePolicy>` and return the policy
   instance directly. The library installs it on the
   `IndexWriterConfig` before constructing the writer.

8. If you sort by a value-type property without `[NumericField]`
   (e.g. a plain `int` or `DateTime`), be aware that 4.8 will sort
   it lexicographically by string form. Add `[NumericField]` for
   true numeric ordering, or accept the string sort if it happens
   to match (e.g. ISO-8601 `DateTime` strings).

9. Replace `Common.Logging` wiring with
   `Microsoft.Extensions.Logging`:
   ```csharp
   Lucene.Net.Linq.Util.Logging.LoggerFactory = myLoggerFactory;
   ```
   Defaults to `NullLoggerFactory` if you don't set one.

10. (Optional) Opt into DocValues on hot sort fields by adding
    `DocValues = true` to `[Field]` / `[NumericField]` attributes
    on properties you `OrderBy` heavily.

## Mapping objects to documents

Lucene.Net.LinqX maps plain CLR objects (POCOs) onto Lucene
`Document`s. You can describe the mapping in two ways: with attributes
on the type, or with a fluent code-first builder. Both produce the
same internal `IFieldMapper<T>` graph and are fully interchangeable.

### Attribute mapping

The simplest case: decorate properties with `[Field]` or
`[NumericField]`. Properties without an attribute are still mapped
with sensible defaults (analyzed, stored, OR query operator).

```csharp
[DocumentKey(FieldName = "Type", Value = "Article")]
public class Article
{
    [Field(Key = true)]
    public string Id { get; set; }

    [Field(IndexMode.Analyzed, DocValues = true)]
    public string Title { get; set; }

    [Field(IndexMode.NotAnalyzed)]
    public string Slug { get; set; }

    [NumericField]
    public int WordCount { get; set; }

    [NumericField(DocValues = true)]
    public DateTime PublishedAt { get; set; }

    [Field("body_text", Analyzer = typeof(EnglishAnalyzer))]
    public string Body { get; set; }

    public IList<string> Tags { get; set; }   // multi-valued, default mapping

    [QueryScore]
    public float Score { get; set; }          // populated at read time

    [IgnoreField]
    public string TransientUiState { get; set; }
}
```

#### `[Field]` options

| Option | Default | Notes |
| --- | --- | --- |
| `Field` (ctor) | property name | Backing Lucene field name. |
| `IndexMode` (ctor) | `Analyzed` | `Analyzed`, `NotAnalyzed`, `NotAnalyzedNoNorms`, `NoIndex`. |
| `Store` | `Yes` | Whether the original value is kept verbatim for read-back. |
| `Key = true` | `false` | Participates in the document primary key (used for replace/delete). Multiple properties can be marked. |
| `Boost` | `1.0f` | Index-time boost. |
| `Converter` | derived | Custom `TypeConverter` for non-string values. |
| `Format` | `yyyy-MM-ddTHH:mm:ss` for `DateTime` | Format string used by the default value-type converter. Ignored if `Converter` is set. |
| `CaseSensitive` | depends on converter | Disables `LowercaseExpandedTerms` in the query parser; also routes the field through `KeywordAnalyzer` instead of `CaseInsensitiveKeywordAnalyzer` when no explicit analyzer is set. |
| `DefaultParserOperator` | `OR` | `OR` or `AND` for parsed-string queries on this field. |
| `Analyzer` | `null` | Per-field analyzer type; must have a default ctor or one accepting `LuceneVersion`. Overridden by an analyzer passed to `LuceneDataProvider`. |
| `TermVector` | `No` | `No`, `Yes`, `WithPositions`, `WithOffsets`, `WithPositionsOffsets`. |
| `NativeSort` | `false` | When `true`, sort uses byte-wise string comparison instead of `IComparable.CompareTo`. Only meaningful when the converter's string output is alphanumerically sortable. |
| `DocValues` | `false` | Write a parallel `SortedDocValuesField` column. Opt in for fast sort/group/facet on hot fields. |

#### `[NumericField]` options

Trie-encoded numeric field. Use this for `int`, `long`, `float`,
`double`, `enum` (via underlying integral type), `DateTime` /
`DateTimeOffset` (via the built-in ticks converter), `bool` (via a
custom converter), or any other type your `TypeConverter` can map onto
one of the four primitive numeric types.

| Option | Default | Notes |
| --- | --- | --- |
| `Field` (ctor) | property name | Backing Lucene field name. |
| `Store` | `Yes` | |
| `Key` | `false` | |
| `Boost` | `1.0f` | **Silently dropped** — Lucene 4.8 numeric fields don't index norms. |
| `Converter` | built-in for `DateTime`/`DateTimeOffset` | `TypeConverter` mapping the property type to one of `int`/`long`/`float`/`double`. |
| `PrecisionStep` | `NumericUtils.PRECISION_STEP_DEFAULT` | Trie granularity. Smaller = faster range queries, larger index. |
| `DocValues` | `false` | Writes a parallel `NumericDocValuesField` / `SingleDocValuesField` / `DoubleDocValuesField` column. Opt in for fast sort. |

#### Other attributes

- **`[IgnoreField]`** — exclude a public property from mapping entirely.
- **`[QueryScore]`** — populate a `float` property with the document's
  relevance score on read. No options.
- **`[DocumentKey(FieldName, Value)]`** *(class-level, repeatable)* —
  pins a constant field/value on every document of the class so
  multiple types can share an index without cross-contamination. The
  framework also adds an automatic filter to LINQ queries on the type
  so they only return matching documents.

### Fluent (code-first) mapping

When you can't or don't want to put attributes on the type — DTOs from
another assembly, generated code, schemas built at runtime — use
`ClassMap<T>`:

```csharp
public class ArticleMap : ClassMap<Article>
{
    public ArticleMap() : base(LuceneVersion.LUCENE_48)
    {
        Key(a => a.Id);
        Property(a => a.Title).AnalyzedWith(new StandardAnalyzer(LuceneVersion.LUCENE_48));
        Property(a => a.Slug).NotAnalyzed();
        NumericField(a => a.WordCount);
        NumericField(a => a.PublishedAt).WithPrecisionStep(8);
        Property(a => a.Body).WithFieldName("body_text");
    }
}

var provider = new LuceneDataProvider(directory, LuceneVersion.LUCENE_48);
provider.RegisterCacheWarmingCallback<Article>(...);
using var session = provider.OpenSession(new ArticleMap());
```

The fluent and attribute paths are equivalent — you can mix them in a
single project, but not on the same type. The fluent API exposes one
method per option in the tables above.

### Custom converters

Anything Lucene can store is ultimately a string (text fields) or a
trie-coded primitive (numeric fields). For everything else, supply a
`TypeConverter`:

```csharp
public class VersionConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext c, Type t) => t == typeof(string);
    public override bool CanConvertTo(ITypeDescriptorContext c, Type t)   => t == typeof(string);
    public override object ConvertFrom(ITypeDescriptorContext c, CultureInfo i, object v) => Version.Parse((string)v);
    public override object ConvertTo(ITypeDescriptorContext c, CultureInfo i, object v, Type t) => v?.ToString();
}

public class Package
{
    [Field(Converter = typeof(VersionConverter))]
    public Version Version { get; set; }
}
```

For `[NumericField]`, the converter must be able to convert the
property type to one of `long`, `int`, `double`, or `float`. The
built-in `DateTimeToTicksConverter` and `DateTimeOffsetToTicksConverter`
are wired up automatically when you mark a `DateTime` /
`DateTimeOffset` property as `[NumericField]`.

### Multi-valued fields

Any property whose type implements `IEnumerable<T>` is treated as a
multi-valued field automatically. The element type drives the
underlying mapper, so `IList<string>` works with `[Field]` and
`IList<int>` with `[NumericField]`. Note that `DocValues=true` is
silently downgraded for collections in this Lucene.Net 4.8 beta —
`SortedNumericDocValuesField` isn't available, and
`SortedSetDocValuesField` doesn't fit single-value LINQ ordering.

### Document keys

Two related concepts share the name "key":

- **`[Field(Key = true)]`** on one or more properties marks a *primary
  key* — a unique identifier within a document type. Adding a document
  whose key collides with an existing one replaces the old document
  in a single atomic transaction.

- **`[DocumentKey(FieldName, Value)]`** on the *class* declares a fixed
  discriminator so multiple unrelated POCO types can share one index.
  The library appends an automatic filter on every query so that
  `provider.AsQueryable<Article>()` only sees article documents.

You can disable the discriminator filter for performance if you keep
each entity type in its own index:

```csharp
provider.Settings.EnableMultipleEntities = false;
```

## Query semantics

Once you have a session, `provider.AsQueryable<T>()` returns an
`IQueryable<T>` that translates standard LINQ operators into Lucene
queries. Most of LINQ-to-objects works; the parts that don't, throw
at translation time with a clear message.

### Supported operators

| LINQ | Lucene equivalent |
| --- | --- |
| `Where(d => d.Field == value)` | `TermQuery` |
| `Where(d => d.Field != value)` | Boolean `MUST_NOT` |
| `Where(d => d.Field.StartsWith("foo"))` | `PrefixQuery` |
| `Where(d => d.Field.EndsWith("foo"))` | `WildcardQuery` (`*foo`) |
| `Where(d => d.Field.Contains("foo"))` | `WildcardQuery` (`*foo*`) |
| `Where(d => d.Numeric > 5)` etc. | `NumericRangeQuery` |
| `Where(d => d.Field == null)` | Negated existence query |
| `&&`, `\|\|`, `!` | Boolean `MUST` / `SHOULD` / `MUST_NOT` |
| `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending` | Multi-field `Sort` |
| `Skip(n).Take(m)` | `IndexSearcher.Search(query, n + m)` window |
| `First` / `FirstOrDefault` / `Single` / `SingleOrDefault` | `Take(1)` |
| `Any()` / `Any(predicate)` | `TotalHits > 0` |
| `Count()` / `LongCount()` | `TotalHits` |
| `Min` / `Max` | `Sort` ascending/descending + `Take(1)` |
| `Select(d => new { ... })` | Document projection (read only the fields you reference) |

### Parsed string queries

For the cases where you need raw Lucene query syntax — wildcards,
fuzzy search, boosting, range, fielded queries — use the
`WhereParseQuery` extension:

```csharp
using Lucene.Net.Linq;

var results = documents
    .Where(d => d.Body.Matches("foo* AND -bar"))      // shorthand for Body field
    .ToList();

var results2 = documents
    .WhereParseQuery("title:foo* AND year:[2020 TO 2024]")
    .ToList();
```

`WhereParseQuery` runs the input through Lucene's classic
`QueryParser` against the provider's analyzer; `Matches` does the
same but scoped to a single field.

### Score and boost

```csharp
// Order by relevance — highest score first.
var top = documents
    .Where(d => d.Body.Matches("lucene"))
    .OrderByDescending(d => d.Score())
    .Take(10);

// Per-query boost. Boost expressions can use document fields.
var boosted = documents
    .Where(d => d.Body.Matches("lucene"))
    .Boost(d => d.WordCount / 100.0f);
```

`Score()` is an extension on the document type, available inside
queries even when the POCO has no `[QueryScore]` property; mark a
property `[QueryScore]` to also have the score materialized onto each
returned object.

### Pagination

`Skip(n).Take(m)` translates to a Lucene window read. Lucene scores
the entire result set up to `n + m` and returns the requested slice,
so very large `Skip` values are slower than a key-set / cursor
approach — prefer cursoring on a sortable key field for deep paging.

### Sorting and DocValues

`OrderBy` / `OrderByDescending` translate into Lucene `SortField`s.
The selection rules:

- `[NumericField]` properties → typed numeric `SortField` (correct
  numeric ordering).
- `[Field]` reference-type properties with a `TypeConverter`
  implementing `IComparable` / `IComparable<T>` → custom comparator
  that calls `CompareTo`, reading bytes from `FieldCache.GetTerms`.
- Plain `[Field]` strings → `SortFieldType.STRING`.
- Anything with `DocValues = true` → typed `SortField` reading from
  the column store, much faster on first touch.

For hot sort fields, opt into DocValues:

```csharp
[Field(DocValues = true)]
public string Title { get; set; }

[NumericField(DocValues = true)]
public DateTime PublishedAt { get; set; }
```

Without DocValues, the first sort on a field per segment uninverts
the inverted index via `FieldCache` — correct, but pays an O(n) cost
on first touch and holds the result in memory. With DocValues, the
sort reads directly from a packed column.

## Sessions, transactions, and the data provider

```csharp
var directory = FSDirectory.Open(new DirectoryInfo("./index"));
var provider  = new LuceneDataProvider(directory, LuceneVersion.LUCENE_48);

using (var session = provider.OpenSession<Article>())
{
    session.Add(new Article { Id = "1", Title = "Hello" });

    var hits = session.Query()
        .Where(a => a.Title.Matches("hello"))
        .ToList();

    foreach (var doc in hits) doc.Title = doc.Title.ToUpperInvariant();

    session.Commit();    // single atomic flush; rollback on exception
}
```

Key points:

- A session implements the **unit of work** pattern. It tracks every
  document you add, every document you read (so dirty-checking can
  detect mutations), and every key you delete. `Commit()` flushes
  them in a single atomic transaction; `Dispose()` without `Commit()`
  rolls them back.
- `Add` on a document whose `[Field(Key = true)]` matches an existing
  key **replaces** the existing document, in the same transaction.
- `Delete(key)` and `DeleteAll()` are also queued and flushed at
  commit time.
- `provider.AsQueryable<T>()` opens a read-only view that doesn't need
  a session — convenient for read-only OData / API endpoints.

### Cache-warming callbacks

Lucene reopens its `IndexSearcher` periodically as the index changes.
You can register queries to run on the new searcher *before* it
becomes visible, so the first user query doesn't pay the warm-up
cost:

```csharp
provider.RegisterCacheWarmingCallback<Article>(queryable =>
{
    queryable.OrderByDescending(a => a.PublishedAt).Take(50).ToList();
});
```

### Logging

Wire up a logger factory once, at startup:

```csharp
Lucene.Net.Linq.Util.Logging.LoggerFactory = LoggerFactory.Create(b =>
    b.AddConsole().SetMinimumLevel(LogLevel.Debug));
```

The library logs query translation steps, cache reload events, and
session commit summaries. Defaults to `NullLoggerFactory` if you
don't set one.

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

#### 3.x => 4.x Changes

A handful of subsystems shifted because the underlying Lucene API was
removed or substantially reworked between 3.0.3 and 4.8:

- **Document-level boost** was removed in Lucene 4.8 (only field-level
  boost remains). The old `[DocumentBoost]` attribute and the document
  boost read/write hooks have been deleted. Equivalent functionality
  must be expressed as field-level boost or as a custom scoring query.
- **Numeric-field boost** is dropped — Lucene 4.8 numeric fields don't
  index norms, so per-field boost on `Int32Field`/`Int64Field`/etc. has
  no effect. The `Boost` property on `[NumericField]` is accepted for
  source compatibility but silently ignored at index time.
- **Converter-based custom sort** is back, on a new code path. Properties
  whose type implements `IComparable` / `IComparable<T>` and has a
  `TypeConverter` (e.g. `System.Version`) sort correctly via
  `GenericConvertableFieldComparatorSource` /
  `NonGenericConvertableFieldComparatorSource`, which read field bytes
  through `FieldCache.GetTerms`. Value-type properties (`int`, `bool`,
  `DateTime`, nullables) cannot use this path because Lucene 4.8's
  `FieldComparer<T>` constrains `T : class` — mark them `[NumericField]`
  for true numeric ordering, otherwise they fall back to string sort.
- **`MergePolicyBuilder`** is now a `Func<MergePolicy>` returning the
  policy to install. Lucene 4.8 requires the merge policy to be set on
  `IndexWriterConfig` *before* the writer is constructed, so the old
  delegate signature that received the live `IndexWriter` no longer
  fits.

