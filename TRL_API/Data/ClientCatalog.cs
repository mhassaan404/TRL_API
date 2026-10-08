using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using TRL_API.Models;

namespace TRL_API.Data
{
    public interface IClientCatalog
    {
        // Active or not; null if the code is unknown. Codes are compared case-insensitively.
        Task<ClientInfo?> GetByCodeAsync(string clientCode);
        Task<ClientInfo?> GetByIdAsync(int clientId);
        // True when the client's database has the migration this API requires (SchemaVersion.Required)
        Task<bool> IsSchemaCurrentAsync(ClientInfo client);
    }

    // Reads the client catalog (ConnectionStrings:Catalog). Lookups are cached for a short time so every request can
    // re-check the client cheaply; deactivating a client or migrating its database takes effect within CacheFor.
    public class ClientCatalog : IClientCatalog
    {
        public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

        private readonly string _catalogConnection;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ClientCatalog> _logger;

        public ClientCatalog(IConfiguration configuration, IMemoryCache cache, ILogger<ClientCatalog> logger)
        {
            _catalogConnection = configuration.GetConnectionString("Catalog")
                ?? throw new InvalidOperationException("ConnectionStrings:Catalog is not configured.");
            _configuration = configuration;
            _cache = cache;
            _logger = logger;
        }

        public Task<ClientInfo?> GetByCodeAsync(string clientCode)
        {
            var code = (clientCode ?? "").Trim();
            if (code.Length == 0 || code.Length > 20) return Task.FromResult<ClientInfo?>(null);
            return Cached($"client-code:{code.ToUpperInvariant()}",
                "SELECT ClientId, ClientCode, ClientName, DatabaseName, ConnectionKey, IsActive FROM dbo.Clients WHERE ClientCode = @Value",
                code);
        }

        public Task<ClientInfo?> GetByIdAsync(int clientId) =>
            clientId <= 0
                ? Task.FromResult<ClientInfo?>(null)
                : Cached($"client-id:{clientId}",
                    "SELECT ClientId, ClientCode, ClientName, DatabaseName, ConnectionKey, IsActive FROM dbo.Clients WHERE ClientId = @Value",
                    clientId);

        private async Task<ClientInfo?> Cached(string key, string sql, object value)
        {
            if (_cache.TryGetValue(key, out ClientInfo? hit)) return hit;

            await using var con = new SqlConnection(_catalogConnection);
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Value", value);
            await con.OpenAsync();
            await using var r = await cmd.ExecuteReaderAsync();
            ClientInfo? client = null;
            if (await r.ReadAsync())
            {
                var connectionKey = r.GetString(4);
                var connection = _configuration[$"ClientConnections:{connectionKey}"];
                client = new ClientInfo(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), connectionKey,
                    r.GetBoolean(5), string.IsNullOrWhiteSpace(connection) ? null : connection);
                if (client.ConnectionString == null)
                    _logger.LogError("Client {Code} has no connection string configured (ClientConnections:{Key}).", client.ClientCode, connectionKey);
            }
            _cache.Set(key, client, CacheFor);
            return client;
        }

        public async Task<bool> IsSchemaCurrentAsync(ClientInfo client)
        {
            if (client.ConnectionString == null) return false;
            var key = $"client-schema:{client.ClientId}";
            if (_cache.TryGetValue(key, out bool ok)) return ok;

            await using var con = new SqlConnection(client.ConnectionString);
            // A database from before version tracking has no SchemaMigrations table: that is "behind", not an error
            await using var cmd = new SqlCommand(
                "IF OBJECT_ID('dbo.SchemaMigrations', 'U') IS NULL SELECT 0 " +
                "ELSE SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = @Name) THEN 1 ELSE 0 END", con);
            cmd.Parameters.AddWithValue("@Name", SchemaVersion.Required);
            await con.OpenAsync();
            ok = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
            if (!ok)
                _logger.LogError("Client {Code} database is behind: migration {Required} is not applied.", client.ClientCode, SchemaVersion.Required);
            _cache.Set(key, ok, CacheFor);
            return ok;
        }
    }
}
