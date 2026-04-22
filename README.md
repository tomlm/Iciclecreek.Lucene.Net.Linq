[![Build and Test](https://github.com/tomlm/Iciclecreek.Lucene.Net.Linq/actions/workflows/BuildAndRunTests.yml/badge.svg)](https://github.com/tomlm/Iciclecreek.Lucene.Net.Linq/actions/workflows/BuildAndRunTests.yml)
[![NuGet](https://img.shields.io/nuget/v/Iciclecreek.Lucene.Net.Linq.svg)](https://www.nuget.org/packages/Iciclecreek.Lucene.Net.Linq)

## LINQ to Lucene Modernized for Lucene.Net 4.8 and .NET 8+

Iciclecreek.Lucene.Net.Linq is a .NET library that enables LINQ queries to run natively on a Lucene.Net index.

## Installation

To install the [Iciclecreek.Lucene.Net.Linq package](http://nuget.org/packages/Iciclecreek.Lucene.Net.Linq),
run the following command in the [Package Manager Console](http://docs.nuget.org/docs/start-here/using-the-package-manager-console)

    PM> Install-Package Iciclecreek.Lucene.Net.Linq

## Port

This branch ports the Lucene.Net.Linq library from the original `Lucene.Net.Linq 3.0.3` /
`net40` baseline onto `Lucene.Net 4.8.0-beta00017` and SDK-style projects
multi-targeting `netstandard2.0;net8.0;net10.0`. Highlights:

- **Lucene.Net 4.8.0-beta00017** for the index, query, analysis, and
  query-parser packages.
- **Remotion.Linq 2.2.0** for the LINQ provider plumbing.
- **Microsoft.Extensions.Logging.Abstractions** replaces `Common.Logging`.
  Wire your logger via `Lucene.Net.Linq.Util.Logging.LoggerFactory =
  myFactory;` (defaults to `NullLoggerFactory`).
- **Tests** moved from RhinoMocks/NUnit 2 to **NSubstitute / NUnit 4**.

### New 4.x features

- **Vector similarity search** via `.Similar()` with automatic embedding at index and query time
- **Multi-targeting**: the library builds for `netstandard2.0`, `net8.0`,
  and `net10.0`; 
- **Polymorphic select** - searching for a base type always properly instantiated object types of the original type.
- **JOIN** LINQ join support utilizes search index to query across document types 
- **DocValues opt-in** (`[Field(DocValues = true)]` /
  `[NumericField(DocValues = true)]`). Writes a parallel column-store
  field at index time so sorting, grouping and faceting read from a
  packed forward index instead of uninverting the inverted index via
  `FieldCache`. Defaults to `false` on both attributes — opt in per
  field where sort/group performance matters. Silently ignored on
  collection (`IEnumerable<T>`) properties because Lucene.Net 4.8
  beta lacks `SortedNumericDocValuesField`.

### Features
* **Vector similarity search** via `.Similar()` with automatic embedding at index and query time
* Automatically converts PONOs to Documents and back
* Add, delete and update documents in atomic transaction
* Unit of Work pattern automatically tracks and flushes updated documents
* Update/replace documents with \[Field(Key=true)\] to prevent duplicates
* Term queries
* Prefix queries
* Range queries and numeric range queries
* Complex boolean queries
* **LINQ joins** across document types with automatic semi-join pushdown via `TermsFilter`
* **`collection.Contains(field)`** — the LINQ "IN" pattern translates to an efficient `TermsFilter`
* Native pagination using Skip and Take
* Support storing and querying NumericField
* Polymorphic type hierarchies: store subtypes, query by base type, get back real types
* Automatically convert complex types for storing, querying and sorting
* Custom boost functions using IQueryable<T>.Boost() extension method
* Sort by standard string, NumericField or any type that implements IComparable
* Sort by item.Score() extension method to sort by relevance
* Specify custom format for DateTime stored as strings
* Register cache-warming queries to be executed when IndexSearcher is being reloaded
* Retrieve per-document term vectors (terms and frequencies) via `TermFreqVectorDocumentMapper<T>` for fields indexed with `TermVector = TermVectorMode.Yes`

## Examples

1. Using [fluent syntax](source/Lucene.Net.Linq.Tests/Samples/FluentConfiguration.cs) to configure mappings
1. Using [attributes](source/Lucene.Net.Linq.Tests/Samples/AttributeConfiguration.cs) to configure mappings
1. Specifying [document keys](source/Lucene.Net.Linq.Tests/Samples/DocumentKeys.cs)

## Mapping objects <=> documents

Iciclecreek.Lucene.Net.Linq maps plain CLR objects (POCOs) onto Lucene
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
  pins a constant field/value on every document of the class. Useful
  for adding fixed metadata fields. 

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

**`[Field(Key = true)]`** on one or more properties marks a *primary
key* — a unique identifier within a document type. Adding a document
whose key collides with an existing one replaces the old document
in a single atomic transaction.

### Polymorphic type hierarchies

The library automatically supports inheritance hierarchies.
This means if you have `class Dog : Animal` and `class Cat : Animal`:

```csharp
// Store mixed subtypes through a base-type session
using (var session = provider.OpenSession<Animal>())
{
    session.Add(new Dog   { Id = "1", Name = "Rex",      Breed = "Shepherd" });
    session.Add(new Cat   { Id = "2", Name = "Whiskers", Indoor = true });
    session.Add(new GuideDog { Id = "3", Name = "Buddy", Breed = "Lab", Handler = "John" });
    session.Commit();
}

// Query by base type — returns all three, each as its real type
var animals = provider.AsQueryable<Animal>().ToList();
// animals[0] is Dog, animals[1] is Cat, animals[2] is GuideDog

// Query by middle type — returns Dog + GuideDog, not Cat
var dogs = provider.AsQueryable<Dog>().ToList();

// Query by leaf type — returns only GuideDog
var guides = provider.AsQueryable<GuideDog>().ToList();
```

Key behaviors:

- **Subtype-specific fields are fully indexed and hydrated.** A `Dog`
  stored via `OpenSession<Animal>()` retains its `Breed` property. When
  read back, `Breed` is populated even when querying as `Animal`.
- **Dirty tracking works across the hierarchy.** If you query a `Dog`
  through `ISession<Animal>` and change `Dog.Breed`, the session
  detects the modification and flushes it on commit.
- **Same key = same entity.** If a `Dog` and a `Cat` share the same
  `[Field(Key = true)]` value, the last write wins — they are treated
  as the same document.
- **Subtypes must have a parameterless constructor.** The library uses
  `Activator.CreateInstance` to instantiate the actual runtime type
  when reading polymorphic documents.

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
| `Where(d => d.Field.Similar("text"))` | `VectorQuery` (KNN or cosine similarity) |
| `Select(d => new { ... })` | Document projection (read only the fields you reference) |

### Collection Contains ("IN" queries)

The LINQ `collection.Contains(field)` pattern translates to an efficient
`TermsFilter` -- a single-pass filter that matches documents whose field
value appears in the collection. This is the Lucene equivalent of SQL's
`IN` operator.

```csharp
var allowedCategories = new[] { "tech", "science", "health" };

var articles = provider.AsQueryable<Article>()
    .Where(a => allowedCategories.Contains(a.Category))
    .ToList();
```

This produces `ConstantScoreQuery(TermsFilter([Category:tech, Category:science, Category:health]))` -- much more efficient than chaining
`||` equality checks, especially for large collections. Works with
arrays, lists, and any `IEnumerable<T>`, including captured variables.

You can combine it with other predicates:

```csharp
var results = provider.AsQueryable<Article>()
    .Where(a => allowedCategories.Contains(a.Category) && a.WordCount > 500)
    .ToList();
```

An empty collection matches nothing (returns zero results).

### Joins

LINQ `join` syntax works across document types. The library materializes
both sides via separate Lucene searches and joins them in memory. A
semi-join optimization uses `TermsFilter` to push the outer key values
into the inner query, so only matching inner documents are fetched.

```csharp
var articles = provider.AsQueryable<Article>();
var authors  = provider.AsQueryable<Author>();

// Single join
var results = (
    from article in articles
    join author in authors on article.AuthorId equals author.Username
    select new { article.Title, author.DisplayName }
).ToList();
```

Multiple joins chain naturally:

```csharp
var categories = provider.AsQueryable<Category>();

var results = (
    from article in articles
    join author in authors on article.AuthorId equals author.Username
    join category in categories on article.CategoryId equals category.Id
    select new { article.Title, author.DisplayName, category.Label }
).ToList();
```

Where clauses on the outer side are pushed into Lucene before the join:

```csharp
var results = (
    from article in articles.Where(a => a.Title.Contains("lucene"))
    join author in authors on article.AuthorId equals author.Username
    select new { article.Title, author.DisplayName }
).ToList();
```

Method syntax also works:

```csharp
var results = provider.AsQueryable<Article>()
    .Join(
        provider.AsQueryable<Author>(),
        article => article.AuthorId,
        author => author.Username,
        (article, author) => new { article.Title, author.DisplayName })
    .ToList();
```

**How it works under the hood:**

1. The outer query executes as a normal Lucene search.
2. Distinct join key values are extracted from the outer results.
3. A `TermsFilter` is built from those keys and pushed into the inner
   query -- only inner documents matching an outer key are fetched.
4. Both materialized sides are joined in memory via `Enumerable.Join`.

This means joins are efficient when the outer result set is selective
(few distinct keys), but will materialize both sides for broad queries.
Lucene has no relational join engine -- this is a convenience that
avoids manual materialization and in-memory joining in user code.

### Parsed string queries

For the cases where you need raw Lucene query syntax — wildcards,
fuzzy search, boosting, range, fielded queries — use the
`WhereParseQuery` extension:

```csharp
using Lucene.Net.Linq;

var results = documents
    .WhereParseQuery("title:foo* AND year:[2020 TO 2024]")
    .ToList();
```

`WhereParseQuery` runs the input through Lucene's classic
`QueryParser` against the provider's analyzer. To pass a
pre-built `Lucene.Net.Search.Query` instead, use the
`Where(IQueryable<T>, Query)` overload.

### Score and boost

```csharp
// Order by relevance — highest score first.
var top = documents
    .WhereParseQuery("body:lucene")
    .OrderByDescending(d => d.Score())
    .Take(10);

// Per-query boost. Boost expressions can use document fields.
var boosted = documents
    .WhereParseQuery("body:lucene")
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
        .Where(a => a.Title == "hello")
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

## Vector Similarity Search

The library supports vector similarity search. String properties can
opt in via `[VectorField]` or the fluent `.AsVectorField()` API. Embeddings
are automatically computed at index time and ranked by similarity at
query time using `.Similar()`.

### Configuring an embedding generator

Vector search requires an `IEmbeddingGenerator<string, Embedding<float>>`
(from `Microsoft.Extensions.AI`). Any implementation works --
OpenAI, Azure OpenAI, Ollama, or a local model. For local / offline
scenarios, [ElBruno.LocalEmbeddings](https://nuget.org/packages/ElBruno.LocalEmbeddings)
is a good choice -- under 20 MB and works great offline:

```csharp
using ElBruno.LocalEmbeddings;
using ElBruno.LocalEmbeddings.Options;

var generator = new LocalEmbeddingGenerator(new LocalEmbeddingsOptions
{
    ModelName = "SmartComponents/bge-micro-v2",
    PreferQuantized = true
});

var provider = new LuceneDataProvider(directory, LuceneVersion.LUCENE_48);
provider.Settings.EmbeddingGenerator = generator;
```

### Attribute mapping

Add `[VectorField]` alongside `[Field]` on any string property:

```csharp
public class Article
{
    [Field(Key = true)]
    public string Id { get; set; }

    [Field, VectorField]
    public string Title { get; set; }

    [Field]
    public string Category { get; set; }
}
```

### Fluent mapping

```csharp
public class ArticleMap : ClassMap<Article>
{
    public ArticleMap() : base(LuceneVersion.LUCENE_48)
    {
        Key(a => a.Id);
        Property(a => a.Title).AsVectorField();
    }
}
```

### Querying with `.Similar()`

Use `.Similar()` inside a `Where` clause to rank results by
similarity. Use `.Take()` to limit how many come back:

```csharp
var results = provider.AsQueryable<Article>()
    .Where(a => a.Title.Similar("a cute cat napping"))
    .Take(5)
    .ToList();
```

`.Similar()` composes naturally with other predicates. Filters are
applied first, then matching documents are ranked by similarity:

```csharp
// Only animals, ranked by similarity
var results = provider.AsQueryable<Article>()
    .Where(a => a.Title.Similar("furry animals") && a.Category == "animals")
    .Take(3)
    .ToList();
```



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

## Upgrading from Lucene.Net.Linq 3.x

`Iciclecreek.Lucene.Net.Linq` 4.x is source-compatible for the most common usage
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
   <PackageReference Include="Iciclecreek.Lucene.Net.Linq" Version="4.8.0-beta00017" />
   ```

   The package id changed from `Lucene.Net.Linq` to `Iciclecreek.Lucene.Net.Linq`
   to disambiguate this fork from the dormant original.

2. Retarget. The library is `netstandard2.0;net8.0;net10.0`. .NET Framework 4.6.1+
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
