namespace Rebelgent.Persistence.Options;

/// <summary>Configuration options for the Rebelgent persistence layer.</summary>
public class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public string ConnectionString { get; set; } = "Data Source=data/rebelgent.db";
}
