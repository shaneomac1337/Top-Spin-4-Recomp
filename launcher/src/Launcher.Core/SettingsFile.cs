using System.Globalization;
using System.Text;

namespace TopSpin4.Launcher.Core;

/// <summary>
/// The player's Settings file (<c>topspin4.settings.toml</c>), written with the game's own rules
/// (src/settings/config_file.h, which this mirrors): a top-level <c>version</c>, a line-preserving
/// single-key rewrite, a temporary file and a rename, and a file of a newer build left alone.
/// The version this build writes comes from the game's export (settings_file_version). A game
/// migration must also be added to <c>Migrations</c> here: saving a setting migrates an older file first.
/// </summary>
public static class SettingsFile
{
    public const string FileName = "topspin4.settings.toml";

    /// <summary>
    /// The version to assume only when the game's export does not say (<see cref="SettingsSchema.FileVersion"/>);
    /// the game decides it (CurrentVersion in src/settings/config_file.h).
    /// </summary>
    public const long FallbackVersion = 1;

    public static string PathIn(string userDataRoot) => Path.Combine(userDataRoot, FileName);

    // Each line split into its body and line end ("\n", "\r\n" or nothing at the end). fn returns false to stop.
    private static void ForEachLine(string text, Func<string, string, bool> fn)
    {
        var start = 0;
        while (start < text.Length)
        {
            var newline = text.IndexOf('\n', start);
            var end = newline < 0 ? text.Length : newline + 1;
            var current = text[start..end];
            var ending = current.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n"
                : current.EndsWith('\n') ? "\n" : "";
            if (!fn(current[..^ending.Length], ending)) return;
            start = end;
        }
    }

    // Whether the line is `key = ...` (blanks before the key allowed); rest is what follows the equals sign.
    private static bool IsKeyLine(string body, string key, out string rest)
    {
        rest = "";
        var first = body.AsSpan().IndexOfAnyExcept(" \t");
        if (first < 0 || !body.AsSpan(first).StartsWith(key, StringComparison.Ordinal)) return false;
        var after = body.AsSpan(first + key.Length);
        var equals = after.IndexOfAnyExcept(" \t");
        if (equals < 0 || after[equals] != '=') return false;
        rest = after[(equals + 1)..].ToString();
        return true;
    }

    /// <summary>The text with key set to the TOML literal: its line replaced (keeping the line end), or appended.</summary>
    public static string SetKey(string text, string key, string literal)
    {
        var line = key + " = " + literal;
        var output = new StringBuilder();
        var found = false;
        ForEachLine(text, (body, ending) =>
        {
            if (!found && IsKeyLine(body, key, out _))
            {
                output.Append(line).Append(ending);
                found = true;
            }
            else
            {
                output.Append(body).Append(ending);
            }
            return true;
        });
        if (!found)
        {
            if (output.Length > 0 && output[^1] != '\n') output.Append('\n');
            output.Append(line).Append('\n');
        }
        return output.ToString();
    }

