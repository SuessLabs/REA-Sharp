using Rea.Validation;

namespace Rea.Tests;

internal static class WebsiteTests
{
    public static IEnumerable<Case> Cases()
    {
        yield return Case.Sync("C# static website conversion removes executable markup while preserving content", () =>
        {
            var source = """<html><body onload="not-run"><script src="old.js"></script><script>not-run</script><h1>Keep</h1><a href="java&#x73;cript:not-run">Link</a><style>body{color:red}</style></body></html>""";
            var result = WebsiteBuilder.StaticMarkup(source);
            Check.True(!result.Contains("<script", StringComparison.OrdinalIgnoreCase)); Check.True(!result.Contains("onload=", StringComparison.OrdinalIgnoreCase));
            Check.True(!result.Contains("script:not-run", StringComparison.OrdinalIgnoreCase)); Check.True(result.Contains("<h1>Keep</h1>", StringComparison.Ordinal)); Check.True(result.Contains("body{color:red}", StringComparison.Ordinal));
            Check.Equal(result, WebsiteBuilder.StaticMarkup(result));
        });
        yield return Case.Sync("C# website publisher builds static pages and rejects JavaScript inputs or source overlap", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "rea-site-" + Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "website/public"); var output = Path.Combine(root, "published");
            Directory.CreateDirectory(input);
            try
            {
                File.WriteAllText(Path.Combine(input, "index.html"), "<h1>REA-Sharp</h1>"); File.WriteAllText(Path.Combine(input, "styles.css"), "body{color:black}");
                Check.Equal(1, WebsiteBuilder.Build(root, output)); Check.True(File.Exists(Path.Combine(output, "styles.css")));
                var overlap = false; try { WebsiteBuilder.Build(root, input); } catch (ArgumentException) { overlap = true; } Check.True(overlap);
                File.WriteAllText(Path.Combine(input, "legacy.js"), "retired fixture");
                var rejected = false; try { WebsiteBuilder.Build(root, output); } catch (InvalidDataException) { rejected = true; } Check.True(rejected);
            }
            finally { Directory.Delete(root, true); }
        });
    }
}
