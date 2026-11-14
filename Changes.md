# 4.8.9-beta00018: filters before vectors

Every other condition of a query now narrows the documents before a vector is compared. See the README's *Filters run before vectors* section.

**Behaviour changes (bug fixes)**
- `Similar` in a different `Where` than the other conditions (`query.Where(a).Where(x => x.Text.Similar(q))`, either order, or a `Where(Query)`) now ranks the documents the conditions select. Before, the K nearest documents of the whole index were chosen first and then filtered, which often left fewer rows than asked for, or none.
- `Similar` alone now chooses its K nearest among documents of the queried type. Before, documents of other types in the same index with the same vector field took places among the K and were then dropped.
- K now includes `Skip`: `Where(similar).Skip(5).Take(5)` returns the sixth to tenth nearest. Before, it returned nothing.
- `x.Similar(q) && x.Topic != "a"` (only exclusions next to `Similar`) now returns the documents that are not excluded. Before, it returned nothing.
- `a || x.Similar(q)` now matches the documents of `a` plus the K nearest. Before, it was treated as `a && x.Similar(q)`.
- Two `Similar` calls in one query now work: the selected documents are scored by each and the scores are added. Before, the query threw `NotSupportedException`.
- `!x.Similar(q)` throws a `NotSupportedException` that says so.
- A join whose returned side has a `Similar` clause is now pushed into Lucene, and narrows the documents before the nearest are chosen. Before, such a join ran in memory over the K nearest of the outer side. With pushdown disabled it still does, so for these queries the two settings can return different rows.

**New**
- `LuceneDataProviderSettings.VectorExactSearchLimit` (default 10,000): when `Similar` is the only condition and the search filter leaves no more than this many documents, each is compared with the query vector and no vector (HNSW) index is built.

**Dependencies**
- `Iciclecreek.Lucene.Net.Vector` 2.1.0. On .NET 10 the vector (HNSW) index is now kept per index segment and built in the background, so the first `Similar` query after a write no longer waits for the whole index to be rebuilt.

# 4.8.8-beta00018: joins inside Lucene

LINQ joins are pushed into Lucene with `Lucene.Net.Join` 4.8.0-beta00018 (`JoinUtil.CreateJoinQuery`) where the query's shape allows it. See the README's *Joins* section.

**Behaviour changes for all queries, to match LINQ**
- A later `OrderBy` is now the primary sort and an earlier one only breaks ties. Before, the first `OrderBy` was the primary sort, so `query.OrderBy(a).OrderBy(b)` sorted by `a` then `b`. `ThenBy` and `orderby a, b` are unchanged. This also applies when an ordered sub-query is ordered again.
- A `Where` or `OrderBy` after `Take`/`Skip` (`query.Take(10).Where(...)`) is now applied to the page. Before, it was folded into the same Lucene query and ran ahead of the paging. It is evaluated on the loaded rows, so string comparisons there are exact, and strings are ordered by ordinal, as Lucene orders them. `ids.Contains(d.Id)` over an in-memory collection works there. Whatever follows is applied to those rows too (`query.Take(10).Where(...).Take(3).Select(...)` threw).
- `Sum`, `Average`, `Distinct`, `Contains(item)`, `All` and `Aggregate` now work, on the loaded rows, also after a `Select` and with a clause after them (`query.Select(...).Distinct().OrderBy(...)`). Before, they threw `NullReferenceException` or (for `Distinct`) returned duplicates.
- `DefaultIfEmpty` is now applied. Before, it was ignored.
- `Min` and `Max` after `Take`/`Skip` now answer for the page, with or without a selector. Before, they ignored the paging.
- `First()` / `FirstOrDefault()` after paging that leaves no rows (`query.Take(0)`, `query.Take(2).Skip(3)`) now finds no row, for queries with or without a join. Before, it returned the first match.

