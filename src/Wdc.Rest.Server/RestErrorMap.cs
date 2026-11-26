using System.Globalization;
using System.Net;
using Wdc.Domain;

namespace Wdc.Rest.Server;

/// <summary>
/// Which answer an exception becomes. The maps added here come first, the most specific exception type first; a map
/// that returns <see langword="null"/> passes the exception on to the map of its base type, and in the end to the
/// contract's table (<see cref="Default"/>).
/// </summary>
public sealed class RestErrorMap
{
    private readonly Dictionary<Type, Func<Exception, RestError?>> _maps = [];

    /// <summary>Maps <typeparamref name="TException"/> and the types derived from it (unless they have their own map).</summary>
    public RestErrorMap Map<TException>(Func<TException, RestError?> map)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(map);
        _maps[typeof(TException)] = error => map((TException)error);
        return this;
    }

    /// <summary>Answers <typeparamref name="TException"/> with <paramref name="status"/> and the exception's message.</summary>
    public RestErrorMap Map<TException>(HttpStatusCode status)
        where TException : Exception =>
        Map<TException>(error => new RestError(status, error.Message));

    /// <summary>The answer for <paramref name="error"/>.</summary>
    public RestError Resolve(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (_maps.Count > 0)
        {
            for (var type = error.GetType(); type is not null && type != typeof(object); type = type.BaseType)
            {
                if (_maps.TryGetValue(type, out var map) && map(error) is { } mapped)
                {
                    return mapped;
                }
            }
        }

        return Default(error);
    }

    /// <summary>The contract's table (<see cref="RestErrors.ToResponse"/>), with <c>Retry-After</c> on a 429 that knows it.</summary>
    public static RestError Default(Exception error)
    {
        var (status, message) = RestErrors.ToResponse(error);
        return error is TooManyRequestsException { RetryAfter: { } wait }
            ? new RestError(status, message) { Headers = new Dictionary<string, string> { ["Retry-After"] = Seconds(wait) } }
            : new RestError(status, message);
    }

    // Whole seconds, rounded up: waiting less than asked would only be refused again.
    internal static string Seconds(TimeSpan wait) =>
        ((long)Math.Ceiling(Math.Max(0, wait.TotalSeconds))).ToString(CultureInfo.InvariantCulture);
}
