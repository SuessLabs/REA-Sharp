using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rea.Validation;

/// <summary>Builds retained website content without a JavaScript runtime.</summary>
public static class WebsiteBuilder
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    public static string StaticMarkup(string source)
    {
        source = Regex.Replace(source, @"<script\b[^>]*>[\s\S]*?</script\s*>", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
        source = Regex.Replace(source, @"\s+on[a-z]+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
        source = Regex.Replace(source, @"(?<attribute>\b(?:href|src|action|xlink:href))\s*=\s*(?<quote>[""'])(?<value>.*?)\k<quote>", match =>
        {
            var value = System.Net.WebUtility.HtmlDecode(match.Groups["value"].Value);
            value = string.Concat(value.Where(character => character > 32));
            return value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ? "" : match.Value;
        }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
        if (Regex.IsMatch(source, @"<script\b|\s+on[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout))
            throw new InvalidDataException("Unsupported executable HTML framing");
        return source;
    }
    public static async Task Convert(string root)
    {
        var website = Path.GetFullPath(Path.Combine(root, "website"));
        var state = await ExecutionChecks.Run(root, "git", ["status", "--porcelain", "--untracked-files=normal", "--", "website"]);
        if (state.ExitCode != 0 || state.Output.Length != 0) throw new InvalidDataException("Website conversion requires unchanged tracked website inputs; preserve local edits before converting");
        var revision = await ExecutionChecks.Run(root, "git", ["rev-parse", "HEAD"]);
        if (revision.ExitCode != 0) throw new InvalidDataException("Cannot preserve source revision");
        var receipts = new List<object>();
        var paths = Directory.GetFiles(website, "*", SearchOption.AllDirectories);
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/'); var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".js" || Path.GetFileName(path) == "package.json")
            {
                receipts.Add(new { path = relative, sha256 = ConvertHash(File.ReadAllBytes(path)), status = "retired-incompatible-browser-or-electron-runtime", recover_from = revision.Output.Trim() });
                File.Delete(path);
            }
            else if (extension is ".html" or ".tmpl" or ".svg")
            {
                var before = await File.ReadAllTextAsync(path); var after = StaticMarkup(before);
                if (extension == ".html")
                    after = Regex.Replace(after, @"<body\b[^>]*>", match => match.Value + "\n<aside role=\"note\" style=\"padding:1rem;border:1px solid #888\">REA-Sharp static archive. Browser/Electron demos and other JavaScript interactions are unavailable. Current analysis uses the C# CLI/MCP.</aside>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
                if (before == after) continue;
                receipts.Add(new { path = relative, sha256 = ConvertHash(System.Text.Encoding.UTF8.GetBytes(before)), status = "static-content-adapted", recover_from = revision.Output.Trim() });
                await File.WriteAllTextAsync(path, after);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(website, "csharp-migration.json"), JsonSerializer.Serialize(new { source_commit = revision.Output.Trim(), scope = "Static content only; client-side demos retired rather than claiming C# equivalents.", entries = receipts }, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static int Build(string root, string destination)
    {
        var source = Path.GetFullPath(Path.Combine(root, "website/public")); destination = Path.GetFullPath(destination);
        if (source == destination || source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Website output must not overlap its input tree");
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories); var pages = 0;
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension is ".js" or ".ts" or ".mjs" or ".cjs" || Path.GetFileName(file) == "package.json") throw new InvalidDataException("JavaScript/Node website input remains: " + file);
            var output = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (extension == ".html") { File.WriteAllText(output, StaticMarkup(File.ReadAllText(file))); pages++; }
            else File.Copy(file, output, true);
        }
        if (pages == 0) throw new InvalidDataException("No website pages discovered");
        return pages;
    }
    private static string ConvertHash(byte[] bytes) => System.Convert.ToHexStringLower(SHA256.HashData(bytes));
}