**Behaviour changes (bug fixes) to existing joins**
- A `where` or `orderby` after a join that refers to the joined source now works when the join runs in memory, as does a `Where` or `Count(predicate)` after paging a join. A `where` on one joined source runs in that source's Lucene query and an `orderby` sorts strings as Lucene sorts the field, so the result is the same whether or not the join is pushed down. A `where` that compares two sources is applied to the joined rows as C# values.
- `Skip`, `Take`, `First`/`Single` and other result operators now apply to the joined rows. Before, they applied to the outer query ahead of the join, so `Take(20)` could return fewer or more than 20 rows.
- `Count()`, `Any()` and other scalars over a join are computed from the joined rows. Before, they ignored the join and answered for the outer side alone.
- Join keys are looked up by the mapped field name and the term the value is indexed as. Before, the property name and `ToString()` were used, so a renamed field, a numeric or converted key, or a mixed-case key on a default (lowercased) field found no matches.
- String keys are compared as their indexed terms, so a join follows the key fields' configuration: case-insensitive on default-mapped fields, exact on `CaseSensitive` / `NotAnalyzed` fields.
- An inner sequence with its own operators (`join a in authors.Where(...)`) now works. Before, it threw `ArgumentException: must be reducible node`.
- `Count()` after `Take` now honours the `Take`, for queries with or without a join. Before, `query.Take(2).Count()` returned the total number of matches. A `Skip` past the rows that were taken counts 0 (`query.Take(2).Skip(3).Count()` was negative).
- `Last()` / `LastOrDefault()` after a `Skip` that passes the last match now finds no row, for queries with or without a join. Before, `query.Skip(n).Last()` returned the last match however large `n` was.

**New**
- A join whose result uses only one side runs inside Lucene: the unused side becomes a filter, its documents are never loaded, and sorting, paging, `Count` and `Any` run in the index. Either side can be the one returned. In a chained join each join is decided separately.
- Joins that don't qualify are joined in memory as before, and the reason is logged at debug level. The README lists the conditions.
- `LuceneDataProviderSettings.EnableJoinPushdown` (default `true`) turns this off.
- A join whose result uses both sides is still joined in memory, but the outer side is first narrowed with a join filter, so only outer documents with a matching inner document are loaded and a filter on the inner side cuts the outer load. No unique key is needed for this.
- Benchmarks (`JoinPushdownBenchmark`, net8.0 Release, 50,000 × 5,000 documents, median of 10 runs):
  - One side selected, filter on the inner side, `OrderBy`, `Take(20)`: 7 ms pushed down against 376 ms in memory.
  - Both sides selected, inner filter keeps 2%: 37 ms narrowed against 431 ms in memory.
  - Both sides selected, no inner filter (everything joins): 414 ms narrowed against 394 ms in memory.
- New dependency: `Lucene.Net.Join` (already present transitively through `Lucene.Net.Facet`).

# Unreleased: facet counting

Adds facet counting via `Lucene.Net.Facet` 4.8.0-beta00018 (sorted-set doc-values facets, no taxonomy index). See the README's *Facets* section.

- **Mapping:** new `Facet` option on `[Field]` / `[NumericField]` (`BaseFieldAttribute.Facet`) and `PropertyMap<T>.Facet()` in the fluent mapping. Works on collection properties. The existing `DocValues` downgrade on collections is unchanged.
- **Writing:** `DocumentMapperBase<T>.MapFieldsToDocument` writes facet values to the shared `$facets` field in the caller's document. It builds them with `FacetsConfig.Build` on a scratch document, then appends the result, replacing any earlier facet fields. Facet field mappers are found at write time, so mappers added with `AddField` / `AddKeyField` are included.
- **Range counts:**
  - Single-valued numeric, `DateTime` and `DateTimeOffset` properties get a hidden `{Field}$range` doc-values column. The property's own field is unchanged.
  - `[NumericField(Facet = true)]` stores the number it indexes there, as a `long` or a `double`. `Facet` does not turn on `DocValues`.
  - Other date properties store UTC ticks there.
  - The hidden column has no indexed terms, so documents written before `Facet` was set are not counted until re-indexed.
- **Query:** new `LuceneMethods.Facets(out FacetResults<T>, f => f.Count(...).Ranges(...))` extension clause (same pattern as `CaptureStatistics`).
  - Counts are collected in the same search pass as the hits (`FacetsCollector.Search`).
  - They use the vector-resolved query.
  - They are filled for list and scalar results.
- **Caching:** the `SortedSetDocValuesReaderState` is built lazily per searcher and dropped when the searcher reloads. An index with no facet data yields empty counts.
- **New public types:** `FacetValue`, `FacetRange` (with `Daily` / `Monthly` / `Yearly`), `FacetRequest<T>`, `FacetResults<T>`, `IFacetFieldMappingInfo`, `IFacetFieldMapper<T>`.
- **New constructor overloads:** `ReflectionFieldMapper<T>` and `NumericReflectionFieldMapper<T>` gain overloads with a `facet` argument. Existing constructors are unchanged.
- **New dependency:** `Lucene.Net.Facet`, which brings `Lucene.Net.Join` transitively.
- **Not included:** drill-sideways and hierarchical facets.

# `net8-upgrade` → `master` merge summary

