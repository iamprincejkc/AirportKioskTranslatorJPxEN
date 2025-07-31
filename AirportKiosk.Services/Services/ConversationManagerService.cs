using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using AirportKiosk.Core.Models;

namespace AirportKiosk.Services
{
    public interface IConversationManager
    {
        string CurrentSessionId { get; }
        event EventHandler<ConversationEntry> EntryAdded;
        event EventHandler SessionCleared;

        void StartNewSession();
        void AddEntry(string originalText, string translatedText, string sourceLanguage, string targetLanguage, float confidence = 0.0f);
        List<ConversationEntry> GetCurrentSession();
        List<ConversationEntry> GetRecentEntries(int count = 10);
        void ClearCurrentSession();
        ConversationSession GetSessionInfo();
        void SetSessionTimeout(TimeSpan timeout);
        bool IsSessionExpired();
    }

    public class ConversationManager : IConversationManager
    {
        private readonly ILogger<ConversationManager> _logger;
        private ConversationSession _currentSession;
        private TimeSpan _sessionTimeout = TimeSpan.FromMinutes(30); // Default 30 minutes
        private readonly object _lockObject = new object();

        public string CurrentSessionId => _currentSession?.SessionId ?? string.Empty;

        public event EventHandler<ConversationEntry> EntryAdded;
        public event EventHandler SessionCleared;

        public ConversationManager(ILogger<ConversationManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            StartNewSession();
        }

        public void StartNewSession()
        {
            lock (_lockObject)
            {
                var previousSessionId = _currentSession?.SessionId;

                _currentSession = new ConversationSession
                {
                    SessionId = Guid.NewGuid().ToString(),
                    StartTime = DateTime.UtcNow,
                    LastActivity = DateTime.UtcNow,
                    KioskId = Environment.MachineName
                };

                _logger.LogInformation("New conversation session started - Previous: {Previous}, New: {New}",
                    previousSessionId ?? "None", _currentSession.SessionId);

                SessionCleared?.Invoke(this, EventArgs.Empty);
            }
        }

        public void AddEntry(string originalText, string translatedText, string sourceLanguage, string targetLanguage, float confidence = 0.0f)
        {
            if (string.IsNullOrWhiteSpace(originalText))
            {
                throw new ArgumentException("Original text cannot be empty", nameof(originalText));
            }

            lock (_lockObject)
            {
                // Check if session is expired and start new one if needed
                if (IsSessionExpired())
                {
                    _logger.LogInformation("Session expired, starting new session");
                    StartNewSession();
                }

                var entry = new ConversationEntry
                {
                    OriginalText = originalText.Trim(),
                    TranslatedText = translatedText?.Trim() ?? originalText.Trim(),
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage,
                    Confidence = confidence,
                    SessionId = _currentSession.SessionId,
                    Timestamp = DateTime.UtcNow
                };

                _currentSession.Entries.Add(entry);
                _currentSession.TotalTranslations++;
                _currentSession.LastActivity = DateTime.UtcNow;

                _logger.LogInformation("Conversation entry added - Session: {SessionId}, Entry: {EntryId}, " +
                                     "{Source}→{Target}, Confidence: {Confidence:P1}",
                    _currentSession.SessionId, entry.Id, sourceLanguage, targetLanguage, confidence);

                EntryAdded?.Invoke(this, entry);
            }
        }

        public List<ConversationEntry> GetCurrentSession()
        {
            lock (_lockObject)
            {
                return _currentSession?.Entries?.ToList() ?? new List<ConversationEntry>();
            }
        }

        public List<ConversationEntry> GetRecentEntries(int count = 10)
        {
            lock (_lockObject)
            {
                if (_currentSession?.Entries == null)
                {
                    return new List<ConversationEntry>();
                }

                return _currentSession.Entries
                    .OrderByDescending(e => e.Timestamp)
                    .Take(count)
                    .ToList();
            }
        }

        public void ClearCurrentSession()
        {
            lock (_lockObject)
            {
                var sessionId = _currentSession?.SessionId;
                var entryCount = _currentSession?.Entries?.Count ?? 0;

                StartNewSession();

                _logger.LogInformation("Conversation session cleared - Session: {SessionId}, Entries: {Count}",
                    sessionId, entryCount);
            }
        }

        public ConversationSession GetSessionInfo()
        {
            lock (_lockObject)
            {
                if (_currentSession == null)
                {
                    return null;
                }

                // Return a copy to prevent external modifications
                return new ConversationSession
                {
                    SessionId = _currentSession.SessionId,
                    StartTime = _currentSession.StartTime,
                    LastActivity = _currentSession.LastActivity,
                    TotalTranslations = _currentSession.TotalTranslations,
                    KioskId = _currentSession.KioskId,
                    Entries = _currentSession.Entries.ToList()
                };
            }
        }

        public void SetSessionTimeout(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentException("Timeout must be positive", nameof(timeout));
            }

            _sessionTimeout = timeout;
            _logger.LogInformation("Session timeout updated to {Timeout} minutes", timeout.TotalMinutes);
        }

        public bool IsSessionExpired()
        {
            lock (_lockObject)
            {
                if (_currentSession == null)
                {
                    return true;
                }

                var timeSinceLastActivity = DateTime.UtcNow - _currentSession.LastActivity;
                var isExpired = timeSinceLastActivity > _sessionTimeout;

                if (isExpired)
                {
                    _logger.LogDebug("Session expired - Last activity: {LastActivity}, Timeout: {Timeout}",
                        _currentSession.LastActivity, _sessionTimeout);
                }

                return isExpired;
            }
        }

        public void UpdateLastActivity()
        {
            lock (_lockObject)
            {
                if (_currentSession != null)
                {
                    _currentSession.LastActivity = DateTime.UtcNow;
                }
            }
        }

        public string GetSessionSummary()
        {
            lock (_lockObject)
            {
                if (_currentSession == null)
                {
                    return "No active session";
                }

                var duration = DateTime.UtcNow - _currentSession.StartTime;
                var timeSinceLastActivity = DateTime.UtcNow - _currentSession.LastActivity;

                return $"Session: {_currentSession.SessionId[..8]}..., " +
                       $"Duration: {duration.TotalMinutes:F1}min, " +
                       $"Translations: {_currentSession.TotalTranslations}, " +
                       $"Last activity: {timeSinceLastActivity.TotalMinutes:F1}min ago";
            }
        }
    }
}