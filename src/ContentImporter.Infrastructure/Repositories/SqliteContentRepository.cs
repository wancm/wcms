using ContentImporter.Application.Repositories;
using ContentImporter.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace ContentImporter.Infrastructure.Repositories
{
    /// <summary>
    /// Stores imported content in an in-memory SQLite database. Swapping this for a file-backed
    /// or server database is a connection string, not a code change.
    /// </summary>
    public sealed class SqliteContentRepository : IContentRepository, IDisposable
    {
        private const string CreateSchema = """
            CREATE TABLE IF NOT EXISTS content_item (
                id            TEXT PRIMARY KEY,
                provider_code TEXT NOT NULL,
                external_id   TEXT NOT NULL,
                title         TEXT NOT NULL,
                body          TEXT NOT NULL,
                language      TEXT NOT NULL,
                content_type  TEXT NOT NULL,
                published_at  TEXT NULL
            );
            """;

        // OR IGNORE, so a row that is already there is left alone and reports 0 rows changed.
        // That is how we learn whether the item was new without asking first.
        private const string InsertSql = """
            INSERT OR IGNORE INTO content_item
                (id, provider_code, external_id, title, body, language, content_type, published_at)
            VALUES
                (@id, @providerCode, @externalId, @title, @body, @language, @contentType, @publishedAt);
            """;

        private const string UpdateSql = """
            UPDATE content_item SET
                provider_code = @providerCode,
                external_id   = @externalId,
                title         = @title,
                body          = @body,
                language      = @language,
                content_type  = @contentType,
                published_at  = @publishedAt
            WHERE id = @id;
            """;

        // A ":memory:" database lives only as long as a connection to it. This one is held open
        // for the life of the repository - close it and the data is gone.
        private readonly SqliteConnection _connection;

        // SQLite allows one writer at a time. The pipeline's consumers all arrive here at once,
        // so the parallelism stops at this boundary. SemaphoreSlim rather than lock because
        // WaitAsync yields the thread instead of blocking it.
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public SqliteContentRepository()
        {
            /*
             * SqliteConnection is not thread-safe. 
             * Like every ADO.NET connection: "Any public static members of this type are thread safe. 
             * Any instance members are not guaranteed to be thread safe." Our repository is a singleton, 
             * so all 12 consumers call UpsertAsync on the same connection object concurrently. 
             * 
             * Without the semaphore, two threads could be inside CreateCommand() / ExecuteNonQueryAsync() on one connection at the same time 
             * — which is undefined behaviour, not a queue.
             */

            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = CreateSchema;
            command.ExecuteNonQuery();
        }

        public async Task<bool> UpsertAsync(ContentItem item, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var inserted = await ExecuteAsync(InsertSql, item, cancellationToken).ConfigureAwait(false);

                // Already there: overwrite it. Upsert by id is what makes a re-import idempotent.
                if (inserted == 0)
                {
                    await ExecuteAsync(UpdateSql, item, cancellationToken).ConfigureAwait(false);
                }

                return inserted == 1;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>How many rows are stored, for the demo to report at the end.</summary>
        public async Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            // Reads take the same lock as writes. It guards the shared connection, not the
            // database - a read running while an upsert is mid-command is the same race.
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using SqliteCommand command = _connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM content_item;";

                var count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                return Convert.ToInt32(count);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>Reads one item back, or null when it was never stored.</summary>
        public async Task<ContentItem?> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT * FROM content_item WHERE id = @id;";
            command.Parameters.AddWithValue("@id", id);

            using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return new ContentItem
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                ProviderCode = reader.GetString(reader.GetOrdinal("provider_code")),
                ExternalId = reader.GetString(reader.GetOrdinal("external_id")),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Body = reader.GetString(reader.GetOrdinal("body")),
                Language = reader.GetString(reader.GetOrdinal("language")),
                ContentType = reader.GetString(reader.GetOrdinal("content_type")),
                PublishedAt = reader.IsDBNull(reader.GetOrdinal("published_at"))
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("published_at"))
            };
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            _connection.Dispose();
            _writeLock.Dispose();
        }

        private async Task<int> ExecuteAsync(string sql, ContentItem item, CancellationToken cancellationToken)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = sql;

            // Parameters, never string concatenation - the body is customer HTML.
            command.Parameters.AddWithValue("@id", item.Id);
            command.Parameters.AddWithValue("@providerCode", item.ProviderCode);
            command.Parameters.AddWithValue("@externalId", item.ExternalId);
            command.Parameters.AddWithValue("@title", item.Title);
            command.Parameters.AddWithValue("@body", item.Body);
            command.Parameters.AddWithValue("@language", item.Language);
            command.Parameters.AddWithValue("@contentType", item.ContentType);
            command.Parameters.AddWithValue("@publishedAt", (object?)item.PublishedAt ?? DBNull.Value);

            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
