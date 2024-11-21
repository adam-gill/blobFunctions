using System.Data;
using Microsoft.Data.SqlClient;

namespace blobFunctions
{
    public class DatabaseHelper
    {
        private static readonly string? _connectionString = Environment.GetEnvironmentVariable("DBConnectionString");

        public static async Task InsertUser(string userId, string? hash, bool? locked)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                string checkQuery = @"SELECT user_id FROM users WHERE user_id = @userId";

                var checkCommand = new SqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@userId", userId);
                int? result = (int)(await checkCommand.ExecuteScalarAsync() ?? 0);

                // do not insert a user row if it is already found
                if (result > 0)
                {
                    return;
                };

                string query = @"
                    INSERT INTO users (user_id, creation_date, phash, locked)
                    VALUES (@userId, @creationDate, @phash, @locked);
                ";



                var command = new SqlCommand(query, connection);
                // Add parameters to avoid SQL injection
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@creationDate", DateTime.UtcNow);
                command.Parameters.AddWithValue("@phash", (object?)hash ?? DBNull.Value);
                command.Parameters.AddWithValue("@locked", (object?)locked ?? DBNull.Value);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to insert user into the database.", ex);
            }
        }

        public static async Task<List<Dictionary<string, object>>> RunQuery(string table, string column, string condition)
        {
            // Validate table and column names to prevent SQL injection
            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(column))
            {
                throw new ArgumentException("Table or column name cannot be null or empty.");
            }

            string query = condition == "none" ? $@"SELECT * FROM {table}" : $@"SELECT * FROM {table} WHERE {column} = @condition";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@condition", condition);

            using var reader = await command.ExecuteReaderAsync();

            // Fetch data and serialize it into JSON
            var results = new List<Dictionary<string, object>>();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? "null" : reader.GetValue(i);
                }
                results.Add(row);
            }

            return results;
        }

    }
}
