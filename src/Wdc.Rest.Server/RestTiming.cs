using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Wdc.Rest.Server;

/// <summary>
/// The <c>Server-Timing</c> of a REST answer, sent when <see cref="RestServerOptions.ServerTiming"/> is on:
/// <c>total</c> (from the start of the handler to the serialized answer) and what the handler measures here.
/// </summary>
public static class RestTiming
{
    private static readonly object s_key = new();

    /// <summary>Adds a measure. <paramref name="name"/> is a token (letters, digits, <c>-</c>, <c>_</c>, <c>.</c>).</summary>
    /// <remarks>The description is sent only with <see cref="RestServerOptions.ServerTimingDescriptions"/>.</remarks>
    public static void Add(HttpContext context, string name, TimeSpan duration, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            throw new ArgumentException($"'{name}' is not a Server-Timing name.", nameof(name));
        }

        if (RestHttp.Options(context).ServerTiming)
        {
            State(context).Entries.Add((name, duration, description));
        }
    }

    /// <summary>Runs <paramref name="work"/> and adds how long it took, also when it fails.</summary>
    public static async Task<T> Measure<T>(HttpContext context, string name, Func<Task<T>> work, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        var start = Stopwatch.GetTimestamp();
        try
        {
            return await work();
        }
        finally
        {
            Add(context, name, Stopwatch.GetElapsedTime(start), description);
        }
    }

    /// <summary>Runs <paramref name="work"/> and adds how long it took, also when it fails.</summary>
    public static Task Measure(HttpContext context, string name, Func<Task> work, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Measure(context, name, async () =>
        {
            await work();
            return true;
        }, description);
    }

    internal static void Start(HttpContext context)
    {
        if (RestHttp.Options(context).ServerTiming)
        {
            State(context).Start ??= Stopwatch.GetTimestamp();
        }
    }

    internal static string? Header(HttpContext context, bool descriptions)
    {
        if (context.Items[s_key] is not Timings timings)
        {
            return null;
        }

        var text = new StringBuilder();
        foreach (var (name, duration, description) in timings.Entries)
        {
            Append(text, name, duration, descriptions ? description : null);
        }

        if (timings.Start is { } start)
        {
            Append(text, "total", Stopwatch.GetElapsedTime(start), null);
        }

        return text.Length == 0 ? null : text.ToString();
    }

    private static void Append(StringBuilder text, string name, TimeSpan duration, string? description)
    {
        if (text.Length > 0)
        {
            text.Append(", ");
        }

        text.Append(name);
        if (description is not null)
        {
            text.Append(";desc=\"").Append(description.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
        }

        text.Append(";dur=").Append(duration.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture));
    }

    private static Timings State(HttpContext context)
    {
        if (context.Items[s_key] is not Timings timings)
        {
            timings = new Timings();
            context.Items[s_key] = timings;
        }

        return timings;
    }

    private sealed class Timings
    {
        public long? Start { get; set; }

        public List<(string Name, TimeSpan Duration, string? Description)> Entries { get; } = [];
    }
}
