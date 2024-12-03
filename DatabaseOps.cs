using Npgsql;

namespace blobFunctions
{
    public class DatabaseHelper
    {
        private static readonly string? _connectionString = Environment.GetEnvironmentVariable("PostgresDBString");

        public static async Task InsertUser(string userId, string? hash, bool? locked)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                string checkQuery = @"SELECT user_id FROM dbo.users WHERE user_id = @userId";

                var checkCommand = new NpgsqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@userId", userId);
                var result = await checkCommand.ExecuteScalarAsync();
                if (result != null && (int)result > 0)
                {
                    return;
                }

                string query = @"
                    INSERT INTO dbo.users (user_id, creation_date, phash, locked)
                    VALUES (@userId, @creationDate, @phash, CAST(@locked as bit));
                ";



                var command = new NpgsqlCommand(query, connection);
                // Add parameters to avoid SQL injection
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@creationDate", DateTime.UtcNow);
                command.Parameters.AddWithValue("@phash", (object?)hash ?? DBNull.Value);
                command.Parameters.AddWithValue("@locked", locked.HasValue ? (object)(locked.Value ? "1" : "0") : DBNull.Value);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to insert user into the database." + ex.Message);
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

            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = new NpgsqlCommand(query, connection);
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

        public static async Task InsertSASToken(string userId, string sas_token, DateTimeOffset start, DateTimeOffset end)
        {
            try
            {


                if (string.IsNullOrWhiteSpace(userId))
                {
                    throw new ArgumentException("userId is required to insert SAS token.");
                }

                string query = $@"
                INSERT INTO dbo.sas_table (user_id, sas_token, start_time, end_time)
                VALUES (@userId, @sas_token, @start, @end)
            ";

                using var connection = new NpgsqlConnection(_connectionString);
                var command = new NpgsqlCommand(query, connection);
                await connection.OpenAsync();

                // Add parameters to avoid SQL injection
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@sas_token", sas_token);
                command.Parameters.AddWithValue("@start", start);
                command.Parameters.AddWithValue("@end", end);

                await command.ExecuteNonQueryAsync();

            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"failed to insert SAS token for {userId}" + ex.Message);
            }

        }

    }
}
