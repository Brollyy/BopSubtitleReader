using System;
using System.Collections.Generic;
using System.Linq;
using BopSubtitleReader.Core;

namespace BopSubtitleReader.Parser;

public sealed class SubtitleParserStrategy
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass<SubtitleParserStrategy>();

	private readonly List<ISubtitleParser> _parsers =
	[
		new SrtSubtitleParser(),
		new AssSubtitleParser(),
		new JsonSubtitleParser()
	];

	public bool TryParse(SubtitleSourceAsset asset, out SubtitleCatalog catalog)
	{
		foreach (var parser in _parsers.Where(parser => parser.CanParse(asset)))
		{
			try
			{
				if (parser.TryParse(asset, out catalog))
				{
					return true;
				}
			}
			catch (Exception ex)
			{
				Log.Error($"Parser '{parser.Name}' threw an unexpected exception for '{asset.Source}': [{ex.GetType().Name}] {ex.Message}{Environment.NewLine}{ex}");
			}
		}

		catalog = new SubtitleCatalog();
		Log.Warn($"No parser could read subtitle asset '{asset.Source}'.");
		return false;
	}
}
