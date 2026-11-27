using System.Globalization;

namespace Wdc.Cube;

/// <summary>
/// A text a view shows, by its key in the application's catalog and the values of its placeholders. The presentation
/// does not know the user's language: each view translates the message with the catalog of its own.
/// </summary>
/// <remarks>
/// <para>An argument is a string, or a message translated in the same language (a date, written by the catalog's own
/// pattern). Two messages are equal when their keys and arguments are, so setting the same message on a scope does not
/// redraw it.</para>
/// <code>Scope.ErrorMessage = Message.Of("login.tooManyAttempts", ("seconds", 30));</code>
/// </remarks>
public sealed class Message : IEquatable<Message>
{
    private static readonly IReadOnlyDictionary<string, object> s_none = new Dictionary<string, object>();

    /// <param name="key">The key in the catalog.</param>
    /// <param name="args">The values of the placeholders: strings or messages.</param>
    public Message(string key, IReadOnlyDictionary<string, object>? args = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (args is not null)
        {
            foreach (var (name, value) in args)
            {
                if (value is not (string or Message))
                {
                    throw new ArgumentException($"The argument '{name}' of '{key}' must be a string or a message.", nameof(args));
                }
            }
        }

        Key = key;
        Args = args is null || args.Count == 0 ? s_none : new Dictionary<string, object>(args, StringComparer.Ordinal);
    }

    public string Key { get; }

    /// <summary>The values of the placeholders: a string, or a message to translate in the same language.</summary>
    public IReadOnlyDictionary<string, object> Args { get; }

    /// <summary>
    /// A message whose arguments are written for the catalog: a message as it is, a number or a date as the invariant
    /// culture writes it, anything else by <see cref="object.ToString"/>, and <see langword="null"/> as an empty text.
    /// </summary>
    public static Message Of(string key, params ReadOnlySpan<(string Name, object? Value)> args)
    {
        var values = new Dictionary<string, object>(args.Length, StringComparer.Ordinal);
        foreach (var (name, value) in args)
        {
            values[name] = value switch
            {
                null => string.Empty,
                Message or string => value,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };
        }

        return new Message(key, values);
    }

    public bool Equals(Message? other)
    {
        if (other is null || other.Key != Key || other.Args.Count != Args.Count)
        {
            return false;
        }

        foreach (var (name, value) in Args)
        {
            if (!other.Args.TryGetValue(name, out var theirs) || !Equals(value, theirs))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as Message);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key, StringComparer.Ordinal);
        foreach (var (name, value) in Args.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            hash.Add(name, StringComparer.Ordinal);
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    /// <summary><c>login.tooManyAttempts(seconds=30)</c>: for logs and test failures, never for the user.</summary>
    public override string ToString() =>
        Args.Count == 0 ? Key : $"{Key}({string.Join(", ", Args.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}={a.Value}"))})";
}
