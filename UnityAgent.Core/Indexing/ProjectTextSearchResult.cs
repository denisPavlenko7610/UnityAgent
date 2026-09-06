namespace UnityAgent.Core.Indexing;

public sealed record ProjectTextSearchResult(
	string Path,
	string Snippet,
	double Rank);