Full port of `Lucene.Net.Linq` from the `Lucene.Net 3.0.3` / `net40` baseline to `Lucene.Net 4.8.0-beta00017` on SDK-style projects multi-targeting `netstandard2.0;net8.0`. **170 files changed, +9 689 / −9 973**, **426 / 427 tests passing** (1 pre-existing skip), **0 build warnings** on both target frameworks.

## Headline changes

| Area | Before | After |
| --- | --- | --- |
| Lucene engine | `Lucene.Net 3.0.3` | `Lucene.Net 4.8.0-beta00017` (+ `Analysis.Common`, `QueryParser`) |
| LINQ provider | `Remotion.Linq 1.13` | `Remotion.Linq 2.2.0` |
| Logging | `Common.Logging` | `Microsoft.Extensions.Logging.Abstractions` |
| Mocking / test framework | RhinoMocks + NUnit 2 | NSubstitute + NUnit 4 |
| Project format | Old-style `.csproj` + `packages.config` | SDK-style, `PackageReference` only |
| Library TFM | `net40` | `netstandard2.0;net8.0` |
| Test TFM | `net40` | `net48;net8.0` (validates the netstandard2.0 build at runtime on classic .NET Framework as well as modern .NET) |
| Package id | `Lucene.Net.Linq` | `Lucene.Net.LinqX` (fork distinction) |
| CI | AppVeyor | GitHub Actions |

## Stage-by-stage commit history

