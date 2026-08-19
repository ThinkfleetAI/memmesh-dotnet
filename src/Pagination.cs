using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MemMesh;

/// <summary>One page of a seek-paginated list endpoint. <c>Next</c> and
/// <c>Previous</c> are opaque cursors — pass <c>Next</c> back to fetch the
/// following page; both are null at the respective ends.</summary>
public sealed record SeekPage<T>(
    [property: JsonPropertyName("data")] IReadOnlyList<T> Data,
    [property: JsonPropertyName("next")] string? Next,
    [property: JsonPropertyName("previous")] string? Previous);

/// <summary>Cursor-following helpers over <see cref="SeekPage{T}"/>.</summary>
public static class Pagination
{
    /// <summary>Flatten a seek-paginated endpoint into a single async stream,
    /// following <c>Next</c> cursors until exhausted. Resources supply a
    /// <paramref name="fetchPage"/> that fetches one page given a cursor
    /// (null for the first page).</summary>
    public static async IAsyncEnumerable<T> ListAllAsync<T>(
        Func<string?, CancellationToken, Task<SeekPage<T>>> fetchPage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? cursor = null;
        do
        {
            ct.ThrowIfCancellationRequested();
            var page = await fetchPage(cursor, ct).ConfigureAwait(false);
            if (page.Data is not null)
                foreach (var item in page.Data)
                    yield return item;
            cursor = page.Next;
        } while (cursor is not null);
    }
}