    /// <summary>The file's top-level integer <c>version</c>; null when it has none (counts as 1) or it is not an integer.</summary>
    public static long? Version(string text)
    {
        long? version = null;
        ForEachLine(text, (body, _) =>
        {
            if (!IsKeyLine(body, "version", out var rest)) return true;
            // As FileVersion in config_file.h: an optional minus, digits that fit a long, then only blanks or a comment.
            rest = rest.TrimStart(' ', '\t');
            var sign = rest.StartsWith('-') ? 1 : 0;
            var digits = rest.Skip(sign).TakeWhile(char.IsAsciiDigit).Count();
            if (digits == 0) return false;
            var number = rest[..(sign + digits)];
            var after = rest[(sign + digits)..].TrimStart(' ', '\t', '\r');
            if ((after.Length == 0 || after[0] == '#')
                && long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                version = value;
            return false;
        });
        return version;
    }

    /// <summary>Whether a newer build wrote the file. Such a file is read as well as possible and never rewritten.</summary>
    public static bool IsNewerThanBuild(string text, long currentVersion) => (Version(text) ?? 1) > currentVersion;

    /// <summary>The text with the version line set, or added first (a top-level key must come before any table).</summary>
    public static string WithVersion(string text, long version)
    {
        var hasLine = false;
        ForEachLine(text, (body, _) =>
        {
            hasLine = IsKeyLine(body, "version", out _);
            return !hasLine;
        });
        return hasLine
            ? SetKey(text, "version", version.ToString(CultureInfo.InvariantCulture))
            : "version = " + version.ToString(CultureInfo.InvariantCulture) + "\n" + text;
    }

    /// <summary>
    /// The text with key's value <paramref name="from"/> (a string in either quote, or a bare number) changed to
    /// <paramref name="to"/> in the same form, keeping the rest of the line. Any other value is left alone.
    /// As ReplaceTomlValue in config_file.h.
    /// </summary>
    public static string ReplaceValue(string text, string key, string from, string to)
    {
        var output = new StringBuilder();
        ForEachLine(text, (body, ending) =>
        {
            if (IsKeyLine(body, key, out var rest))
            {
                // rest is the end of body, so the value starts where the blanks after the equals sign end.
                var value = rest.TrimStart(' ', '\t');
                var length = 0;
                var quote = "";
                if (value.StartsWith('"') || value.StartsWith('\''))
                {
                    var close = value.IndexOf(value[0], 1);
                    if (close >= 0)
                    {
                        length = close + 1;
                        quote = value[..1];
                    }
                }
                else
                {
                    var stop = value.AsSpan().IndexOfAny(" \t#\r");
                    length = stop < 0 ? value.Length : stop;
                }
                var inner = quote.Length == 0 ? value[..length] : value[1..(length - 1)];
                if (length != 0 && inner == from)
                {
                    output.Append(body.AsSpan(0, body.Length - value.Length)).Append(quote).Append(to).Append(quote)
                        .Append(value.AsSpan(length)).Append(ending);
                    return true;
                }
            }
            output.Append(body).Append(ending);
            return true;
        });
        return output.ToString();
    }

    /// <summary>
    /// The game's settings migrations, in order (Migrations in config_file.h, which this mirrors): the one at index i
    /// takes a file of version i + 1 to version i + 2.
    /// </summary>
    private static readonly Func<string, string>[] Migrations =
    [
        // 1 -> 2: the Resolution scale's 4x was removed (the largest is 3x), so a saved 4 becomes 3.
        text => ReplaceValue(text, "ts4_resolution_scale", "4", "3"),
        // 2 -> 3: players no longer choose Classic, so a saved one becomes Native.
        text => ReplaceValue(text, "ts4_renderer", "classic", "native"),
    ];

    /// <summary>
    /// The text brought to <paramref name="currentVersion"/> (the game's export): its migrations run from the file's
    /// version on, and the version line is set. A file of a newer build is returned as it is.
    /// </summary>
    public static string Migrate(string text, long currentVersion)
    {
        if (IsNewerThanBuild(text, currentVersion)) return text;
        var version = Math.Max(Version(text) ?? 1, 1);
        // The game's own list is as long as its version says; this one stops at the migrations it knows.
        for (var i = version - 1; i < Math.Min(Migrations.Length, currentVersion - 1); i++) text = Migrations[i](text);
        return WithVersion(text, currentVersion);
    }

    /// <summary>The text after every migration this build knows, for a reader without the game's export.</summary>
    public static string MigrateToLatest(string text) => Migrate(text, Migrations.Length + 1);

    /// <summary>The value written after <paramref name="key"/> with quotes and trailing comment removed, or null when the key is absent.</summary>
    public static string? ValueOf(string text, string key)
    {
        string? value = null;
        ForEachLine(text, (body, _) =>
        {
            if (!IsKeyLine(body, key, out var rest)) return true;
            value = Unquote(rest.Trim());
            return false;
        });
        return value;
    }

    private static string Unquote(string literal)
    {
        if (literal.Length > 0 && literal[0] == '\'')
        {
            var close = literal.IndexOf('\'', 1);
            return close < 0 ? literal[1..] : literal[1..close];
        }
        if (literal.Length > 0 && literal[0] == '"')
        {
            var value = new StringBuilder();
            for (var i = 1; i < literal.Length && literal[i] != '"'; i++)
            {
                if (literal[i] == '\\' && i + 1 < literal.Length) i++;
                value.Append(literal[i]);
            }
            return value.ToString();
        }
        var comment = literal.IndexOf('#');
        return (comment < 0 ? literal : literal[..comment]).Trim();
    }

    /// <summary>The TOML literal for a value: bare for a bool, a basic string otherwise.</summary>
    public static string Literal(SettingDescription setting, string value) =>
        setting.IsBool ? value : "\"" + value + "\"";

    /// <summary>
    /// Writes key = literal into the file at <paramref name="path"/> (an older or unversioned file is brought to this
    /// version first, a new one created at it) through a temporary file and a rename. Only that line changes otherwise.
    /// A file of a newer build is left alone: false.
    /// </summary>
    public static bool Save(string path, string key, string literal, long currentVersion)
    {
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        if (IsNewerThanBuild(text, currentVersion)) return false;
        var result = SetKey(Migrate(text, currentVersion), key, literal);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, new UTF8Encoding(false).GetBytes(result));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); } catch (IOException) { }
            return false;
        }
    }
}
