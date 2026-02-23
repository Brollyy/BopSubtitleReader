namespace BopSubtitleReader.Parser;

public sealed class SubtitleSourceAsset(string source, string content, string extension, string languageHint)
{
	public string Source { get; } = source;
	public string Content { get; } = content;
	public string Extension { get; } = extension;
	public string LanguageHint { get; } = languageHint;
}
