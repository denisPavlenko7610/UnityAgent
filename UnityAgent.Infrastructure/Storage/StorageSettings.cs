namespace UnityAgent.Infrastructure.Storage;

public sealed class StorageSettings
{
	public const string SectionName = "Storage";

	public int UnusedProjectCacheDays { get; set; } = 30;

	public int TemporaryDataHours { get; set; } = 24;

	public int MaximumCacheSizeMb { get; set; } = 2048;

	public bool PersistChats { get; set; }
}
