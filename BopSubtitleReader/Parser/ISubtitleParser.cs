using BopSubtitleReader.Core;

namespace BopSubtitleReader.Parser;

public interface ISubtitleParser
{
	string Name { get; }
	bool CanParse(SubtitleSourceAsset asset);
	bool TryParse(SubtitleSourceAsset asset, out SubtitleCatalog catalog);
}
