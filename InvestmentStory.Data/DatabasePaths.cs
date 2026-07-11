namespace InvestmentStory.Data;

public static class DatabasePaths
{
    public const string DatabaseFileName = "investment_story.db";
    public const string SampleSessionDatabaseFileName = "investment_story_sample_session.db";

    public static string GetDefaultDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "InvestmentStory", DatabaseFileName);
    }

    public static string GetSampleSessionDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "InvestmentStory", SampleSessionDatabaseFileName);
    }

    public static void DeleteSampleSessionDatabase()
    {
        var path = GetSampleSessionDatabasePath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
