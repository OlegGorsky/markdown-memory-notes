using Markdig;
using Markdig.Renderers;
using System.Globalization;

namespace MemoryNotes.Web.Services;

public static class MarkdownPreviewRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .Build();

    public static string Render(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer)
        {
            LinkRewriter = RewriteLink
        };
        Pipeline.Setup(renderer);
        Markdown.Convert(markdown, renderer, Pipeline);
        writer.Flush();
        return writer.ToString();
    }

    private static string RewriteLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "#";
        }

        var value = url.Trim();
        if (value.Any(char.IsControl) || value.StartsWith("//", StringComparison.Ordinal))
        {
            return "#";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value;
        }

        return uri.Scheme is "http" or "https" or "mailto"
            ? value
            : "#";
    }
}
