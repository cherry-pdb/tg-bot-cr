using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrTelegramBot.ClashRoyale.Models;

namespace CrTelegramBot.ClashRoyale;

public sealed class ClashRoyaleApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ClashRoyaleApiClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ClashRoyaleApiClient(HttpClient httpClient, ILogger<ClashRoyaleApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<PlayerDto?> GetPlayerAsync(string playerTag, CancellationToken ct = default) =>
        GetAsync<PlayerDto>($"players/{EncodeTag(playerTag)}", ct);

    public Task<PlayerChestsDto?> GetUpcomingChestsAsync(string playerTag, CancellationToken ct = default) =>
        GetAsync<PlayerChestsDto>($"players/{EncodeTag(playerTag)}/upcomingchests", ct);

    public Task<ClanDto?> GetClanAsync(string clanTag, CancellationToken ct = default) =>
        GetAsync<ClanDto>($"clans/{EncodeTag(clanTag)}", ct);

    public Task<CurrentRiverRaceDto?> GetCurrentRiverRaceAsync(string clanTag, CancellationToken ct = default) =>
        GetAsync<CurrentRiverRaceDto>($"clans/{EncodeTag(clanTag)}/currentriverrace", ct);

    public Task<ClanWarLogDto?> GetClanWarLogAsync(string clanTag, int limit = 10, CancellationToken ct = default) =>
        GetAsync<ClanWarLogDto>($"clans/{EncodeTag(clanTag)}/riverracelog?limit={limit}", ct);

    public Task<ClanRankingDto?> GetClanRankingsAsync(int locationId = 57000193, int limit = 50, CancellationToken ct = default) =>
        GetAsync<ClanRankingDto>($"locations/{locationId}/rankings/clans?limit={limit}", ct);

    public Task<ClanRankingDto?> GetClanRankingsPageAsync(int locationId = 57000193, int limit = 100, string? after = null, CancellationToken ct = default)
    {
        var url = $"locations/{locationId}/rankings/clans?limit={limit}";
        
        if (!string.IsNullOrWhiteSpace(after))
            url += $"&after={Uri.EscapeDataString(after)}";
        
        return GetAsync<ClanRankingDto>(url, ct);
    }

    public Task<ClanRankingDto?> GetClanWarRankingsPageAsync(int locationId = 57000193, int limit = 100, string? after = null, CancellationToken ct = default)
    {
        var url = $"locations/{locationId}/rankings/clanwars?limit={limit}";
        
        if (!string.IsNullOrWhiteSpace(after))
            url += $"&after={Uri.EscapeDataString(after)}";
        
        return GetAsync<ClanRankingDto>(url, ct);
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("CR API error: {Url} -> {StatusCode} {Body}", url, (int)response.StatusCode, content);
            
            return default;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(content, JsonOptions);

            if (typeof(T) == typeof(CurrentRiverRaceDto) && TryParseCurrentRiverRaceLoosely(content, out var loose))
            {
                var typed = parsed as CurrentRiverRaceDto;
                var typedScore = ScoreCurrentRiverRace(typed);
                var looseScore = ScoreCurrentRiverRace(loose);
                
                if (looseScore > typedScore)
                {
                    _logger.LogWarning("CR API currentriverrace: using loose parse (score {Loose} > {Typed}) for {Url}", looseScore, typedScore, url);
                    
                    return (T)(object)loose;
                }
            }

            if (typeof(T) == typeof(ClanWarLogDto) && TryParseClanWarLogLoosely(content, out var looseWarLog))
            {
                var typed = parsed as ClanWarLogDto;
                var typedScore = ScoreClanWarLog(typed);
                var looseScore = ScoreClanWarLog(looseWarLog);
                
                if (looseScore > typedScore)
                {
                    _logger.LogWarning("CR API riverracelog: using loose parse (score {Loose} > {Typed}) for {Url}", looseScore, typedScore, url);
                    
                    return (T)(object)looseWarLog;
                }
            }

            return parsed;
        }
        catch (JsonException ex)
        {
            try
            {
                if (typeof(T) == typeof(CurrentRiverRaceDto) && TryParseCurrentRiverRaceLoosely(content, out var loose))
                {
                    _logger.LogWarning(ex, "CR API JSON parse error for {Url}. Loose currentriverrace parse succeeded.", url);
                    
                    return (T)(object)loose;
                }
            }
            catch (Exception looseEx)
            {
                _logger.LogWarning(looseEx, "CR API loose parse failed for {Url}", url);
            }

            try
            {
                if (typeof(T) == typeof(ClanWarLogDto) && TryParseClanWarLogLoosely(content, out var looseWarLog))
                {
                    _logger.LogWarning(ex, "CR API JSON parse error for {Url}. Loose riverracelog parse succeeded.", url);
                    
                    return (T)(object)looseWarLog;
                }
            }
            catch (Exception looseLogEx)
            {
                _logger.LogWarning(looseLogEx, "CR API loose riverracelog parse failed for {Url}", url);
            }

            var sample = content.Length <= 800 ? content : content[..800];
            var fileName = $"crapi_badjson_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmssfff}.json";
            
            try
            {
                await File.WriteAllTextAsync(fileName, content, Encoding.UTF8, ct);
            }
            catch (Exception writeEx)
            {
                _logger.LogWarning(writeEx, "Could not write bad JSON to disk");
            }

            _logger.LogError(ex, "CR API JSON parse error for {Url}. Saved to {File}. Sample: {Sample}", url, fileName, sample);
            
            return default;
        }
    }

    private static int ScoreCurrentRiverRace(CurrentRiverRaceDto? dto)
    {
        if (dto?.Clans is null || dto.Clans.Count == 0)
            return 0;
        
        var score = 0;
        
        foreach (var c in dto.Clans)
        {
            if (c.DecksUsedToday != 0)
                score += 3;
            
            if (c.DecksUsed != 0)
                score += 1;
            
            if (c.Fame != 0)
                score += 1;
            
            if (c.RepairPoints != 0)
                score += 1;
            
            if (c.PeriodPoints != 0)
                score += 1;
        }
        
        return score;
    }

    private static bool TryParseCurrentRiverRaceLoosely(string json, out CurrentRiverRaceDto dto)
    {
        dto = new CurrentRiverRaceDto();
        
        if (string.IsNullOrWhiteSpace(json)) 
            return false;

        var clansIdx = json.IndexOf("\"clans\"", StringComparison.OrdinalIgnoreCase);
        var slice = clansIdx >= 0 ? json[clansIdx..] : json;
        var tagMatches = Regex.Matches(slice, "\"tag\"\\s*:\\s*\"(?<tag>#[^\"]+)\"", RegexOptions.IgnoreCase);
        
        if (tagMatches.Count == 0) 
            return false;

        var clans = new List<RiverRaceClanDto>(Math.Min(5, tagMatches.Count));

        foreach (Match m in tagMatches)
        {
            var tag = m.Groups["tag"].Value;
            
            if (string.IsNullOrWhiteSpace(tag) || clans.Any(c => c.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                continue;

            var start = m.Index;
            var windowLen = Math.Min(2500, slice.Length - start);
            
            if (windowLen <= 0)
                continue;
            
            var window = slice.Substring(start, windowLen);

            clans.Add(new RiverRaceClanDto
            {
                Tag = tag,
                Name = GetName(),
                Fame = GetInt("fame"),
                RepairPoints = GetInt("repairPoints"),
                DecksUsed = GetInt("decksUsed"),
                DecksUsedToday = GetInt("decksUsedToday"),
                BoatAttacks = GetInt("boatAttacks"),
                Crowns = GetInt("crowns"),
                PeriodPoints = GetInt("periodPoints"),
                Participants = []
            });
            
            continue;

            string GetName()
            {
                var mm = Regex.Match(
                    window,
                    "\"name\"\\s*:\\s*\"(?<name>[\\s\\S]*?)\"\\s*,\\s*\"(?:fame|repairPoints|decksUsed|decksUsedToday|boatAttacks|crowns|periodPoints)\"",
                    RegexOptions.IgnoreCase);
                
                return mm.Success ? mm.Groups["name"].Value : tag;
            }

            int GetInt(string field)
            {
                var mm = Regex.Match(window, $"\"{Regex.Escape(field)}\"\\s*:\\s*(?<n>-?\\d+)", RegexOptions.IgnoreCase);
                
                return mm.Success && int.TryParse(mm.Groups["n"].Value, out var n) ? n : 0;
            }
        }

        if (clans.Count == 0)
            return false;
        
        dto.Clans = clans;
        var clanIdx = json.IndexOf("\"clan\"", StringComparison.OrdinalIgnoreCase);
        
        if (clanIdx >= 0)
        {
            var clanSlice = json.Substring(clanIdx, Math.Min(8000, json.Length - clanIdx));
            var mmTag = Regex.Match(clanSlice, "\"tag\"\\s*:\\s*\"(?<tag>#[^\"]+)\"", RegexOptions.IgnoreCase);
            var tag = mmTag.Success ? mmTag.Groups["tag"].Value : string.Empty;
            
            if (!string.IsNullOrWhiteSpace(tag))
            {
                int GetInt(string field)
                {
                    var mm = Regex.Match(clanSlice, $"\"{Regex.Escape(field)}\"\\s*:\\s*(?<n>-?\\d+)", RegexOptions.IgnoreCase);
                    
                    return mm.Success && int.TryParse(mm.Groups["n"].Value, out var n) ? n : 0;
                }

                dto.Clan = new RiverRaceClanDto
                {
                    Tag = tag,
                    Name = tag,
                    Fame = GetInt("fame"),
                    RepairPoints = GetInt("repairPoints"),
                    DecksUsed = GetInt("decksUsed"),
                    DecksUsedToday = GetInt("decksUsedToday"),
                    BoatAttacks = GetInt("boatAttacks"),
                    Crowns = GetInt("crowns"),
                    PeriodPoints = GetInt("periodPoints"),
                    Participants = []
                };
            }
        }

        return true;
    }

    private static int ScoreClanWarLog(ClanWarLogDto? dto)
    {
        if (dto?.Items is null || dto.Items.Count == 0)
            return 0;
        
        var score = 0;
        
        foreach (var it in dto.Items)
        {
            if (it.SeasonId != 0)
                score += 1;
            
            if (it.SectionIndex != 0)
                score += 1;
            
            if (it.Standings is null || it.Standings.Count == 0)
                continue;
            
            foreach (var s in it.Standings)
            {
                if (!string.IsNullOrWhiteSpace(s.Tag))
                    score += 1;
                
                if (s.Clan is not null && !string.IsNullOrWhiteSpace(s.Clan.Tag))
                    score += 1;
                
                if (s.PeriodPoints != 0)
                    score += 3;
                
                if (s.DecksUsed != 0)
                    score += 2;
                
                if (s.Fame != 0)
                    score += 1;
            }
        }
        
        return score;
    }

    private static bool TryParseClanWarLogLoosely(string json, out ClanWarLogDto dto)
    {
        dto = new ClanWarLogDto();
        
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array)
                return false;

            var items = new List<ClanWarLogItemDto>();
            
            foreach (var itemEl in itemsEl.EnumerateArray())
            {
                if (itemEl.ValueKind != JsonValueKind.Object)
                    continue;

                var item = new ClanWarLogItemDto
                {
                    CreatedDate = GetString(itemEl, "createdDate"),
                    SeasonId = GetInt(itemEl, "seasonId"),
                    SectionIndex = GetInt(itemEl, "sectionIndex"),
                    Standings = []
                };

                if (itemEl.TryGetProperty("standings", out var standingsEl) && standingsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var stEl in standingsEl.EnumerateArray())
                    {
                        if (stEl.ValueKind != JsonValueKind.Object)
                            continue;

                        var clanRef = ParseClanRef(stEl);
                        var flatTag = GetString(stEl, "tag");
                        var flatName = GetString(stEl, "name");
                        var standing = new RiverRaceClanDto
                        {
                            Tag = flatTag,
                            Name = flatName,
                            Clan = clanRef,
                            Fame = GetInt(stEl, "fame"),
                            RepairPoints = GetInt(stEl, "repairPoints"),
                            DecksUsed = GetInt(stEl, "decksUsed", "totalDecksUsed", "decksUsedTotal"),
                            DecksUsedToday = GetInt(stEl, "decksUsedToday"),
                            BoatAttacks = GetInt(stEl, "boatAttacks"),
                            Crowns = GetInt(stEl, "crowns"),
                            PeriodPoints = GetInt(stEl, "periodPoints", "points", "fame"),
                            Participants = []
                        };

                        item.Standings.Add(standing);
                    }
                }

                items.Add(item);
            }

            if (items.Count == 0)
                return false;
            
            dto.Items = items;
            
            return true;

            RiverRaceClanRefDto? ParseClanRef(JsonElement standingEl)
            {
                if (!standingEl.TryGetProperty("clan", out var clanEl) || clanEl.ValueKind != JsonValueKind.Object)
                    return null;

                var tag = GetString(clanEl, "tag");
                var name = GetString(clanEl, "name");
                
                if (string.IsNullOrWhiteSpace(tag) && string.IsNullOrWhiteSpace(name))
                    return null;
                
                return new RiverRaceClanRefDto { Tag = tag, Name = name };
            }

            string GetString(JsonElement obj, params string[] names)
            {
                foreach (var n in names)
                {
                    if (!obj.TryGetProperty(n, out var v))
                        continue;
                    
                    if (v.ValueKind == JsonValueKind.String)
                        return v.GetString() ?? string.Empty;
                }
                
                return string.Empty;
            }

            int GetInt(JsonElement obj, params string[] names)
            {
                foreach (var n in names)
                {
                    if (!obj.TryGetProperty(n, out var v))
                        continue;
                    
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i))
                        return i;
                    
                    if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out i))
                        return i;
                }
                
                return 0;
            }
        }
        catch
        {
            return false;
        }
    }

    public static string NormalizeTag(string tag)
    {
        tag = tag.Trim().ToUpperInvariant();
        
        if (!tag.StartsWith('#'))
            tag = "#" + tag;
        
        return tag;
    }

    private static string EncodeTag(string tag) => Uri.EscapeDataString(NormalizeTag(tag));
}