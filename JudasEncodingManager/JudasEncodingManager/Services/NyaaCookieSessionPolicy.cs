using System;

namespace JudasEncodingManager.Services
{
    public enum NyaaCookieSessionState
    {
        Missing,
        Untracked,
        Fresh,
        Expiring,
        Expired
    }

    /// <summary>
    /// Keeps Nyaa session-cookie timing rules independent from the WPF clock and UI.
    /// </summary>
    public static class NyaaCookieSessionPolicy
    {
        public const int ValidityDays = 28;
        public static readonly TimeSpan WarningWindow = TimeSpan.FromDays(1);

        public static NyaaCookieSessionState GetState(
            string? session,
            DateTime? updatedAtUtc,
            DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(session))
                return NyaaCookieSessionState.Missing;

            if (!updatedAtUtc.HasValue)
                return NyaaCookieSessionState.Untracked;

            var remaining = GetExpiryUtc(updatedAtUtc.Value) - NormalizeUtc(nowUtc);
            if (remaining <= TimeSpan.Zero)
                return NyaaCookieSessionState.Expired;

            return remaining <= WarningWindow
                ? NyaaCookieSessionState.Expiring
                : NyaaCookieSessionState.Fresh;
        }

        public static DateTime GetExpiryUtc(DateTime updatedAtUtc)
        {
            return NormalizeUtc(updatedAtUtc).AddDays(ValidityDays);
        }

        public static bool ShouldShowWarning(
            string? session,
            DateTime? updatedAtUtc,
            DateTime? warningShownAtUtc,
            DateTime nowUtc)
        {
            if (GetState(session, updatedAtUtc, nowUtc) != NyaaCookieSessionState.Expiring)
                return false;

            // The updated timestamp identifies the cookie's validity period.
            // A warning marker from that period (or a later marker from a
            // malformed/clock-adjusted settings file) suppresses duplicates.
            return !warningShownAtUtc.HasValue ||
                   NormalizeUtc(warningShownAtUtc.Value) < NormalizeUtc(updatedAtUtc!.Value);
        }

        public static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }
    }
}