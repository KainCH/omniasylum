namespace OmniForge.Core.Configuration
{
    /// <summary>
    /// Configuration for Azure Table Storage table names.
    /// Allows different table names per environment (e.g., users-dev vs users).
    /// </summary>
    public class AzureTableConfiguration
    {
        public const string SectionName = "AzureTables";

        public string UsersTable { get; set; } = "users";
        public string CountersTable { get; set; } = "counters";
        public string SeriesTable { get; set; } = "series";
        public string AlertsTable { get; set; } = "alerts";
    }
}
