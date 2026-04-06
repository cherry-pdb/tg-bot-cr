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

    /// <summary>
    /// Перед десериализацией riverracelog: API иногда отдаёт кавычки внутри строк без экранирования (ломает JSON).
    /// </summary>
    private static string SanitizeClanWarLogJson(string json) =>
        json.Replace("\")", "\\\")");

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("CR API error: {Url} -> {StatusCode} {Body}", url, (int)response.StatusCode, content);
            
            return default;
        }

        if (typeof(T) == typeof(ClanWarLogDto))
            content = SanitizeClanWarLogJson(content);

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(content, JsonOptions);

            if (typeof(T) == typeof(CurrentRiverRaceDto) && TryParseCurrentRiverRaceLoosely(content, out var loose))
            {
                var typed = parsed as CurrentRiverRaceDto;
                if (typed?.Clans is { Count: > 0 })
                    return parsed;

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

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            dto.State = GetJsonString(root, "state");
            dto.PeriodType = GetJsonString(root, "periodType");

            if (!TryGetJsonProperty(root, "clans", out var clansEl) || clansEl.ValueKind != JsonValueKind.Array)
                return false;

            var clans = new List<RiverRaceClanDto>();

            foreach (var clanEl in clansEl.EnumerateArray())
            {
                if (clanEl.ValueKind != JsonValueKind.Object)
                    continue;

                var tag = GetJsonString(clanEl, "tag");

                if (string.IsNullOrWhiteSpace(tag))
                    continue;

                clans.Add(new RiverRaceClanDto
                {
                    Tag = tag,
                    Name = GetJsonString(clanEl, "name"),
                    Fame = GetJsonInt(clanEl, "fame"),
                    RepairPoints = GetJsonInt(clanEl, "repairPoints"),
                    DecksUsed = GetJsonInt(clanEl, "decksUsed"),
                    DecksUsedToday = GetJsonInt(clanEl, "decksUsedToday"),
                    BoatAttacks = GetJsonInt(clanEl, "boatAttacks"),
                    Crowns = GetJsonInt(clanEl, "crowns"),
                    PeriodPoints = GetJsonInt(clanEl, "periodPoints"),
                    Participants = []
                });
            }

            if (clans.Count == 0)
                return false;

            dto.Clans = clans;

            if (TryGetJsonProperty(root, "clan", out var singleEl) && singleEl.ValueKind == JsonValueKind.Object)
            {
                var t = GetJsonString(singleEl, "tag");

                if (!string.IsNullOrWhiteSpace(t))
                {
                    dto.Clan = new RiverRaceClanDto
                    {
                        Tag = t,
                        Name = GetJsonString(singleEl, "name"),
                        Fame = GetJsonInt(singleEl, "fame"),
                        RepairPoints = GetJsonInt(singleEl, "repairPoints"),
                        DecksUsed = GetJsonInt(singleEl, "decksUsed"),
                        DecksUsedToday = GetJsonInt(singleEl, "decksUsedToday"),
                        BoatAttacks = GetJsonInt(singleEl, "boatAttacks"),
                        Crowns = GetJsonInt(singleEl, "crowns"),
                        PeriodPoints = GetJsonInt(singleEl, "periodPoints"),
                        Participants = []
                    };
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetJsonProperty(JsonElement el, string name, out JsonElement value)
    {
        foreach (var p in el.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string GetJsonString(JsonElement el, string name)
    {
        if (!TryGetJsonProperty(el, name, out var p))
            return string.Empty;

        return p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;
    }

    private static int GetJsonInt(JsonElement el, string name)
    {
        if (!TryGetJsonProperty(el, name, out var p))
            return 0;

        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i))
            return i;

        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out i))
            return i;

        return 0;
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