using System.Globalization;
using System.Text;

namespace Notes.Core.Notes;

public static class NoteTitle
{
    public static string FromBodyOrFileName(string body, string path)
    {
        var heading = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .FirstOrDefault(static line => line.StartsWith("# ", StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(heading))
        {
            return heading[2..].Trim();
        }

        return System.IO.Path.GetFileNameWithoutExtension(path).Replace('-', ' ');
    }

    public static string ToSlug(string title)
    {
        var normalized = title.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        var previousDash = false;

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "note" : slug;
    }
}
