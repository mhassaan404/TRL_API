namespace TRL_API.Models
{
    // A client (one company using TRL) from the catalog, with its database connection resolved from server settings.
    // ConnectionString is null when ClientConnections:<ConnectionKey> isn't configured on this server.
    public sealed record ClientInfo(
        int ClientId,
        string ClientCode,
        string ClientName,
        string DatabaseName,
        string ConnectionKey,
        bool IsActive,
        string? ConnectionString);
}
