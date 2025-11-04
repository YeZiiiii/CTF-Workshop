using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HRMCPServer.Services;

/// <summary>
/// Service for managing candidate data in memory with file persistence
/// </summary>
public class CandidateService : ICandidateService
{
    private readonly List<Candidate> _candidates;
    private readonly object _candidatesLock = new();
    private readonly ILogger<CandidateService> _logger;
    private readonly HRMCPServerConfiguration _config;

    public CandidateService(
        List<Candidate> candidates,
        ILogger<CandidateService> logger,
        IOptions<HRMCPServerConfiguration> config)
    {
        _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
    }

    public Task<List<Candidate>> GetAllCandidatesAsync()
    {
        lock (_candidatesLock)
        {
            return Task.FromResult(new List<Candidate>(_candidates));
        }
    }

    public Task<bool> AddCandidateAsync(Candidate candidate)
    {
        if (candidate == null)
            throw new ArgumentNullException(nameof(candidate));

        lock (_candidatesLock)
        {
            // Check if candidate with same email already exists
            if (_candidates.Any(c => string.Equals(c.Email, candidate.Email, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(false);
            }

            _candidates.Add(candidate);
            _logger.LogInformation("Added new candidate: {FullName} ({Email})", candidate.FullName, candidate.Email);
            
            // 自动保存到文件
            _ = SaveToFileAsync();
            
            return Task.FromResult(true);
        }
    }

    public Task<bool> UpdateCandidateAsync(string email, Action<Candidate> updateAction)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be null or empty", nameof(email));
        
        if (updateAction == null)
            throw new ArgumentNullException(nameof(updateAction));

        lock (_candidatesLock)
        {
            var candidate = _candidates.FirstOrDefault(c => 
                string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase));

            if (candidate == null)
            {
                return Task.FromResult(false);
            }

            updateAction(candidate);
            _logger.LogInformation("Updated candidate with email: {Email}", email);
            
            // 自动保存到文件
            _ = SaveToFileAsync();
            
            return Task.FromResult(true);
        }
    }

    public Task<bool> RemoveCandidateAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be null or empty", nameof(email));

        lock (_candidatesLock)
        {
            var candidate = _candidates.FirstOrDefault(c => 
                string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase));

            if (candidate == null)
            {
                return Task.FromResult(false);
            }

            _candidates.Remove(candidate);
            _logger.LogInformation("Removed candidate with email: {Email}", email);
            
            // 自动保存到文件
            _ = SaveToFileAsync();
            
            return Task.FromResult(true);
        }
    }

    public Task<List<Candidate>> SearchCandidatesAsync(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return GetAllCandidatesAsync();
        }

        var searchTermLower = searchTerm.Trim().ToLowerInvariant();

        lock (_candidatesLock)
        {
            var matchingCandidates = _candidates.Where(c =>
                c.FirstName.ToLowerInvariant().Contains(searchTermLower) ||
                c.LastName.ToLowerInvariant().Contains(searchTermLower) ||
                c.FullName.ToLowerInvariant().Contains(searchTermLower) ||
                c.Email.ToLowerInvariant().Contains(searchTermLower) ||
                c.CurrentRole.ToLowerInvariant().Contains(searchTermLower) ||
                c.Skills.Any(skill => skill.ToLowerInvariant().Contains(searchTermLower)) ||
                c.SpokenLanguages.Any(lang => lang.ToLowerInvariant().Contains(searchTermLower))
            ).ToList();

            return Task.FromResult(matchingCandidates);
        }
    }

    public async Task<bool> SaveToFileAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_config.CandidatesPath))
            {
                _logger.LogWarning("Candidates path not configured. Cannot save to file.");
                return false;
            }

            List<Candidate> candidatesToSave;
            lock (_candidatesLock)
            {
                candidatesToSave = new List<Candidate>(_candidates);
            }

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            var jsonContent = JsonSerializer.Serialize(candidatesToSave, options);
            
            // 确保目录存在
            var directory = Path.GetDirectoryName(_config.CandidatesPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_config.CandidatesPath, jsonContent);
            _logger.LogInformation("Successfully saved {Count} candidates to file: {Path}", 
                candidatesToSave.Count, _config.CandidatesPath);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving candidates to file: {Path}", _config.CandidatesPath);
            return false;
        }
    }
}
