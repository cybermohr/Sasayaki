namespace Sasayaki.Core;

public static class DictationText
{
    public static string ForInsertion(string text)
    {
        if (text.Length > 16000 || text.Any(char.IsControl))
            throw new ServiceException("Text contains unsupported control characters or is too long. The result is retained.");

        // Leave one separator for the next dictation or anything typed afterward.
        var content = text.TrimEnd();
        return string.IsNullOrWhiteSpace(content) ? "" : content + " ";
    }
}
