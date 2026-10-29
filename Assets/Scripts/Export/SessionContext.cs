using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace EchoSpace.Export
{
    /// <summary>
    /// Holds the unique Session ID of the current experience (one Unity run = one session).
    /// The ID is generated at runtime and is intentionally NOT serialized into the scene.
    /// Contains no participant-identifying information.
    /// </summary>
    public class SessionContext : MonoBehaviour
    {
        private static readonly Regex ValidIdPattern = new Regex("^[A-Za-z0-9_-]{8,40}$", RegexOptions.Compiled);

        private string _sessionId;
        private DateTime _startedAtUtc;

        /// <summary>Format: S-yyyyMMdd-HHmmss-xxxxxx (UTC time + 6 random hex chars).</summary>
        public string SessionId
        {
            get
            {
                EnsureInitialized();
                return _sessionId;
            }
        }

        public DateTime StartedAtUtc
        {
            get
            {
                EnsureInitialized();
                return _startedAtUtc;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
            Debug.Log("[SessionContext] SessionId=" + _sessionId);
        }

        private void EnsureInitialized()
        {
            if (!string.IsNullOrEmpty(_sessionId))
            {
                return;
            }

            _startedAtUtc = DateTime.UtcNow;
            _sessionId = "S-"
                + _startedAtUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + "-"
                + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        /// <summary>Safe to use as a folder name (blocks path traversal and odd characters).</summary>
        public static bool IsValidSessionId(string id)
        {
            return !string.IsNullOrEmpty(id) && ValidIdPattern.IsMatch(id);
        }
    }
}
