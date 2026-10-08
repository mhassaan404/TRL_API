using Microsoft.Extensions.Caching.Memory;

namespace TRL_API.Services
{
    // Failed-login lockout per client code, username and IP (the same username at two clients is two separate
    // accounts, so they never share a lockout): after MaxFailures wrong attempts within the window that
    // username is locked for LockMinutes from that IP, even with the right password. Keyed by IP too, so someone who
    // only knows a username can't lock its real owner out from another address. Unknown usernames are counted too,
    // so the response doesn't reveal which accounts exist. Kept in memory: it resets on restart and is per server
    // instance. The per-IP "login" rate limit (Program.cs) slows guessing across many usernames.
    public class LoginThrottle
    {
        public const int MaxFailures = 5;
        public const int LockMinutes = 15;

        private readonly IMemoryCache _cache;
        private readonly object _gate = new();

        public LoginThrottle(IMemoryCache cache) => _cache = cache;

        private static string Key(string clientCode, string username, string ip) =>
            $"login-fail:{ip}:{clientCode.Trim().ToUpperInvariant()}:{username.Trim().ToLowerInvariant()}";

        private sealed class Entry
        {
            public int Failures;
            public DateTime? LockedUntil;
        }

        // Minutes left on the lock, or null when the username may try to log in from this IP
        public int? LockedMinutesLeft(string clientCode, string username, string ip)
        {
            if (_cache.TryGetValue(Key(clientCode, username, ip), out Entry? e) && e!.LockedUntil > DateTime.UtcNow)
                return (int)Math.Ceiling((e.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            return null;
        }

        public void RecordFailure(string clientCode, string username, string ip)
        {
            lock (_gate)
            {
                var e = _cache.Get<Entry>(Key(clientCode, username, ip)) ?? new Entry();
                e.Failures++;
                if (e.Failures >= MaxFailures)
                    e.LockedUntil = DateTime.UtcNow.AddMinutes(LockMinutes);
                // The count expires LockMinutes after the last failure
                _cache.Set(Key(clientCode, username, ip), e, TimeSpan.FromMinutes(LockMinutes));
            }
        }

        public void Reset(string clientCode, string username, string ip) => _cache.Remove(Key(clientCode, username, ip));
    }
}
