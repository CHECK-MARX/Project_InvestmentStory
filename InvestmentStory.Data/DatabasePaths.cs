namespace InvestmentStory.Data;

public static class DatabasePaths
{
    public const string DatabaseFileName = "investment_story.db";

    public static string GetDefaultDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "InvestmentStory", DatabaseFileName);
    }
}
