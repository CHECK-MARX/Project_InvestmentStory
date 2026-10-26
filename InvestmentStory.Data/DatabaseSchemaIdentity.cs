using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

internal static class DatabaseSchemaIdentity
{
    public static string Compute(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT type, name, tbl_name, sql
            FROM sqlite_master
            WHERE sql IS NOT NULL
            ORDER BY type, name, tbl_name;
            """;
        using var reader = command.ExecuteReader();
        var schema = new StringBuilder();
        while (reader.Read())
        {
            for (var index = 0; index < 4; index++)
            {
                var value = reader.GetString(index);
                schema.Append(value.Length).Append(':').Append(value);
            }
            schema.Append('\n');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(schema.ToString()));
        return "schema-" + Convert.ToHexString(hash)[..16];
    }
}
