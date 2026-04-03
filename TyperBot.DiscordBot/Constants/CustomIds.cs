namespace TyperBot.DiscordBot.Constants;

/// <summary>
/// Centralized Discord custom IDs for component interactions, modals, and slash commands.
/// Every WithCustomId() call and [ComponentInteraction]/[ModalInteraction] attribute
/// should reference these constants to prevent mismatches.
/// </summary>
public static class CustomIds
{
    public static class Match
    {
        public const string AddButton = "admin_add_match";
        public const string AddModalV2 = "admin_add_match_modal_v2";
        public const string AddModalLegacy = "admin_add_match_modal";
        public const string AddModalBatchRound = "admin_add_match_modal_kolejka";

        public const string EditButton = "admin_edit_match_";
        public const string EditModal = "admin_edit_match_modal_";
        public const string EditButtonWildcard = "admin_edit_match_*";
        public const string EditModalWildcard = "admin_edit_match_modal_*";

        public const string DeleteButton = "admin_delete_match_";
        public const string DeleteButtonWildcard = "admin_delete_match_*";
        public const string CancelButton = "admin_cancel_match_";
        public const string CancelButtonWildcard = "admin_cancel_match_*";
        public const string ConfirmCancel = "admin_confirm_cancel_match_";
        public const string ConfirmCancelWildcard = "admin_confirm_cancel_match_*";
        public const string ConfirmHardDelete = "admin_confirm_hard_delete_match_";
        public const string ConfirmHardDeleteWildcard = "admin_confirm_hard_delete_match_*";
        public const string Restore = "admin_restore_match_";
        public const string RestoreWildcard = "admin_restore_match_*";
        public const string SetCancelledDate = "admin_set_cancelled_match_date_";
        public const string SetCancelledDateWildcard = "admin_set_cancelled_match_date_*";
        public const string SetCancelledDateModal = "admin_set_cancelled_match_date_modal_";
        public const string SetCancelledDateModalWildcard = "admin_set_cancelled_match_date_modal_*";

        public const string AddToRound = "admin_add_match_to_round_";
        public const string AddToRoundWildcard = "admin_add_match_to_round_*";

        public const string CancelAction = "admin_cancel_action_";
        public const string CancelActionWildcard = "admin_cancel_action_*";
    }

    public static class MatchCreationWizard
    {
        public const string RoundSelect = "admin_add_match_round";
        public const string DateSelect = "admin_add_match_date";
        public const string CalendarPrev = "admin_calendar_prev";
        public const string CalendarNext = "admin_calendar_next";
        public const string CalendarToday = "admin_calendar_today";
        public const string TimeMinus15 = "admin_time_minus_15";
        public const string TimePlus15 = "admin_time_plus_15";
        public const string TimeManual = "admin_time_manual";
        public const string TimeModal = "admin_time_modal";
        public const string ContinueButton = "admin_add_match_continue";
    }

    public static class BatchRound
    {
        public const string HomeTeam = "admin_kolejka_home_team";
        public const string AwayTeam = "admin_kolejka_away_team";
        public const string MatchDate = "admin_kolejka_match_date";
        public const string TimeMinus15 = "admin_kolejka_time_minus_15";
        public const string TimePlus15 = "admin_kolejka_time_plus_15";
        public const string TimeManual = "admin_kolejka_time_manual";
        public const string TimeModal = "admin_kolejka_time_modal";
        public const string CalendarPrev = "admin_kolejka_calendar_prev";
        public const string CalendarNext = "admin_kolejka_calendar_next";
        public const string CalendarToday = "admin_kolejka_calendar_today";
        public const string SubmitMatch = "admin_kolejka_submit_match";
        public const string OpenMatchModal = "admin_kolejka_open_match_modal_";
        public const string OpenMatchModalWildcard = "admin_kolejka_open_match_modal_*";
        public const string Finish = "admin_kolejka_finish";
    }

    public static class Result
    {
        public const string SetResult = "admin_set_result_";
        public const string SetResultWildcard = "admin_set_result_*";
        public const string SetResultModal = "admin_set_result_modal_";
        public const string SetResultModalWildcard = "admin_set_result_modal_*";
        public const string ConfirmChange = "admin_confirm_change_result_";
        public const string ConfirmChangeWildcard = "admin_confirm_change_result_*";
        public const string CancelChange = "admin_cancel_change_result_";
        public const string CancelChangeWildcard = "admin_cancel_change_result_*";
        public const string SendMatchTable = "admin_send_match_table_";
        public const string SendMatchTableWildcard = "admin_send_match_table_*";
    }

    public static class Prediction
    {
        public const string PredictMatch = "predict_match_";
        public const string PredictMatchWildcard = "predict_match_*";
        public const string PredictMatchModal = "predict_match_modal_";
        public const string PredictMatchModalWildcard = "predict_match_modal_*";
        public const string MyMatchPrediction = "my_match_prediction_";
        public const string MyMatchPredictionWildcard = "my_match_prediction_*";
        public const string MentionUntyped = "admin_mention_untyped_";
        public const string MentionUntypedWildcard = "admin_mention_untyped_*";
        public const string RevealPredictions = "admin_reveal_predictions_";
        public const string RevealPredictionsWildcard = "admin_reveal_predictions_*";
    }

    public static class Season
    {
        public const string SelectSeason = "admin_select_season";
        public const string StartModal = "admin_start_season_modal";
        public const string EndSeason = "admin_end_season_";
        public const string EndSeasonWildcard = "admin_end_season_*";
        public const string ReactivateSeason = "admin_reactivate_season_";
        public const string ReactivateSeasonWildcard = "admin_reactivate_season_*";
    }

    public static class Round
    {
        public const string AddKolejka = "admin_add_kolejka";
        public const string AddKolejkaModal = "admin_add_kolejka_modal";
        public const string ManageKolejka = "admin_manage_kolejka";
        public const string ManageKolejkaSelect = "admin_manage_kolejka_select";
    }

    public static class Table
    {
        public const string SeasonTable = "admin_table_season";
        public const string RoundTable = "admin_table_round";
        public const string RoundTableSelect = "admin_table_round_select";
    }
}
