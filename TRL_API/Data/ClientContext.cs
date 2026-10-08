using TRL_API.Models;

namespace TRL_API.Data
{
    // The client (and so the database) the current request works with.
    public interface IClientContext
    {
        ClientInfo Client { get; }
        string ConnectionString { get; }
    }

    // Per request. The client comes ONLY from the signed access token: the "cid" claim is checked against the catalog
    // when the token is validated (Program.cs, OnTokenValidated), which stores the client in HttpContext.Items. Nothing
    // the browser sends (query, body, headers) can choose the database. Auth endpoints, which run before there is a
    // token, set the client explicitly with Use() after looking it up themselves.
    public class HttpClientContext : IClientContext
    {
        public const string ItemKey = "TRL.Client";
        public const string ClaimType = "cid";

        private readonly IHttpContextAccessor _http;
        private ClientInfo? _explicit;

        public HttpClientContext(IHttpContextAccessor http) => _http = http;

        public void Use(ClientInfo client) => _explicit = client;

        public ClientInfo Client =>
            _explicit
            ?? _http.HttpContext?.Items[ItemKey] as ClientInfo
            ?? throw new UnauthorizedAccessException("No client is selected for this request.");

        public string ConnectionString =>
            Client.ConnectionString ?? throw new InvalidOperationException($"No database is configured for client {Client.ClientCode}.");
    }

    // A fixed client/database, for tools and tests that work with one database directly
    public sealed class FixedClientContext : IClientContext
    {
        public FixedClientContext(string connectionString) =>
            Client = new ClientInfo(0, "FIXED", "Fixed database", "", "", true, connectionString);

        public ClientInfo Client { get; }
        public string ConnectionString => Client.ConnectionString!;
    }
}
