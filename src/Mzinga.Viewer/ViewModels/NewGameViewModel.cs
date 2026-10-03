// Copyright (c) Jon Thysell <http://jonthysell.com>
// Licensed under the MIT License.

using System;

using Mzinga.Core;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Mzinga.Viewer.ViewModels
{
    public class NewGameViewModel : ObservableObject
    {
        public static AppViewModel AppVM
        {
            get
            {
                return AppViewModel.Instance;
            }
        }

        public string Title
        {
            get
            {
                return IsNewGame ? "New Game" : "Continue Game";
            }
        }

        public PlayerType WhitePlayerType
        {
            get
            {
                return Settings.WhitePlayerType;
            }
            set
            {
                try
                {
                    Settings.WhitePlayerType = value;
                    OnPropertyChanged(nameof(WhitePlayerType));
                }
                catch (Exception ex)
                {
                    ExceptionUtils.HandleException(ex);
                }
            }
        }

        public PlayerType BlackPlayerType
        {
            get
            {
                return Settings.BlackPlayerType;
            }
            set
            {
                try
                {
                    Settings.BlackPlayerType = value;
                    OnPropertyChanged(nameof(BlackPlayerType));
                }
                catch (Exception ex)
                {
                    ExceptionUtils.HandleException(ex);
                }
            }
        }

        public static bool EnableMosquito
        {
            get
            {
                return AppVM.EngineWrapper.EngineCapabilities.Mosquito;
            }
        }

        public bool IncludeMosquito
        {
            get
            {
                return Enums.BugTypeIsEnabledForGameType(BugType.Mosquito, Settings.GameType);
            }
            set
            {
                try
                {
                    Settings.GameType = Enums.EnableBugType(BugType.Mosquito, Settings.GameType, EnableMosquito && value);
                    OnPropertyChanged(nameof(IncludeMosquito));
                }
                catch (Exception ex)
                {
                    ExceptionUtils.HandleException(ex);
                }
            }
        }

        public static bool EnableLadybug
        {
            get
            {
                return AppVM.EngineWrapper.EngineCapabilities.Ladybug;
            }
        }

        public bool IncludeLadybug
        {
            get
            {
                return Enums.BugTypeIsEnabledForGameType(BugType.Ladybug, Settings.GameType);
            }
            set
            {
                try
                {
                    Settings.GameType = Enums.EnableBugType(BugType.Ladybug, Settings.GameType, EnableLadybug && value);
                    OnPropertyChanged(nameof(IncludeLadybug));
                }
                catch (Exception ex)
                {
                    ExceptionUtils.HandleException(ex);
                }
            }
        }

        public static bool EnablePillbug
        {
            get
            {
                return AppVM.EngineWrapper.EngineCapabilities.Pillbug;
            }
        }

        public bool IncludePillbug
        {
            get
            {
                return Enums.BugTypeIsEnabledForGameType(BugType.Pillbug, Settings.GameType);
            }
            set
            {
                try
                {
                    Settings.GameType = Enums.EnableBugType(BugType.Pillbug, Settings.GameType, EnablePillbug && value);
                    OnPropertyChanged(nameof(IncludePillbug));
                }
                catch (Exception ex)
                {
                    ExceptionUtils.HandleException(ex);
                }
            }
        }

        public BestMoveType BestMoveType
        {
            get
            {
                return Settings.BestMoveType;
            }
            set
            {
                try
                {
                    Settings.BestMoveType = value;
                    OnPropertyChanged(nameof(BestMoveType));
                    OnPropertyChanged(nameof(EnableBestMoveMaxDepthValue));
                    OnPropertyChanged(nameof(BestMoveMaxDepthValue));
                    OnPropertyChanged(nameof(EnableBestMoveMaxTimeValue));
                    OnPropertyChanged(nameof(BestMoveMaxTimeValue));
                    NotifyOpponentWarning();
                }
                catch (Exception ex)
                {
                    ExceptionUtils.HandleException(ex);
                }
            }
        }

        public bool EnableBestMoveMaxDepthValue
        {
            get
            {
                return Settings.BestMoveType == BestMoveType.MaxDepth;
            }
        }

        public int? BestMoveMaxDepthValue
        {
            get
            {
                return Settings.BestMoveMaxDepth;
            }
            set
            {
                Settings.BestMoveMaxDepth = value;
                OnPropertyChanged(nameof(BestMoveMaxDepthValue));
                NotifyOpponentWarning();
            }
        }

        public bool EnableBestMoveMaxTimeValue
        {
            get
            {
                return Settings.BestMoveType == BestMoveType.MaxTime;
            }
        }

        public TimeSpan? BestMoveMaxTimeValue
        {
            get
            {
                return Settings.BestMoveMaxTime;
            }
            set
            {
                Settings.BestMoveMaxTime = value;
                OnPropertyChanged(nameof(BestMoveMaxTimeValue));
                NotifyOpponentWarning();
            }
        }

        #region Difficulty presets

        // Presets only pick the existing search limits (depth or time); the engine itself is unchanged.
        private static readonly (BestMoveType Type, int? Depth, TimeSpan? Time) EasyPreset = (BestMoveType.MaxDepth, 1, null);
        private static readonly (BestMoveType Type, int? Depth, TimeSpan? Time) MediumPreset = (BestMoveType.MaxDepth, 2, null);
        private static readonly (BestMoveType Type, int? Depth, TimeSpan? Time) HardPreset = (BestMoveType.MaxTime, null, TimeSpan.FromSeconds(5));

        public AIDifficulty Difficulty
        {
            get
            {
                return _difficulty;
            }
            set
            {
                try
                {
                    _difficulty = value;

                    var preset = value switch
                    {
                        AIDifficulty.Easy => EasyPreset,
                        AIDifficulty.Medium => MediumPreset,
                        AIDifficulty.Hard => HardPreset,
                        _ => ((BestMoveType Type, int? Depth, TimeSpan? Time)?)null,
                    };

                    if (preset.HasValue)
                    {
                        BestMoveType = preset.Value.Type;
                        if (preset.Value.Type == BestMoveType.MaxDepth)
                        {
                            BestMoveMaxDepthValue = preset.Value.Depth;
                        }
                        else
                        {
                            BestMoveMaxTimeValue = preset.Value.Time;
                        }
                    }

                    OnPropertyChanged(nameof(Difficulty));
                    OnPropertyChanged(nameof(IsCustomDifficulty));
                    OnPropertyChanged(nameof(DifficultyDescription));
                    NotifyOpponentWarning();
                }
                catch (Exception ex)
                {
                    ExceptionUtils.HandleException(ex);
                }
            }
        }
        private AIDifficulty _difficulty;

        public bool IsCustomDifficulty => Difficulty == AIDifficulty.Custom;

        public string DifficultyDescription => Difficulty switch
        {
            AIDifficulty.Easy => "Looks only at its own next move. Good for learning the rules.",
            AIDifficulty.Medium => "Also considers your reply. Punishes obvious mistakes.",
            AIDifficulty.Hard => "Thinks for up to 5 seconds per move, looking several moves ahead.",
            _ => "Search depth: how many moves ahead the AI looks. Max time: the AI looks deeper and deeper until time runs out.",
        };

        // Warns when the opponent will likely search deeper than the viewer's analysis (eval bar,
        // hint, Play best), so the "best move" may be weaker than the opponent's moves.
        public string OpponentStrongerWarning
        {
            get
            {
                double analysisSeconds = AppVM.ViewerConfig.AnalysisSeconds;
                const double strongestAnalysisSeconds = 5.0;

                if (Settings.BestMoveType == BestMoveType.MaxTime && Settings.BestMoveMaxTime.HasValue)
                {
                    double opponentSeconds = Settings.BestMoveMaxTime.Value.TotalSeconds;

                    if (opponentSeconds > strongestAnalysisSeconds)
                    {
                        return $"The opponent thinks {opponentSeconds:0.#} s per move, longer than even the strongest analysis (Deep, 5 s). It may be stronger than the hints and Play best.";
                    }

                    if (opponentSeconds > analysisSeconds)
                    {
                        return $"The opponent thinks {opponentSeconds:0.#} s per move, the analysis only {analysisSeconds:0.#} s. Hints and Play best may be weaker than the opponent; set Analysis strength to Deep in the Engine menu to match it.";
                    }
                }
                else if (Settings.BestMoveType == BestMoveType.MaxDepth && Settings.BestMoveMaxDepth >= 4)
                {
                    return "A search depth of 4 or more can take longer than the analysis and may be stronger than the hints and Play best.";
                }

                return "";
            }
        }

        public bool ShowOpponentStrongerWarning => !string.IsNullOrEmpty(OpponentStrongerWarning);

        private void NotifyOpponentWarning()
        {
            OnPropertyChanged(nameof(OpponentStrongerWarning));
            OnPropertyChanged(nameof(ShowOpponentStrongerWarning));
        }

        private static AIDifficulty DetectDifficulty(GameSettings settings)
        {
            if (Matches(settings, EasyPreset))
            {
                return AIDifficulty.Easy;
            }

            if (Matches(settings, MediumPreset))
            {
                return AIDifficulty.Medium;
            }

            if (Matches(settings, HardPreset))
            {
                return AIDifficulty.Hard;
            }

            return AIDifficulty.Custom;

            static bool Matches(GameSettings s, (BestMoveType Type, int? Depth, TimeSpan? Time) p)
            {
                return s.BestMoveType == p.Type && (p.Type == BestMoveType.MaxDepth ? s.BestMoveMaxDepth == p.Depth : s.BestMoveMaxTime == p.Time);
            }
        }

        #endregion

        public RelayCommand<string> ToggleRadioButton
        {
            get
            {
                return _toggleRadioButton ??= new RelayCommand<string>((parameter) =>
                {
                    try
                    {
                        string[] split = parameter.Split(".", StringSplitOptions.RemoveEmptyEntries);
                        switch (split[0])
                        {
                            case nameof(WhitePlayerType):
                                WhitePlayerType = (PlayerType)Enum.Parse(typeof(PlayerType), split[1]);
                                break;
                            case nameof(BlackPlayerType):
                                BlackPlayerType = (PlayerType)Enum.Parse(typeof(PlayerType), split[1]);
                                break;
                            case nameof(BestMoveType):
                                BestMoveType = (BestMoveType)Enum.Parse(typeof(BestMoveType), split[1]);
                                break;
                            case nameof(Difficulty):
                                Difficulty = (AIDifficulty)Enum.Parse(typeof(AIDifficulty), split[1]);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                });
            }
        }
        private RelayCommand<string> _toggleRadioButton = null;

        public RelayCommand Accept
        {
            get
            {
                return _accept ??= new RelayCommand(() =>
                {
                    try
                    {
                        Accepted = true;
                        RequestClose?.Invoke(this, null);
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                });
            }
        }
        private RelayCommand _accept = null;

        public RelayCommand Reject
        {
            get
            {
                return _reject ??= new RelayCommand(() =>
                {
                    try
                    {
                        Accepted = false;
                        RequestClose?.Invoke(this, null);
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                });
            }
        }
        private RelayCommand _reject = null;

        public GameSettings Settings { get; private set; }

        public bool IsNewGame { get; private set; }

        public bool Accepted { get; private set; }

        public event EventHandler RequestClose;

        public Action<GameSettings> Callback { get; private set; }

        public NewGameViewModel(GameSettings settings, bool isNewGame, Action<GameSettings> callback)
        {
            Settings = settings?.Clone() ?? new GameSettings();
            _difficulty = DetectDifficulty(Settings);

            IsNewGame = isNewGame;

            Accepted = false;
            Callback = callback;
        }

        private GameSettings GetNewGameSettings()
        {
            GameSettings gs = GameSettings.CreateNewFromExisting(Settings);

            gs.Metadata.SetTag("White", gs.WhitePlayerType == PlayerType.Human ? Environment.UserName : AppVM.EngineWrapper.ID);
            gs.Metadata.SetTag("Black", gs.BlackPlayerType == PlayerType.Human ? Environment.UserName : AppVM.EngineWrapper.ID);
            gs.Metadata.SetTag("Date", DateTime.Today.ToString("yyyy.MM.dd"));

            gs.GameMode = GameMode.Play;

            return gs;
        }

        public void ProcessClose()
        {
            if (Callback is not null && Accepted)
            {
                Callback(GetNewGameSettings());
            }
        }
    }

    public enum AIDifficulty
    {
        Easy,
        Medium,
        Hard,
        Custom,
    }
}