| Stage | Commit | What happened |
| --- | --- | --- |
| 1 | `0996f42` | Convert all projects to SDK-style; switch to `PackageReference`; multi-target `netstandard2.0;net8.0`. |
| 2 | `d742c99` | Migrate `Remotion.Linq` 1.13 → 2.2 (interface and namespace changes throughout the visitor / query-model layer). |
| 3 | `f701da3` | Replace `Common.Logging` with `Microsoft.Extensions.Logging`; introduce `Lucene.Net.Linq.Util.Logging.LoggerFactory` extension point (defaults to `NullLoggerFactory`). |
| 4 | `851769f` | Port the library proper to `Lucene.Net 4.8.0-beta00017`. The bulk of the diff: namespace moves (`QueryParsers.Classic`, `Analysis.Core`), `Version` → `LuceneVersion`, `Field` ctor reshape with `FieldType`, `IndexWriter`/`IndexWriterConfig`, `IndexReader.Open` → `DirectoryReader.Open`, `BytesRef` everywhere, `AtomicReader`/`AtomicReaderContext` for collectors. |
| 5 | `5ca5b03` | Port the test project: NUnit 2 → 4, RhinoMocks → NSubstitute, refit fixtures to the new Lucene API. |
| 6 | `62e9542` | First green run: 416 passing, 0 failing, 8 `[Ignore]`'d for subsystems still pending. |
| 7 | `1012080` | Cleanup: drop legacy build files, version bump, README. |
| 8 | `1885dc8` | De-shim — remove the Stage 2 / 4 compatibility layers now that all callers are migrated. |
| 9 | `6ce85cf` | Clear all build warnings (library + tests, both TFMs). |
| 10 | `3442daf` | Remove document-level boost (gone in Lucene 4.8) and the numeric-field boost stub (numeric fields don't index norms in 4.8). `[DocumentBoost]` deleted. |
| 11 | `65e2905` | `MergePolicyBuilder` becomes a `Func<MergePolicy>` returning the policy to install on `IndexWriterConfig` *before* the writer is constructed. Old delegate that received the live `IndexWriter` no longer fits the 4.8 lifecycle. |
| 12 | `e0eb892` | Multi-target tests to `net48;net8.0` so the netstandard2.0 build is exercised at runtime on both runtimes. |
| 13 | `5a870e9` | Drop the `Tree` infix from visitor file/class/folder names — Remotion.Linq 2.x already operates on the expression tree, the prefix was historical noise. |
| 14 | `0b6c76b` | Re-implement term-vector retrieval against `IndexReader.GetTermVectors(docId)` → `Fields.GetTerms(name)` → enumerate `Terms` for `(term, totalTermFreq)`. `TermFreqVectorDocumentMapper<T>` and the `TermVectorTests` integration test are live. |
| 15 | `f984675` | Re-implement converter-based custom sort. New `GenericConvertableFieldComparatorSource` and `NonGenericConvertableFieldComparatorSource` read field bytes via `FieldCache.GetTerms` and call `IComparable.CompareTo` for reference-type properties with a `TypeConverter` (e.g. `System.Version`). Value-type properties remain on the string-sort fallback because Lucene 4.8's `FieldComparer<T>` constrains `T : class`. |
| 16 | `5daddce` | DocValues opt-in: new `DocValues` property on `BaseFieldAttribute`. When `true`, the mapper writes a parallel column-store field at index time (`SortedDocValuesField` for strings; `NumericDocValuesField` / `SingleDocValuesField` / `DoubleDocValuesField` for numerics) and `CreateSortField` short-circuits to a typed `SortField` reading from the column. Defaults to `false` on both `[Field]` and `[NumericField]` — opt in per field. Silently downgraded for `IEnumerable<T>` properties (the 4.8 beta lacks `SortedNumericDocValuesField`). New `DocValuesMappingTests` (6 tests) and an `OrderBy_Int_MultiDigit` integration test prove typed numeric ordering. |
| — | `621bb27`, `bb774c8`, `3dfc4f1` | README rewrites: project title, Lucene 4.8 API notes. |
| — | `e965572` | Add GitHub Actions CI workflow. |

## Public API changes (consumer-facing)

### Removed
- `[DocumentBoost]` attribute and the document-boost read/write hooks on `LuceneDataProvider` / mappers (Lucene 4.8 dropped per-document boost entirely).
- The old `MergePolicyBuilder` delegate type that received an `IndexWriter`.
- `Version.LUCENE_30` and the rest of the pre-4.8 enum members (Lucene rename to `LuceneVersion`).
- `Common.Logging` integration points.

### Renamed / changed shape
- `MergePolicyBuilder` → `Func<MergePolicy>`. One-line migration at the call site.
- `Lucene.Net.Linq.Util.Logging.LogManager` → `Lucene.Net.Linq.Util.Logging.LoggerFactory` (assignable property; settable to any `Microsoft.Extensions.Logging.ILoggerFactory`).
- Package id `Lucene.Net.Linq` → `Lucene.Net.LinqX`.
- Internal visitor classes lost the `Tree` infix (only matters if you reference internals).

### Added
- `BaseFieldAttribute.DocValues : bool` (default `false`).
- `TermFreqVectorDocumentMapper<T>` and `DocumentTermVector` are now real, working types backed by integration tests.
- `GenericConvertableFieldComparatorSource` / `NonGenericConvertableFieldComparatorSource` (internal, but the wiring now picks them up automatically).

### Behavior shifts to flag
- **Boost on `[NumericField]`** is silently dropped. Numeric fields don't index norms in 4.8, so the property is accepted for source compatibility but has no effect.
- **Sorting a value-type property without `[NumericField]`** (e.g. plain `int`, `bool`, `DateTime`) falls back to lexicographic string sort because `FieldComparer<T> : class`. Mark it `[NumericField]` for true numeric ordering.
- **Index file format** is incompatible with 3.x. Reindex from source, or run a one-time migration through Lucene's `IndexUpgrader` 3 → 4 path *before* pointing the new library at the directory.

## Documentation

`README.md` substantially expanded:
- "New in the 4.8 port" subsection covering DocValues opt-in and multi-targeting.
- Refreshed "Behaviour caveats" — accurate to the current state, not the mid-port snapshot.
- Full **"Upgrading from Lucene.Net.Linq 3.x"** section with 10 concrete migration steps and an index-incompatibility warning.
- Full **"Mapping objects to documents"** section: attribute walkthrough, complete option tables for `[Field]` and `[NumericField]`, fluent `ClassMap<T>` example, custom converters, multi-valued fields, the two flavors of "key".
- Full **"Query semantics"** section: supported-operators table, `WhereParseQuery` / `Matches`, score & boost, pagination caveat, sort/DocValues selection rules.
- Full **"Sessions, transactions, and the data provider"** section: unit-of-work behavior, replace-on-key, cache-warming callbacks, logging wiring.

## Test status

```
net8.0 : Passed 426  Failed 0  Skipped 1  (Total 427)
net48  : Passed 426  Failed 0  Skipped 1  (Total 427)
0 warnings on either TFM
```

The single skip is a pre-existing `[Ignore]` unrelated to this port.

## Recommended merge approach

This is a hard break — index format incompatibility, package rename, removed public API. Suggest:
1. Merge the branch as-is (don't squash; the staged history is useful for archaeology).
2. Tag the merge commit `v4.8.0-beta.1` (or whatever versioning scheme the fork adopts).
3. Publish to NuGet under the new `Lucene.Net.LinqX` id so existing 3.x consumers don't auto-upgrade and break.
4. Keep `master` for 4.x going forward; if the 3.x line needs life support, branch it as `legacy/3.x` from the pre-merge tip.
