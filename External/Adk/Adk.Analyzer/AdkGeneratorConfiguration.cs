using System;
using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;

namespace Adk.Analyzer;

/// <summary>
/// Reads per-consumer source-generator switches from an <c>adk.ini</c> additional file.
/// Missing files and missing switches preserve the historical behavior: enabled.
/// </summary>
internal static class AdkGeneratorConfiguration
{
    const string SectionName = "generators";

    public static void RegisterSourceOutput<TSource>(
        this IncrementalGeneratorInitializationContext context,
        string feature,
        IncrementalValueProvider<TSource> source,
        Action<SourceProductionContext, TSource> action)
    {
        var configuredSource = source.Combine(GetEnabledProvider(context, feature));
        context.RegisterSourceOutput(configuredSource, (output, input) =>
        {
            if (input.Right)
                action(output, input.Left);
        });
    }

    public static void RegisterSourceOutput<TSource>(
        this IncrementalGeneratorInitializationContext context,
        string feature,
        IncrementalValuesProvider<TSource> source,
        Action<SourceProductionContext, TSource> action)
    {
        var configuredSource = source.Combine(GetEnabledProvider(context, feature));
        context.RegisterSourceOutput(configuredSource, (output, input) =>
        {
            if (input.Right)
                action(output, input.Left);
        });
    }

    static IncrementalValueProvider<bool> GetEnabledProvider(
        IncrementalGeneratorInitializationContext context,
        string feature)
    {
        var configurations = context.AdditionalTextsProvider
            .Where(static file =>
                string.Equals(
                    Path.GetFileName(file.Path),
                    "adk.ini",
                    StringComparison.OrdinalIgnoreCase))
            .Select(static (file, cancellationToken) =>
                new ConfigurationFile(
                    file.Path,
                    file.GetText(cancellationToken)?.ToString() ?? string.Empty))
            .Collect();

        return configurations.Select((files, _) => IsEnabled(files, feature));
    }

    static bool IsEnabled(ImmutableArray<ConfigurationFile> files, string feature)
    {
        var allEnabled = true;
        bool? featureEnabled = null;

        foreach (var file in files)
            Read(file.Text, feature, ref allEnabled, ref featureEnabled);

        return featureEnabled ?? allEnabled;
    }

    static void Read(
        string text,
        string feature,
        ref bool allEnabled,
        ref bool? featureEnabled)
    {
        var section = string.Empty;
        string[] lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = StripComment(lines[i]).Trim();
            if (line.Length == 0)
                continue;

            if (line[0] == '[' && line[line.Length - 1] == ']')
            {
                section = line.Substring(1, line.Length - 2).Trim();
                continue;
            }

            if (!string.Equals(section, SectionName, StringComparison.OrdinalIgnoreCase))
                continue;

            int separator = line.IndexOf('=');
            if (separator < 0)
                separator = line.IndexOf(':');
            if (separator <= 0)
                continue;

            string key = NormalizeKey(line.Substring(0, separator));
            bool value;
            if (!TryParseBoolean(line.Substring(separator + 1), out value))
                continue;

            if (key == "all")
                allEnabled = value;
            else if (key == NormalizeKey(feature))
                featureEnabled = value;
        }
    }

    static string StripComment(string line)
    {
        int semicolon = line.IndexOf(';');
        int hash = line.IndexOf('#');
        int comment = semicolon < 0
            ? hash
            : hash < 0 ? semicolon : Math.Min(semicolon, hash);
        return comment < 0 ? line : line.Substring(0, comment);
    }

    static string NormalizeKey(string key)
    {
        return key.Trim()
            .Replace('_', '-')
            .Replace('.', '-')
            .ToLowerInvariant();
    }

    static bool TryParseBoolean(string text, out bool value)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "true":
            case "yes":
            case "on":
            case "enabled":
            case "1":
                value = true;
                return true;
            case "false":
            case "no":
            case "off":
            case "disabled":
            case "0":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    readonly struct ConfigurationFile
    {
        public readonly string Path;
        public readonly string Text;

        public ConfigurationFile(string path, string text)
        {
            Path = path;
            Text = text;
        }
    }
}
