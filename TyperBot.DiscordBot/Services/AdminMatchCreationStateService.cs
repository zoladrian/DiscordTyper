namespace TyperBot.DiscordBot.Services;

public class AdminMatchCreationState
{
    public int? SelectedRound { get; set; }
    public string? SelectedDate { get; set; }
    public string? SelectedTime { get; set; }
    public int CurrentCalendarYear { get; set; }
    public int CurrentCalendarMonth { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AdminMatchCreationStateService
{
    private readonly Dictionary<(ulong guildId, ulong userId), AdminMatchCreationState> _states = new();
    private readonly TimeSpan _expirationTime = TimeSpan.FromMinutes(15);

    public void SetState(ulong guildId, ulong userId, AdminMatchCreationState state)
    {
        var key = (guildId, userId);
        _states[key] = state;
    }

    public AdminMatchCreationState? GetState(ulong guildId, ulong userId)
    {
        var key = (guildId, userId);
        if (_states.TryGetValue(key, out var state))
        {
            // Check if expired
            if (DateTimeOffset.UtcNow - state.CreatedAt > _expirationTime)
            {
                _states.Remove(key);
                return null;
            }
            return state;
        }
        return null;
    }

    public void ClearState(ulong guildId, ulong userId)
    {
        var key = (guildId, userId);
        _states.Remove(key);
    }

    public void UpdateRound(ulong guildId, ulong userId, int round)
    {
        var state = GetOrCreateState(guildId, userId);
        state.SelectedRound = round;
    }

    public void UpdateDate(ulong guildId, ulong userId, string date)
    {
        var state = GetOrCreateState(guildId, userId);
        state.SelectedDate = date;
    }

    public void UpdateTime(ulong guildId, ulong userId, string time)
    {
        var state = GetOrCreateState(guildId, userId);
        state.SelectedTime = time;
    }

    public void UpdateCalendarMonth(ulong guildId, ulong userId, int year, int month)
    {
        var state = GetOrCreateState(guildId, userId);
        state.CurrentCalendarYear = year;
        state.CurrentCalendarMonth = month;
    }

    private AdminMatchCreationState GetOrCreateState(ulong guildId, ulong userId)
    {
        var state = GetState(guildId, userId);
        if (state == null)
        {
            state = new AdminMatchCreationState();
            SetState(guildId, userId, state);
        }
        return state;
    }
}

