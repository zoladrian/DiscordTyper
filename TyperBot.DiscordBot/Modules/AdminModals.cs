using Discord;
using Discord.Interactions;

namespace TyperBot.DiscordBot.Modules;

// Modal classes using IModal interface (REQUIRED for Discord.Net 3.x)

public class StartSeasonModal : IModal
{
    public string Title => "Rozpocznij nowy sezon";

    [InputLabel("Nazwa sezonu")]
    [ModalTextInput("season_name", TextInputStyle.Short, placeholder: "PGE Ekstraliga 2025", maxLength: 200, minLength: 1)]
    [RequiredInput(true)]
    public string SeasonName { get; set; } = string.Empty;
}

public class AddKolejkaModal : IModal
{
    public string Title => "Dodaj kolejkę";

    [InputLabel("Numer kolejki (1-18)")]
    [ModalTextInput("kolejka_number", TextInputStyle.Short, placeholder: "1", minLength: 1, maxLength: 2)]
    [RequiredInput(true)]
    public string KolejkaNumber { get; set; } = string.Empty;

    [InputLabel("Liczba meczów w kolejce")]
    [ModalTextInput("liczba_meczow", TextInputStyle.Short, placeholder: "4", minLength: 1, maxLength: 1)]
    [RequiredInput(true)]
    public string LiczbaMeczow { get; set; } = string.Empty;
}

public class AddMatchModalV2 : IModal
{
    public virtual string Title => "Dodaj mecz";

    [InputLabel("Nr Kolejki")]
    [ModalTextInput("round_number", TextInputStyle.Short)]
    [RequiredInput(true)]
    public string RoundNumber { get; set; } = string.Empty;

    [InputLabel("Drużyna domowa")]
    [ModalTextInput("home_team", TextInputStyle.Short)]
    [RequiredInput(true)]
    public string HomeTeam { get; set; } = string.Empty;

    [InputLabel("Drużyna wyjazdowa")]
    [ModalTextInput("away_team", TextInputStyle.Short)]
    [RequiredInput(true)]
    public string AwayTeam { get; set; } = string.Empty;

    [InputLabel("Data (YYYY-MM-DD)")]
    [ModalTextInput("match_date", TextInputStyle.Short, placeholder: "2023-05-01")]
    [RequiredInput(true)]
    public string MatchDate { get; set; } = string.Empty;

    [InputLabel("Godzina (HH:mm)")]
    [ModalTextInput("match_time", TextInputStyle.Short, placeholder: "18:00")]
    [RequiredInput(true)]
    public string MatchTime { get; set; } = string.Empty;
}

public class EditMatchModal : IModal
{
    public string Title => "Edytuj mecz";

    [InputLabel("Drużyna domowa")]
    [ModalTextInput("home_team", TextInputStyle.Short)]
    [RequiredInput(true)]
    public string HomeTeam { get; set; } = string.Empty;

    [InputLabel("Drużyna wyjazdowa")]
    [ModalTextInput("away_team", TextInputStyle.Short)]
    [RequiredInput(true)]
    public string AwayTeam { get; set; } = string.Empty;

    [InputLabel("Data (YYYY-MM-DD)")]
    [ModalTextInput("match_date", TextInputStyle.Short, placeholder: "2023-05-01")]
    [RequiredInput(true)]
    public string Date { get; set; } = string.Empty;

    [InputLabel("Godzina (HH:mm)")]
    [ModalTextInput("match_time", TextInputStyle.Short, placeholder: "18:00")]
    [RequiredInput(true)]
    public string Time { get; set; } = string.Empty;

    [InputLabel("Deadline (YYYY-MM-DD HH:mm)")]
    [ModalTextInput("typing_deadline", TextInputStyle.Short, placeholder: "Puste = 1h przed meczem")]
    [RequiredInput(false)]
    public string TypingDeadline { get; set; } = string.Empty;
}

public class SetResultModal : IModal
{
    public string Title => "Ustaw wynik meczu";

    [InputLabel("Wynik drużyny domowej")]
    [ModalTextInput("home_score", TextInputStyle.Short, placeholder: "50")]
    [RequiredInput(true)]
    public string HomeScore { get; set; } = string.Empty;

    [InputLabel("Wynik drużyny wyjazdowej")]
    [ModalTextInput("away_score", TextInputStyle.Short, placeholder: "40")]
    [RequiredInput(true)]
    public string AwayScore { get; set; } = string.Empty;
}

public class SetCancelledMatchDateModal : IModal
{
    public string Title => "Ustaw nową datę meczu";

    [InputLabel("Data (YYYY-MM-DD)")]
    [ModalTextInput("match_date", TextInputStyle.Short, placeholder: "2025-01-28")]
    [RequiredInput(true)]
    public string Date { get; set; } = string.Empty;

    [InputLabel("Godzina (HH:mm)")]
    [ModalTextInput("match_time", TextInputStyle.Short, placeholder: "18:00")]
    [RequiredInput(true)]
    public string Time { get; set; } = string.Empty;
}

public class TimeModal : IModal
{
    public string Title => "Ustaw godzinę";

    [InputLabel("Godzina (HH:mm)")]
    [ModalTextInput("time", TextInputStyle.Short, placeholder: "18:00")]
    [RequiredInput(true)]
    public string Time { get; set; } = string.Empty;
}
