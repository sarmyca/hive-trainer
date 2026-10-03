// Copyright (c) Jon Thysell <http://jonthysell.com>
// Licensed under the MIT License.

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

using Mzinga.Core;
using Mzinga.Core.AI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Avalonia.Styling;

namespace Mzinga.Viewer.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        public static AppViewModel AppVM
        {
            get
            {
                return AppViewModel.Instance;
            }
        }

        public static string Title
        {
            get
            {
                string title = AppVM.ProgramTitle;

                if (AppVM.EngineWrapper is CLIEngineWrapper)
                {
                    title += $" [{AppVM.EngineWrapper.ID}]";
                }

                if (IsReviewMode)
                {
                    var fileUri = AppVM.EngineWrapper.CurrentGameSettings?.GameRecording?.FileUri;

                    if (fileUri is not null)
                    {
                        title = $"{(fileUri.IsFile ? fileUri.LocalPath : fileUri.ToString())} - {title}";
                    }
                }

                return title;
            }
        }

        public bool IsIdle
        {
            get
            {
                return _isIdle;
            }
            set
            {
                _isIdle = value;
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsEngineThinking));

                NewGame.NotifyCanExecuteChanged();
                LoadGame.NotifyCanExecuteChanged();
                SaveGame.NotifyCanExecuteChanged();

                PlayTarget.NotifyCanExecuteChanged();
                Pass.NotifyCanExecuteChanged();
                UndoLastMove.NotifyCanExecuteChanged();

                MoveToStart.NotifyCanExecuteChanged();
                MoveBack.NotifyCanExecuteChanged();
                MoveForward.NotifyCanExecuteChanged();
                MoveToEnd.NotifyCanExecuteChanged();

                SwitchToPlayMode.NotifyCanExecuteChanged();
                ShowGameMetadata.NotifyCanExecuteChanged();
                SwitchToReviewMode.NotifyCanExecuteChanged();

                FindBestMove.NotifyCanExecuteChanged();
                PlayBestMove.NotifyCanExecuteChanged();
                ShowEngineOptions.NotifyCanExecuteChanged();
                ShowViewerConfig.NotifyCanExecuteChanged();

                CopyHistoryToClipboard.NotifyCanExecuteChanged();
                CheckForUpdatesAsync.NotifyCanExecuteChanged();

                if (value)
                {
                    TryFinishPlayBest();
                }
            }
        }
        private bool _isIdle = true;

        public bool IsBusy
        {
            get
            {
                return !IsIdle;
            }
        }

        public bool IsRunningTimedCommand
        {
            get
            {
                return _isRunningTimeCommand;
            }
            private set
            {
                _isRunningTimeCommand = value;
                OnPropertyChanged(nameof(IsRunningTimedCommand));
                OnPropertyChanged(nameof(IsRunningIndeterminateCommand));
                OnPropertyChanged(nameof(IsEngineThinking));
            }
        }
        private bool _isRunningTimeCommand = false;

        // The timed-progress flag can be left "running" after a search ends early (the progress
        // task may report once more after it was stopped), so also require the engine to be busy.
        public bool IsEngineThinking => (IsBusy && IsRunningTimedCommand) || _playBestRunning;

        public bool IsRunningIndeterminateCommand
        {
            get
            {
                return !IsRunningTimedCommand;
            }
        }

        public double TimedCommandProgress
        {
            get
            {
                return _timedCommandProgress;
            }
            private set
            {
                _timedCommandProgress = Math.Max(0.0, Math.Min(100, value * 100));
                OnPropertyChanged(nameof(TimedCommandProgress));
            }
        }
        private double _timedCommandProgress = 0.0;

        public static bool IsPlayMode
        {
            get
            {
                return AppVM.EngineWrapper.CurrentGameSettings is null || AppVM.EngineWrapper.CurrentGameSettings.GameMode == GameMode.Play;
            }
        }

        public static bool IsReviewMode
        {
            get
            {
                return AppVM.EngineWrapper.CurrentGameSettings is not null && AppVM.EngineWrapper.CurrentGameSettings.GameMode == GameMode.Review;
            }
        }

        public static bool ShowMenu => AppInfo.IsWindows || AppInfo.IsLinux;

        public static ViewerConfig ViewerConfig => AppVM.ViewerConfig;

        public static Board Board
        {
            get
            {
                return AppVM.EngineWrapper.Board;
            }
        }

        public static bool BoardIsLoaded
        {
            get
            {
                return Board is not null;
            }
        }

        public static Board ReviewBoard
        {
            get
            {
                return AppVM.EngineWrapper.ReviewBoard;
            }
        }

        #region Peek

        // In play mode the user can browse earlier positions (like a chess GUI) without
        // touching the live game or the engine. The peek board is a local reconstruction.
        private static Board _peekBoard = null;

        public static bool IsPeeking => _peekBoard is not null;

        // The board the renderer should draw: the peeked position, or the live one.
        public static Board DisplayBoard => _peekBoard ?? Board;

        private static int LiveMoveCount => Board?.BoardHistory.Count ?? 0;

        private static int DisplayMoveCount => IsPeeking ? _peekBoard.BoardHistory.Count : LiveMoveCount;

        public string PeekStatusText => IsPeeking ? $"Viewing move {DisplayMoveCount} of {LiveMoveCount}" : "";

        private bool _updatingPeekHistory = false;

        internal void PeekTo(int moveCount)
        {
            Board live = Board;
            if (live is null)
            {
                return;
            }

            moveCount = Math.Clamp(moveCount, 0, live.BoardHistory.Count);

            if (moveCount == DisplayMoveCount)
            {
                return;
            }

            if (moveCount == live.BoardHistory.Count)
            {
                _peekBoard = null;
            }
            else
            {
                Board peek = new Board(live.GameType);
                for (int i = 0; i < moveCount; i++)
                {
                    BoardHistoryItem item = live.BoardHistory[i];
                    peek.Play(item.Move, item.MoveString);
                }
                _peekBoard = peek;
            }

            OnPeekChanged();
        }

        internal void ExitPeek()
        {
            if (IsPeeking)
            {
                _peekBoard = null;
                OnPeekChanged();
            }
        }

        private void OnPeekChanged()
        {
            // The bar and hint follow whatever position is on screen. Reset them before the
            // redraw so a stale hint is never drawn on the new position.
            RequestEvaluation();

            // Redraws the board (the renderer listens for Board changes and draws DisplayBoard).
            OnPropertyChanged(nameof(Board));
            OnPropertyChanged(nameof(IsPeeking));
            OnPropertyChanged(nameof(PeekStatusText));

            if (IsPlayMode && BoardHistory is not null)
            {
                _updatingPeekHistory = true;
                BoardHistory.CurrentMoveIndex = DisplayMoveCount - 1;
                _updatingPeekHistory = false;
            }

            MoveToStart.NotifyCanExecuteChanged();
            MoveBack.NotifyCanExecuteChanged();
            MoveForward.NotifyCanExecuteChanged();
            MoveToEnd.NotifyCanExecuteChanged();
        }

        private void PlayHistory_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Clicking a move in the list during play peeks at that position.
            if (!_updatingPeekHistory && e.PropertyName == nameof(ObservableBoardHistory.CurrentMoveIndex) && IsPlayMode)
            {
                PeekTo(BoardHistory.CurrentMoveIndex + 1);
            }
        }

        #endregion

        public ObservableBoardHistory BoardHistory
        {
            get
            {
                return _boardHistory;
            }
            private set
            {
                if (_boardHistory is not null)
                {
                    _boardHistory.PropertyChanged -= MoveList_HistoryPropertyChanged;
                }

                _boardHistory = value;

                if (_boardHistory is not null)
                {
                    _boardHistory.PropertyChanged += MoveList_HistoryPropertyChanged;
                }

                OnPropertyChanged(nameof(BoardHistory));
                CopyHistoryToClipboard.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CurrentMoveCommentary));
                RebuildMoveRows();
            }
        }
        private ObservableBoardHistory _boardHistory = null;

        #region Move list & player panel (presentation only)

        // Chess-style two-column view of BoardHistory. Purely a projection: selecting a move just
        // sets BoardHistory.CurrentMoveIndex, which the existing peek/review logic already handles.
        public ObservableCollection<MoveListRow> MoveRows { get; } = new ObservableCollection<MoveListRow>();

        public bool HasMoves => MoveRows.Count > 0;

        public int CurrentMoveRowIndex { get; private set; } = -1;

        private void RebuildMoveRows()
        {
            MoveRows.Clear();

            Mzinga.Core.BoardHistory history = BoardHistory?.BoardHistory;
            if (history is not null)
            {
                for (int i = 0; i < history.Count; i += 2)
                {
                    MovePly white = new MovePly(i, history[i].MoveString, JumpToMove);
                    MovePly black = i + 1 < history.Count ? new MovePly(i + 1, history[i + 1].MoveString, JumpToMove) : null;
                    MoveRows.Add(new MoveListRow(i / 2, white, black));
                }
            }

            OnPropertyChanged(nameof(HasMoves));
            UpdateMoveRowHighlights();
        }

        private void UpdateMoveRowHighlights()
        {
            int current = BoardHistory?.CurrentMoveIndex ?? -1;

            foreach (MoveListRow row in MoveRows)
            {
                Highlight(row.White);
                Highlight(row.Black);
            }

            CurrentMoveRowIndex = current >= 0 ? current / 2 : -1;
            OnPropertyChanged(nameof(CurrentMoveRowIndex));

            void Highlight(MovePly ply)
            {
                if (ply is not null)
                {
                    ply.IsCurrent = ply.Index == current;
                    ply.IsFuture = ply.Index > current;
                }
            }
        }

        private void MoveList_HistoryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ObservableBoardHistory.CurrentMoveIndex))
            {
                UpdateMoveRowHighlights();
            }
        }

        private void JumpToMove(int index)
        {
            // Review navigation goes through the engine, so it needs to be idle; play-mode peeking doesn't.
            if (BoardHistory is not null && (IsPlayMode || IsIdle))
            {
                BoardHistory.CurrentMoveIndex = index;
            }
        }

        public static string WhitePlayerName => GetPlayerName(PlayerColor.White);

        public static string BlackPlayerName => GetPlayerName(PlayerColor.Black);

        private static string GetPlayerName(PlayerColor color)
        {
            GameSettings settings = AppVM.EngineWrapper.CurrentGameSettings;
            if (settings is null || Board is null)
            {
                return "—";
            }

            PlayerType self = color == PlayerColor.White ? settings.WhitePlayerType : settings.BlackPlayerType;
            PlayerType other = color == PlayerColor.White ? settings.BlackPlayerType : settings.WhitePlayerType;

            if (self == PlayerType.EngineAI)
            {
                return "Mzinga AI";
            }

            return other == PlayerType.EngineAI ? "You" : "Human";
        }

        public static bool IsWhiteToMove => Board is not null && Board.GameInProgress && Board.CurrentColor == PlayerColor.White;

        public static bool IsBlackToMove => Board is not null && Board.GameInProgress && Board.CurrentColor == PlayerColor.Black;

        public static string ModeLabel => IsReviewMode ? "REVIEW" : "PLAY";

        private void NotifyPlayerPanel()
        {
            OnPropertyChanged(nameof(WhitePlayerName));
            OnPropertyChanged(nameof(BlackPlayerName));
            OnPropertyChanged(nameof(IsWhiteToMove));
            OnPropertyChanged(nameof(IsBlackToMove));
            OnPropertyChanged(nameof(ModeLabel));
        }

        #endregion

        public RelayCommand CopyHistoryToClipboard
        {
            get
            {
                return _copyHistoryToClipboard ??= new RelayCommand(() =>
                {
                    try
                    {
                        AppVM.TextToClipboard(BoardHistory.Text);
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsIdle && (BoardHistory is not null) && BoardHistory.CurrentMoveIndex >= 0;
                });
            }
        }
        private RelayCommand _copyHistoryToClipboard = null;

        public Action RequestClose;

        #region Status properties

        public static string GameState
        {
            get
            {
                string state = "A game has not been started.";

                if (Board is not null)
                {
                    switch (Board.BoardState)
                    {
                        case BoardState.Draw:
                            state = "The game is a draw.";
                            break;
                        case BoardState.WhiteWins:
                            state = "White has won the game.";
                            break;
                        case BoardState.BlackWins:
                            state = "Black has won the game.";
                            break;
                        default:
                            state = (Board.CurrentColor == PlayerColor.White) ? "It's white's turn." : "It's black's turn.";
                            break;
                    }
                }

                return state;
            }
        }

        public static string ValidMoves
        {
            get
            {
                string moves = "";
                if (AppVM.EngineWrapper.ValidMoves is not null)
                {
                    moves = AppVM.EngineWrapper.ValidMoves.Count.ToString();
                }

                return moves;
            }
        }

        public static string TargetMove
        {
            get
            {
                var targetMove = AppVM.EngineWrapper.TargetMove;
                if (targetMove.HasValue && Board.TryGetMoveString(targetMove.Value, out string move))
                {
                    return move;
                }

                var targetPiece = AppVM.EngineWrapper.TargetPiece;
                if (targetPiece != PieceName.INVALID)
                {
                    return targetPiece.ToString();
                }

                return "";
            }
        }

        #endregion

        #region Canvas properties

        public double CanvasHexRadius
        {
            get
            {
                return _canvasHexRadius;
            }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _canvasHexRadius = value;
                OnPropertyChanged(nameof(CanvasHexRadius));
            }
        }
        private double _canvasHexRadius = 20;

        public double CanvasCursorX
        {
            get
            {
                return _canvasCursorX;
            }
            private set
            {
                _canvasCursorX = value;
                OnPropertyChanged(nameof(CanvasCursorX));
            }
        }
        private double _canvasCursorX;

        public double CanvasCursorY
        {
            get
            {
                return _canvasCursorY;
            }
            private set
            {
                _canvasCursorY = value;
                OnPropertyChanged(nameof(CanvasCursorY));
            }
        }
        private double _canvasCursorY;

        public bool CanCenterBoard
        {
            get
            {
                return (AppVM.EngineWrapper.GameInProgress || AppVM.EngineWrapper.GameIsOver) && !ViewerConfig.AutoCenterBoard;
            }
        }

        public bool CanRaiseStackedPieces
        {
            get
            {
                return _canRaiseStackedPieces;
            }
            internal set
            {
                _canRaiseStackedPieces = value;
                OnPropertyChanged(nameof(CanRaiseStackedPieces));
            }
        }
        private bool _canRaiseStackedPieces = false;

        public bool CanZoomBoard
        {
            get
            {
                return (AppVM.EngineWrapper.GameInProgress || AppVM.EngineWrapper.GameIsOver) && !ViewerConfig.AutoZoomBoard;
            }
        }

        #endregion

        #region File

        public RelayCommand NewGame
        {
            get
            {
                return _newGame ??= new RelayCommand(() =>
                {
                    try
                    {
                        StrongReferenceMessenger.Default.Send(new NewGameMessage(AppVM.EngineWrapper.CurrentGameSettings, true, (settings) =>
                        {
                            try
                            {
                                AppVM.EngineWrapper.NewGame(settings);
                                OnPropertyChanged(nameof(Title));
                            }
                            catch (Exception ex)
                            {
                                ExceptionUtils.HandleException(ex);
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, ()=>
                {
                    return IsIdle;
                });
            }
        }
        private RelayCommand _newGame = null;

        public RelayCommand LoadGame
        {
            get
            {
                return _loadGame ??= new RelayCommand(() =>
                {
                    try
                    {
                        IsIdle = false;
                        StrongReferenceMessenger.Default.Send(new LoadGameMessage((gameRecording) =>
                        {
                            try
                            {
                                if (gameRecording is not null)
                                {
                                    AppVM.EngineWrapper.LoadGame(gameRecording);
                                    OnPropertyChanged(nameof(Title));
                                }
                            }
                            catch (Exception ex)
                            {
                                ExceptionUtils.HandleException(ex);
                            }
                            finally
                            {
                                IsIdle = true;
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsIdle;
                });
            }
        }
        private RelayCommand _loadGame = null;

        public RelayCommand SaveGame
        {
            get
            {
                return _saveGame ??= new RelayCommand(() =>
                {
                    try
                    {
                        StrongReferenceMessenger.Default.Send(new GameMetadataMessage(AppVM.EngineWrapper.CurrentGameSettings.Metadata, (metadata) =>
                        {
                            try
                            {
                                AppVM.EngineWrapper.CurrentGameSettings.Metadata.Clear();
                                AppVM.EngineWrapper.CurrentGameSettings.Metadata.CopyFrom(metadata);

                                StrongReferenceMessenger.Default.Send(new SaveGameMessage(AppVM.EngineWrapper.CurrentGameSettings.GameRecording, (fileUri) =>
                                {
                                    if (fileUri is not null && IsReviewMode)
                                    {
                                        AppVM.EngineWrapper.CurrentGameSettings.GameRecording.FileUri = fileUri;
                                        OnPropertyChanged(nameof(Title));
                                    }
                                }));
                            }
                            catch (Exception ex)
                            {
                                ExceptionUtils.HandleException(ex);
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsIdle && (AppVM.EngineWrapper.GameInProgress || AppVM.EngineWrapper.GameIsOver);
                });
            }
        }
        private RelayCommand _saveGame = null;

        public RelayCommand Close
        {
            get
            {
                return _close ??= new RelayCommand(() =>
                {
                    try
                    {
                        RequestClose?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                });
            }
        }
        private RelayCommand _close;

        #endregion

        #region Play Mode

        public RelayCommand PlayTarget
        {
            get
            {
                return _playTarget ??= new RelayCommand(() =>
                {
                    try
                    {
                        AppVM.EngineWrapper.PlayTargetMove();
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsIdle && AppVM.EngineWrapper.GameInProgress && ViewerConfig.RequireMoveConfirmation && (!ViewerConfig.BlockInvalidMoves || AppVM.EngineWrapper.CanPlayTargetMove) && IsPlayMode;
                });
            }
        }
        private RelayCommand _playTarget = null;

        public RelayCommand Pass
        {
            get
            {
                return _pass ??= new RelayCommand(() =>
                {
                    try
                    {
                        AppVM.EngineWrapper.Pass();
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsIdle && AppVM.EngineWrapper.GameInProgress && ViewerConfig.RequireMoveConfirmation && (!ViewerConfig.BlockInvalidMoves || AppVM.EngineWrapper.CanPass) && IsPlayMode;
                });
            }
        }
        private RelayCommand _pass = null;

        public RelayCommand UndoLastMove
        {
            get
            {
                return _undoLastMove ??= new RelayCommand(() =>
                {
                    try
                    {
                        AppVM.EngineWrapper.UndoLastMove();
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsIdle && AppVM.EngineWrapper.CanUndoLastMove;
                });
            }
        }
        private RelayCommand _undoLastMove = null;

        #endregion

        #region Review Mode

        public string CurrentMoveCommentary
        {
            get
            {
                return AppVM.EngineWrapper.CurrentGameSettings?.Metadata.GetMoveCommentary(BoardHistory.CurrentMoveIndex + 1);
            }
            set
            {
                if (AppVM.EngineWrapper.CurrentGameSettings is not null)
                {
                    AppVM.EngineWrapper.CurrentGameSettings?.Metadata.SetMoveCommentary(BoardHistory.CurrentMoveIndex + 1, value);
                    OnPropertyChanged(nameof(CurrentMoveCommentary));
                }
            }
        }

        public RelayCommand MoveToStart
        {
            get
            {
                return _moveToStart ??= new RelayCommand(() =>
                {
                    try
                    {
                        if (IsPlayMode)
                        {
                            PeekTo(0);
                        }
                        else
                        {
                            AppVM.EngineWrapper.MoveToStart();
                        }
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsPlayMode ? DisplayMoveCount > 0 : IsIdle && AppVM.EngineWrapper.CanMoveBack;
                });
            }
        }
        private RelayCommand _moveToStart = null;

        public RelayCommand MoveBack
        {
            get
            {
                return _moveBack ??= new RelayCommand(() =>
                {
                    try
                    {
                        if (IsPlayMode)
                        {
                            PeekTo(DisplayMoveCount - 1);
                        }
                        else
                        {
                            AppVM.EngineWrapper.MoveBack();
                        }
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsPlayMode ? DisplayMoveCount > 0 : IsIdle && AppVM.EngineWrapper.CanMoveBack;
                });
            }
        }
        private RelayCommand _moveBack = null;

        public RelayCommand MoveForward
        {
            get
            {
                return _moveForward ??= new RelayCommand(() =>
                {
                    try
                    {
                        if (IsPlayMode)
                        {
                            PeekTo(DisplayMoveCount + 1);
                        }
                        else
                        {
                            AppVM.EngineWrapper.MoveForward();
                        }
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsPlayMode ? IsPeeking : IsIdle && AppVM.EngineWrapper.CanMoveForward;
                });
            }
        }
        private RelayCommand _moveForward = null;

        public RelayCommand MoveToEnd
        {
            get
            {
                return _moveToEnd ??= new RelayCommand(() =>
                {
                    try
                    {
                        if (IsPlayMode)
                        {
                            ExitPeek();
                        }
                        else
                        {
                            AppVM.EngineWrapper.MoveToEnd();
                        }
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsPlayMode ? IsPeeking : IsIdle && AppVM.EngineWrapper.CanMoveForward;
                });
            }
        }
        private RelayCommand _moveToEnd = null;

        public RelayCommand ShowGameMetadata
        {
            get
            {
                return _showGameMetadata ??= new RelayCommand(() =>
                {
                    try
                    {
                        StrongReferenceMessenger.Default.Send(new GameMetadataMessage(AppVM.EngineWrapper.CurrentGameSettings.Metadata, (metadata) =>
                        {
                            try
                            {
                                AppVM.EngineWrapper.CurrentGameSettings.Metadata.Clear();
                                AppVM.EngineWrapper.CurrentGameSettings.Metadata.CopyFrom(metadata);
                            }
                            catch (Exception ex)
                            {
                                ExceptionUtils.HandleException(ex);
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsIdle && IsReviewMode;
                });
            }
        }
        private RelayCommand _showGameMetadata = null;

        public RelayCommand SwitchToPlayMode
        {
            get
            {
                return _switchToPlayMode ??= new RelayCommand(() =>
                {
                    try
                    {
                        StrongReferenceMessenger.Default.Send(new ConfirmationMessage("Switching to play mode starts a new game at the current position. Do you want to continue?", (confirmed) =>
                        {
                            try
                            {
                                if (confirmed)
                                {
                                    string activeGameString = Board.GetGameString();

                                    StrongReferenceMessenger.Default.Send(new NewGameMessage(AppVM.EngineWrapper.CurrentGameSettings, false, (settings) =>
                                    {
                                        try
                                        {
                                            AppVM.EngineWrapper.NewGame(settings, activeGameString);
                                        }
                                        catch (Exception ex)
                                        {
                                            ExceptionUtils.HandleException(ex);
                                        }
                                    }));
                                }
                            }
                            catch (Exception ex)
                            {
                                ExceptionUtils.HandleException(ex);
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsIdle && IsReviewMode && (AppVM.EngineWrapper.GameInProgress || AppVM.EngineWrapper.GameIsOver);
                });
            }
        }
        private RelayCommand _switchToPlayMode = null;

        public RelayCommand SwitchToReviewMode
        {
            get
            {
                return _switchToReviewMode ??= new RelayCommand(() =>
                {
                    try
                    {
                        if (AppVM.EngineWrapper.GameIsOver)
                        {
                            AppVM.EngineWrapper.SwitchToReviewMode();
                        }
                        else
                        {
                            StrongReferenceMessenger.Default.Send(new ConfirmationMessage("Switching to review mode will end your game. Do you want to continue?", (confirmed) =>
                            {
                                try
                                {
                                    if (confirmed)
                                    {
                                        AppVM.EngineWrapper.SwitchToReviewMode();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ExceptionUtils.HandleException(ex);
                                }
                            }));
                        }
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsIdle && IsPlayMode && (AppVM.EngineWrapper.GameInProgress || AppVM.EngineWrapper.GameIsOver);
                });
            }
        }
        private RelayCommand _switchToReviewMode = null;

        #endregion

        #region Engine

        public string EngineId => AppVM.EngineWrapper.ID;

        public RelayCommand FindBestMove
        {
            get
            {
                return _findBestMove ??= new RelayCommand(() =>
                {
                    try
                    {
                        _playBestPending = !ViewerConfig.RequireMoveConfirmation;
                        _playBestMoveCount = LiveMoveCount;
                        AppVM.EngineWrapper.FindBestMove();
                    }
                    catch (Exception ex)
                    {
                        _playBestPending = false;
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsIdle && AppVM.EngineWrapper.CanFindBestMove;
                });
            }
        }
        private RelayCommand _findBestMove = null;

        // "Play best": the found move is normally auto-played when it gets selected, but that
        // attempt can land while the engine is still finishing the search and be dropped. If the
        // move is still unplayed once the engine is idle again, play it then.
        private bool _playBestPending = false;
        private int _playBestMoveCount = 0;

        private void TryFinishPlayBest()
        {
            if (!_playBestPending)
            {
                return;
            }

            _playBestPending = false;

            try
            {
                if (LiveMoveCount == _playBestMoveCount && AppVM.EngineWrapper.CanPlayTargetMove)
                {
                    AppVM.EngineWrapper.PlayTargetMove();
                }
            }
            catch (Exception ex)
            {
                ExceptionUtils.HandleException(ex);
            }
        }

        public RelayCommand ShowEngineConsole
        {
            get
            {
                return _showEngineConsole ??= new RelayCommand(() =>
                {
                    try
                    {
                        StrongReferenceMessenger.Default.Send(new EngineConsoleMessage());
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                });
            }
        }
        private RelayCommand _showEngineConsole = null;

        public RelayCommand ShowEngineOptions
        {
            get
            {
                return _showEngineOptions ??= new RelayCommand(() =>
                {
                    try
                    {
                        AppVM.EngineWrapper.OptionsList(() =>
                        {
                            AppVM.DoOnUIThread(() =>
                            {
                                StrongReferenceMessenger.Default.Send(new EngineOptionsMessage(AppVM.EngineWrapper.EngineOptions, (changedOptions) =>
                                {
                                    try
                                    {
                                        AppVM.EngineWrapper.OptionsSet(changedOptions);
                                    }
                                    catch (Exception ex)
                                    {
                                        ExceptionUtils.HandleException(ex);
                                    }
                                }));
                            });
                        });
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsIdle;
                });
            }
        }
        private RelayCommand _showEngineOptions = null;

        #endregion

        #region Viewer

        public bool ShowBoardHistory
        {
            get
            {
                return ViewerConfig.ShowBoardHistory;
            }
            set
            {
                ViewerConfig.ShowBoardHistory = value;
                OnPropertyChanged(nameof(ShowBoardHistory));
            }
        }

        public bool ShowMoveCommentary
        {
            get
            {
                return ViewerConfig.ShowMoveCommentary;
            }
            set
            {
                ViewerConfig.ShowMoveCommentary = value;
                OnPropertyChanged(nameof(ShowMoveCommentary));
            }
        }

        public bool ShowEvaluationBar
        {
            get
            {
                return ViewerConfig.ShowEvaluationBar;
            }
            set
            {
                ViewerConfig.ShowEvaluationBar = value;
                OnPropertyChanged(nameof(ShowEvaluationBar));
                RequestEvaluation();
            }
        }

        #region Evaluation

        // The evaluation bar, best-move hint and forced-win readout are fed by a private GameAI
        // instance that analyzes the displayed position in the background after every change.
        // It never touches the playing engine.

        // Latest evaluation of the displayed position from White's perspective; null = unknown/even.
        private double? _evaluation = null;

        // Best move the analyzer found for the displayed position (null = none yet / pass).
        private Move? _bestMove = null;
        private string _bestMoveText = null;

        // Set when the analyzer proves a forced win: who wins and in how many of their moves.
        private PlayerColor? _forcedWinner = null;
        private int _forcedWinMoves = 0;

        public bool ShowBestMove
        {
            get => _showBestMove;
            set
            {
                if (SetProperty(ref _showBestMove, value))
                {
                    OnPropertyChanged(nameof(HintMove));
                    OnPropertyChanged(nameof(BestMoveText));
                    RequestEvaluation();
                }
            }
        }
        private bool _showBestMove = false;

        public RelayCommand ToggleShowBestMove => _toggleShowBestMove ??= new RelayCommand(() => ShowBestMove = !ShowBestMove);
        private RelayCommand _toggleShowBestMove = null;

        // What the board renderer should draw as a hint (only while the hint is switched on).
        public Move? HintMove => ShowBestMove ? _bestMove : null;

        public string BestMoveText => ShowBestMove ? (_bestMoveText ?? "thinking...") : "";

        public bool HasForcedWin => _forcedWinner.HasValue;

        public string ForcedWinText => _forcedWinner switch
        {
            PlayerColor.White => $"White wins in {_forcedWinMoves} (forced)",
            PlayerColor.Black => $"Black wins in {_forcedWinMoves} (forced)",
            _ => "",
        };

        // "#3"-style badge on the evaluation bar, shown at the winning side's end.
        public string ForcedWinBadge => _forcedWinner.HasValue ? $"#{_forcedWinMoves}" : "";

        public bool IsWhiteForcedWin => _forcedWinner == PlayerColor.White;

        public bool IsBlackForcedWin => _forcedWinner == PlayerColor.Black;

        private GameAI _evalAI = null;
        private GameType _evalAIGameType;
        private CancellationTokenSource _evalCTS = null;
        private Task _evalTask = Task.CompletedTask;
        private volatile int _evalRequestId = 0;

        // Height (0-100) of the White portion of the evaluation bar. 50 == even.
        public double EvaluationPercent
        {
            get
            {
                if (!_evaluation.HasValue)
                {
                    return 50.0;
                }

                double score = _evaluation.Value;

                if (double.IsPositiveInfinity(score))
                {
                    return 100.0;
                }

                if (double.IsNegativeInfinity(score))
                {
                    return 0.0;
                }

                // Mzinga scores span from a few thousand in the opening to close to a million in
                // the middle game. A linear squash either hides small edges or pegs the bar, so
                // compress logarithmically first: ~59% at 10k, ~77% at 100k, ~90% at 740k.
                // Only a proven forced win (infinity, above) fills the bar completely.
                double compressed = Math.Log(1.0 + (Math.Abs(score) / EvaluationScale)) / EvaluationLogDivisor;
                return 50.0 + (50.0 * Math.Sign(score) * Math.Tanh(compressed));
            }
        }

        private const double EvaluationScale = 10000.0;
        private const double EvaluationLogDivisor = 4.0;

        // How long the analyzer thinks per position. Independent of the opponent's difficulty.
        private static TimeSpan EvaluationMaxTime => TimeSpan.FromSeconds(ViewerConfig.AnalysisSeconds);

        private const double AnalysisFastSeconds = 0.5;
        private const double AnalysisStandardSeconds = 1.5;
        private const double AnalysisDeepSeconds = 5.0;

        public bool IsAnalysisFast => IsAnalysisSeconds(AnalysisFastSeconds);

        public bool IsAnalysisStandard => IsAnalysisSeconds(AnalysisStandardSeconds);

        public bool IsAnalysisDeep => IsAnalysisSeconds(AnalysisDeepSeconds);

        private static bool IsAnalysisSeconds(double seconds) => Math.Abs(ViewerConfig.AnalysisSeconds - seconds) < 0.01;

        // Parameter: "Fast", "Standard" or "Deep"
        public RelayCommand<string> SetAnalysisStrength => _setAnalysisStrength ??= new RelayCommand<string>((strength) =>
        {
            ViewerConfig.AnalysisSeconds = strength switch
            {
                "Fast" => AnalysisFastSeconds,
                "Deep" => AnalysisDeepSeconds,
                _ => AnalysisStandardSeconds,
            };

            OnPropertyChanged(nameof(IsAnalysisFast));
            OnPropertyChanged(nameof(IsAnalysisStandard));
            OnPropertyChanged(nameof(IsAnalysisDeep));
            RequestEvaluation();
        });
        private RelayCommand<string> _setAnalysisStrength = null;

        private GameAI GetEvaluationAI(GameType gameType)
        {
            if (_evalAI is null || _evalAIGameType != gameType)
            {
                _evalAI = AppVM.InternalEngineConfig.GetGameAI(gameType);
                _evalAIGameType = gameType;
            }

            return _evalAI;
        }

        #region Play best (uses the analyzer, not the opponent engine)

        // "Play best" plays the analyzer's best move at analysis strength, so it matches the hint
        // and is not weakened when playing against an Easy opponent.
        public RelayCommand PlayBestMove => _playBestMove ??= new RelayCommand(async () => await PlayBestMoveAsync(), CanPlayBestMove);
        private RelayCommand _playBestMove = null;

        private bool _playBestRunning = false;

        private bool CanPlayBestMove()
        {
            return IsIdle && !_playBestRunning && IsPlayMode && AppVM.EngineWrapper.GameInProgress && AppVM.EngineWrapper.CurrentTurnIsHuman;
        }

        private async Task PlayBestMoveAsync()
        {
            SetPlayBestRunning(true);
            try
            {
                ExitPeek();

                Board live = Board?.Clone();
                if (live is null || live.GameIsOver)
                {
                    return;
                }

                int moveCount = live.BoardHistory.Count;

                // Use the background analysis of the live position, so the move played is exactly the
                // move the hint shows. Start that analysis if it isn't running (hint and bar hidden).
                if (_searchRequestId != _evalRequestId)
                {
                    RequestEvaluation(force: true);
                }

                int requestId = _evalRequestId;
                if (_completedRequestId != requestId)
                {
                    try
                    {
                        await _evalTask;
                    }
                    catch (Exception) { }
                }

                // Only play if the analysis finished for this exact position and nothing changed meanwhile.
                Move? best = _completedRequestId == requestId ? _completedBestMove : null;
                if (best.HasValue && LiveMoveCount == moveCount && IsPlayMode && AppVM.EngineWrapper.CurrentTurnIsHuman)
                {
                    if (best.Value == Move.PassMove)
                    {
                        AppVM.EngineWrapper.Pass();
                    }
                    else if (live.TryGetMoveString(best.Value, out string moveString))
                    {
                        AppVM.EngineWrapper.SendCommand("play {0}", moveString);
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionUtils.HandleException(ex);
            }
            finally
            {
                SetPlayBestRunning(false);
            }
        }

        private void SetPlayBestRunning(bool running)
        {
            _playBestRunning = running;
            OnPropertyChanged(nameof(IsEngineThinking));
            PlayBestMove.NotifyCanExecuteChanged();
        }

        #endregion

        // force: analyze even if the evaluation bar and hint are hidden (used by "Play best").
        private void RequestEvaluation(bool force = false)
        {
            int requestId = ++_evalRequestId;
            _evalCTS?.Cancel();

            // Whatever we knew belongs to the previous position.
            ClearAnalysis();

            Board board = DisplayBoard?.Clone();

            if (board is null || !(ShowEvaluationBar || ShowBestMove || force))
            {
                SetEvaluation(null, requestId);
                return;
            }

            if (board.GameIsOver)
            {
                SetEvaluation(board.BoardState switch
                {
                    BoardState.WhiteWins => double.PositiveInfinity,
                    BoardState.BlackWins => double.NegativeInfinity,
                    _ => 0.0,
                }, requestId);
                return;
            }

            if (board.BoardState == BoardState.NotStarted)
            {
                // No evaluation for an empty board, but a hint for the opening placement is still useful.
                SetEvaluation(null, requestId);
                if (!ShowBestMove && !force)
                {
                    return;
                }
            }

            CancellationTokenSource cts = new CancellationTokenSource(EvaluationMaxTime);
            _evalCTS = cts;
            _searchRequestId = requestId;

            // On your turn the analyzer uses as many cores as the opponent engine, so at the same time
            // setting it searches like the opponent does. While the opponent is thinking it stays
            // on a single core so it doesn't slow the opponent down (the bar still updates after your move).
            bool opponentThinking = IsPlayMode && AppVM.EngineWrapper.CurrentTurnIsEngineAI;
            int helperThreads = opponentThinking ? 0 : AppVM.InternalEngineConfig.MaxHelperThreads;

            // Chain onto the previous search: a GameAI instance must only run one search at a time.
            Task previous = _evalTask;
            _evalTask = Task.Run(async () =>
            {
                try
                {
                    await previous;
                }
                catch (Exception) { }

                if (requestId != _evalRequestId)
                {
                    return; // Superseded by a newer position
                }

                try
                {
                    await EvaluateAsync(board, requestId, helperThreads, cts.Token);
                }
                catch (Exception)
                {
                    // Evaluation is best-effort; never disturb the game over it.
                }
            });
        }

        private async Task EvaluateAsync(Board board, int requestId, int helperThreads, CancellationToken token)
        {
            GameAI ai = GetEvaluationAI(board.GameType);
            PlayerColor toMove = board.CurrentColor;
            bool evaluateScore = board.BoardState != BoardState.NotStarted;
            double? lastScore = null;
            double? previousEvenScore = null;
            bool reportedEvenDepth = false;

            // The search plays/undoes moves on 'board', so format move strings on an untouched copy.
            Board notationBoard = board.Clone();

            void OnFound(object sender, BestMoveFoundEventArgs e)
            {
                if (requestId != _evalRequestId)
                {
                    return;
                }

                string moveText = e.Move == Move.PassMove ? "pass" : (notationBoard.TryGetMoveString(e.Move, out string s) ? s : null);
                SetBestMove(e.Move == Move.PassMove ? null : e.Move, moveText, requestId);

                // Unevaluated moves report double.MinValue; ignore those scores.
                if (!evaluateScore || e.Score == double.MinValue || double.IsNaN(e.Score))
                {
                    return;
                }

                // Scores are from the side-to-move's view; flip to White's.
                double white = toMove == PlayerColor.White ? e.Score : -e.Score;
                lastScore = white;

                if (double.IsInfinity(e.Score))
                {
                    // Forced win/loss found at search depth e.Depth (in plies). Convert to the
                    // number of moves the winner needs, counting the winning move itself.
                    bool sideToMoveWins = double.IsPositiveInfinity(e.Score);
                    PlayerColor winner = sideToMoveWins ? toMove : (toMove == PlayerColor.White ? PlayerColor.Black : PlayerColor.White);
                    int moves = sideToMoveWins ? (e.Depth + 1) / 2 : Math.Max(1, e.Depth / 2);

                    reportedEvenDepth = true;
                    SetEvaluation(white, requestId);
                    SetForcedWin(winner, Math.Max(1, moves), requestId);
                }
                else if (e.Depth % 2 == 0)
                {
                    // Odd depths are biased toward the side that moved last, so only even depths
                    // count. Mzinga's even depths can still disagree wildly (e.g. +64k then -59k
                    // in the opening), so show the average of the last two to calm the bar.
                    reportedEvenDepth = true;
                    double shown = previousEvenScore.HasValue ? (previousEvenScore.Value + white) / 2 : white;
                    previousEvenScore = white;
                    SetEvaluation(shown, requestId);
                }
            }

            Move result;
            ai.BestMoveFound += OnFound;
            try
            {
                result = await ai.GetBestMoveAsync(board, helperThreads, token);
            }
            finally
            {
                ai.BestMoveFound -= OnFound;
            }

            if (!reportedEvenDepth && lastScore.HasValue)
            {
                SetEvaluation(lastScore, requestId);
            }

            // Remember the final answer so "Play best" plays exactly the move the hint ends on.
            if (requestId == _evalRequestId)
            {
                _completedBestMove = result;
                _completedRequestId = requestId;
            }
        }

        // Final best move of the most recent finished analysis, and which request it belongs to.
        private Move? _completedBestMove = null;
        private volatile int _completedRequestId = -1;

        // The request id that has a background search scheduled (vs. one that returned early).
        private int _searchRequestId = -1;

        private void ClearAnalysis()
        {
            _bestMove = null;
            _bestMoveText = null;
            _forcedWinner = null;
            _forcedWinMoves = 0;
            OnAnalysisChanged();
        }

        private void SetEvaluation(double? value, int requestId)
        {
            AppVM.DoOnUIThread(() =>
            {
                if (requestId == _evalRequestId)
                {
                    _evaluation = value;
                    OnPropertyChanged(nameof(EvaluationPercent));
                }
            });
        }

        private void SetBestMove(Move? move, string moveText, int requestId)
        {
            AppVM.DoOnUIThread(() =>
            {
                if (requestId == _evalRequestId && (_bestMove != move || _bestMoveText != moveText))
                {
                    _bestMove = move;
                    _bestMoveText = moveText;
                    OnAnalysisChanged();
                }
            });
        }

        private void SetForcedWin(PlayerColor winner, int moves, int requestId)
        {
            AppVM.DoOnUIThread(() =>
            {
                if (requestId == _evalRequestId)
                {
                    _forcedWinner = winner;
                    _forcedWinMoves = moves;
                    OnAnalysisChanged();
                }
            });
        }

        private void OnAnalysisChanged()
        {
            OnPropertyChanged(nameof(HintMove));
            OnPropertyChanged(nameof(BestMoveText));
            OnPropertyChanged(nameof(HasForcedWin));
            OnPropertyChanged(nameof(ForcedWinText));
            OnPropertyChanged(nameof(ForcedWinBadge));
            OnPropertyChanged(nameof(IsWhiteForcedWin));
            OnPropertyChanged(nameof(IsBlackForcedWin));
        }

        #endregion

        public bool AutoCenterBoard
        {
            get
            {
                return ViewerConfig.AutoCenterBoard;
            }
            set
            {
                ViewerConfig.AutoCenterBoard = value;
                OnPropertyChanged(nameof(AutoCenterBoard));
                OnPropertyChanged(nameof(CanCenterBoard));
            }
        }

        public bool AutoZoomBoard
        {
            get
            {
                return ViewerConfig.AutoZoomBoard;
            }
            set
            {
                ViewerConfig.AutoZoomBoard = value;
                OnPropertyChanged(nameof(AutoZoomBoard));
                OnPropertyChanged(nameof(CanZoomBoard));
            }
        }

        public RelayCommand ToggleShowBoardHistory
        {
            get
            {
                return _toggleShowBoardHistory ??= new RelayCommand(() =>
                {
                    try
                    {
                        ShowBoardHistory = !ShowBoardHistory;
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                });
            }
        }
        private RelayCommand _toggleShowBoardHistory = null;

        public RelayCommand ToggleShowMoveCommentary
        {
            get
            {
                return _toggleShowMoveCommentary ??= new RelayCommand(() =>
                {
                    try
                    {
                        ShowMoveCommentary = !ShowMoveCommentary;
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                });
            }
        }
        private RelayCommand _toggleShowMoveCommentary = null;

        public RelayCommand ToggleShowEvaluationBar
        {
            get
            {
                return _toggleShowEvaluationBar ??= new RelayCommand(() =>
                {
                    try
                    {
                        ShowEvaluationBar = !ShowEvaluationBar;
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                });
            }
        }
        private RelayCommand _toggleShowEvaluationBar = null;

        public RelayCommand ToggleAutoCenterBoard
        {
            get
            {
                return _toggleAutoCenterBoard ??= new RelayCommand(() =>
                {
                    try
                    {
                        AutoCenterBoard = !AutoCenterBoard;
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                });
            }
        }
        private RelayCommand _toggleAutoCenterBoard = null;

        public RelayCommand ToggleAutoZoomBoard
        {
            get
            {
                return _toggleAutoZoomBoard ??= new RelayCommand(() =>
                {
                    try
                    {
                        AutoZoomBoard = !AutoZoomBoard;
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                });
            }
        }
        private RelayCommand _toggleAutoZoomBoard = null;

        public RelayCommand ShowViewerConfig
        {
            get
            {
                return _showViewerConfig ??= new RelayCommand(() =>
                {
                    try
                    {
                        StrongReferenceMessenger.Default.Send(new ViewerConfigMessage(ViewerConfig, (config) =>
                        {
                            try
                            {
                                ViewerConfig.CopyFrom(config);

                                OnPropertyChanged(nameof(ViewerConfig));
                                OnPropertyChanged(nameof(TargetMove));

                                PlayTarget.NotifyCanExecuteChanged();
                                Pass.NotifyCanExecuteChanged();

                                UpdateBoardHistory();
                            }
                            catch (Exception ex)
                            {
                                ExceptionUtils.HandleException(ex);
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        ExceptionUtils.HandleException(ex);
                    }
                }, () =>
                {
                    return IsIdle;
                });
            }
        }
        private RelayCommand _showViewerConfig = null;

        #endregion

        #region Help

        public static RelayCommand ShowLicenses => AppVM.ShowLicenses;

        public static RelayCommand LaunchHiveWebsite => AppVM.LaunchHiveWebsite;

        public static RelayCommand LaunchMzingaWebsite => AppVM.LaunchMzingaWebsite;

        public static bool CheckForUpdatesEnabled => AppViewModel.CheckForUpdatesEnabled;

        public static RelayCommand CheckForUpdatesAsync => AppVM.CheckForUpdatesAsync;

        #endregion

        public MainViewModel()
        {
            AppVM.EngineWrapper.BoardUpdated += (sender, args) =>
            {
                AppVM.DoOnUIThread(() =>
                {
                    // Any change to the live game snaps the view back to the live position.
                    _peekBoard = null;
                    OnPropertyChanged(nameof(IsPeeking));
                    OnPropertyChanged(nameof(PeekStatusText));

                    RequestEvaluation();
                    NotifyPlayerPanel();
                    PlayBestMove.NotifyCanExecuteChanged();

                    OnPropertyChanged(nameof(Board));
                    OnPropertyChanged(nameof(BoardIsLoaded));
                    SaveGame.NotifyCanExecuteChanged();

                    PlayTarget.NotifyCanExecuteChanged();
                    Pass.NotifyCanExecuteChanged();
                    UndoLastMove.NotifyCanExecuteChanged();

                    MoveToStart.NotifyCanExecuteChanged();
                    MoveBack.NotifyCanExecuteChanged();
                    MoveForward.NotifyCanExecuteChanged();
                    MoveToEnd.NotifyCanExecuteChanged();

                    FindBestMove.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(GameState));

                    OnPropertyChanged(nameof(CanCenterBoard));
                    OnPropertyChanged(nameof(CanZoomBoard));

                    if (AppVM.EngineWrapper.GameIsOver && AppVM.EngineWrapper.CurrentGameSettings.GameMode == GameMode.Play)
                    {
                        if (ViewerConfig.PlaySoundEffects)
                        {
                            SoundUtils.PlaySound(GameSound.GameOver);
                        }

                        switch (Board.BoardState)
                        {
                            case BoardState.WhiteWins:
                                StrongReferenceMessenger.Default.Send(new InformationMessage("White has won the game.", "Game Over"));
                                break;
                            case BoardState.BlackWins:
                                StrongReferenceMessenger.Default.Send(new InformationMessage("Black has won the game.", "Game Over"));
                                break;
                            case BoardState.Draw:
                                StrongReferenceMessenger.Default.Send(new InformationMessage("The game is a draw.", "Game Over"));
                                break;
                        }
                    }

                    UpdateBoardHistory();
                });
            };

            AppVM.EngineWrapper.ValidMovesUpdated += (sender, args) =>
            {
                AppVM.DoOnUIThread(() =>
                {
                    OnPropertyChanged(nameof(ValidMoves));
                });
            };

            AppVM.EngineWrapper.TargetMoveUpdated += (sender, args) =>
            {
                AppVM.DoOnUIThread(() =>
                {
                    OnPropertyChanged(nameof(TargetMove));
                    PlayTarget.NotifyCanExecuteChanged();

                    if (AppVM.EngineWrapper.CurrentTurnIsHuman && IsPlayMode && !ViewerConfig.RequireMoveConfirmation)
                    {
                        try
                        {
                            if (AppVM.EngineWrapper.TargetMove is not null)
                            {
                                // Only fast-play if a move is selected
                                AppVM.EngineWrapper.PlayTargetMove();
                            }
                            else if (AppVM.EngineWrapper.CanPass)
                            {
                                // Only fast-pass if pass is available
                                AppVM.EngineWrapper.Pass();
                            }
                        }
                        catch (Exception ex)
                        {
                            ExceptionUtils.HandleException(ex);
                        }
                    }
                });
            };

            AppVM.EngineWrapper.IsIdleUpdated += (sender, args) =>
            {
                AppVM.DoOnUIThread(() =>
                {
                    IsIdle = AppVM.EngineWrapper.IsIdle;
                });
            };

            AppVM.EngineWrapper.TimedCommandProgressUpdated += (sender, args) =>
            {
                AppVM.DoOnUIThread(() =>
                {
                    IsRunningTimedCommand = args.IsRunning;
                    TimedCommandProgress = args.Progress;
                });
            };

            AppVM.EngineWrapper.MovePlaying += (sender, args) =>
            {
                if (ViewerConfig.PlaySoundEffects)
                {
                    SoundUtils.PlaySound(GameSound.Move);
                }
            };

            AppVM.EngineWrapper.MoveUndoing += (sender, args) =>
            {
                if (ViewerConfig.PlaySoundEffects)
                {
                    SoundUtils.PlaySound(GameSound.Undo);
                }
            };

            AppVM.EngineWrapper.GameModeChanged += (sender, args) =>
            {
                OnPropertyChanged(nameof(IsPlayMode));
                OnPropertyChanged(nameof(IsReviewMode));
                NotifyPlayerPanel();
            };

            PropertyChanged += MainViewModel_PropertyChanged;
        }

        public void OnLoaded()
        {
            Task.Run(async () =>
            {
                try
                {
                    AppVM.DoOnUIThread(() =>
                    {
                        IsIdle = false;
                    });

                    if (ViewerConfig.FirstRun)
                    {
                        FirstRun();
                    }

                    if (AppViewModel.CheckForUpdatesEnabled && ViewerConfig.CheckUpdateOnStart && UpdateUtils.IsConnectedToInternet)
                    {
                        //AppVM.DoOnUIThread(async () =>
                        //{
                            await UpdateUtils.UpdateCheckAsync(true, false);
                        //});
                    }
                }
                catch (Exception ex)
                {
                    ExceptionUtils.HandleException(ex);
                }
                finally
                {
                    AppVM.DoOnUIThread(() =>
                    {
                        IsIdle = true;
                    });
                }
            });
        }

        private static void FirstRun()
        {
            AppVM.DoOnUIThread(() =>
            {
                // Turn off first-run so it doesn't run next time
                ViewerConfig.FirstRun = false;

                if (!AppViewModel.CheckForUpdatesEnabled)
                {
                    StrongReferenceMessenger.Default.Send(new InformationMessage($"Welcome to {AppInfo.Name}!"));
                }
                else
                {
                    StrongReferenceMessenger.Default.Send(new ConfirmationMessage(string.Join(Environment.NewLine + Environment.NewLine, $"Welcome to {AppInfo.Name}!", $"Would you like to check for updates when {AppInfo.Name} starts?", "You can change your mind later in Viewer Options."), (enableAutoUpdate) =>
                    {
                        try
                        {
                            ViewerConfig.CheckUpdateOnStart = enableAutoUpdate;
                        }
                        catch (Exception ex)
                        {
                            ExceptionUtils.HandleException(ex);
                        }
                    }));
                }
            });
        }

        private void MainViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(IsPlayMode):
                case nameof(IsReviewMode):
                    AppVM.DoOnUIThread(() =>
                    {
                        OnPropertyChanged(nameof(Title));

                        PlayTarget.NotifyCanExecuteChanged();
                        Pass.NotifyCanExecuteChanged();
                        UndoLastMove.NotifyCanExecuteChanged();

                        MoveToStart.NotifyCanExecuteChanged();
                        MoveBack.NotifyCanExecuteChanged();
                        MoveForward.NotifyCanExecuteChanged();
                        MoveToEnd.NotifyCanExecuteChanged();

                        SwitchToPlayMode.NotifyCanExecuteChanged();
                        ShowGameMetadata.NotifyCanExecuteChanged();
                        SwitchToReviewMode.NotifyCanExecuteChanged();

                        UpdateBoardHistory();
                    });
                    break;
                case nameof(ViewerConfig):
                    AppVM.UpdateVisualTheme(ViewerConfig.VisualTheme);
                    AppVM.DoOnUIThread(() =>
                    {
                        OnPropertyChanged(nameof(ShowBoardHistory));
                        OnPropertyChanged(nameof(ShowMoveCommentary));
                        OnPropertyChanged(nameof(ShowEvaluationBar));
                        OnPropertyChanged(nameof(AutoCenterBoard));
                        OnPropertyChanged(nameof(CanCenterBoard));
                        OnPropertyChanged(nameof(AutoZoomBoard));
                        OnPropertyChanged(nameof(CanZoomBoard));
                    });
                    break;
            }
        }

        private void UpdateBoardHistory()
        {
            if (Board is null)
            {
                BoardHistory = null;
            }
            else if (IsPlayMode)
            {
                // Replace the BoardHistory and move on
                if (BoardHistory is not null)
                {
                    BoardHistory.PropertyChanged -= BoardHistory_PropertyChanged;
                    BoardHistory.PropertyChanged -= PlayHistory_PropertyChanged;
                }
                BoardHistory = new ObservableBoardHistory(Board.BoardHistory);
                BoardHistory.PropertyChanged += PlayHistory_PropertyChanged;
            }
            else if (IsReviewMode)
            {
                if (BoardHistory?.BoardHistory == ReviewBoard.BoardHistory)
                {
                    BoardHistory.CurrentMoveIndex = Board.BoardHistory.Count - 1;
                }
                else
                {
                    // Replace the BoardHistory
                    BoardHistory = new ObservableBoardHistory(ReviewBoard.BoardHistory, Board.BoardHistory.Count - 1);
                    BoardHistory.PropertyChanged += BoardHistory_PropertyChanged;
                }
            }
        }

        private void BoardHistory_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ObservableBoardHistory.CurrentMoveIndex))
            {
                try
                {
                    AppVM.EngineWrapper.MoveToMoveNumber(BoardHistory.CurrentMoveIndex + 1);
                    OnPropertyChanged(nameof(CurrentMoveCommentary));
                }
                catch (Exception ex)
                {
                    ExceptionUtils.HandleException(ex);
                }
            }
        }

        internal void CanvasClick(double cursorX, double cursorY)
        {
            if (IsPeeking)
            {
                // Board input only makes sense on the live position.
                ExitPeek();
                return;
            }

            if (AppVM.EngineWrapper.CurrentTurnIsHuman)
            {
                CanvasCursorX = cursorX;
                CanvasCursorY = cursorY;

                PieceName clickedPiece = AppVM.EngineWrapper.GetPieceAt(CanvasCursorX, CanvasCursorY, CanvasHexRadius, ViewerConfig.HexOrientation);
                Position clickedPosition = AppVM.EngineWrapper.GetTargetPositionAt(CanvasCursorX, CanvasCursorY, CanvasHexRadius, ViewerConfig.HexOrientation);

                // Make sure the first move is on the origin, no matter what
                if (Board.BoardState == BoardState.NotStarted && AppVM.EngineWrapper.TargetPiece != PieceName.INVALID)
                {
                    if (AppVM.EngineWrapper.TargetPosition == Position.OriginPosition)
                    {
                        AppVM.EngineWrapper.TargetPiece = PieceName.INVALID;
                    }
                    else
                    {
                        clickedPosition = Position.OriginPosition;
                    }
                }

                if (AppVM.EngineWrapper.TargetPiece == PieceName.INVALID && clickedPiece != PieceName.INVALID)
                {
                    // No piece selected, select it
                    AppVM.EngineWrapper.TargetPiece = clickedPiece;
                }
                else if (AppVM.EngineWrapper.TargetPiece != PieceName.INVALID)
                {
                    // Piece is selected
                    if (clickedPiece == AppVM.EngineWrapper.TargetPiece || clickedPosition == AppVM.EngineWrapper.TargetPosition)
                    {
                        // Unselect piece
                        AppVM.EngineWrapper.TargetPiece = PieceName.INVALID;
                    }
                    else
                    {
                        // Get the move with the clicked position
                        Move targetMove = new Move(AppVM.EngineWrapper.TargetPiece, Board.GetPosition(AppVM.EngineWrapper.TargetPiece), clickedPosition);
                        if (IsPlayMode && (!ViewerConfig.BlockInvalidMoves || AppVM.EngineWrapper.CanPlayMove(targetMove)))
                        {
                            // Move is selectable, select position
                            AppVM.EngineWrapper.TargetPosition = clickedPosition;
                        }
                        else
                        {
                            // Move is not selectable, (un)select clicked piece
                            AppVM.EngineWrapper.TargetPiece = clickedPiece;
                        }
                    }
                }
            }
        }

        internal static bool TryPieceClick(PieceName clickedPiece)
        {
            if (IsPeeking)
            {
                AppVM.MainVM.ExitPeek();
                return true;
            }

            if (AppVM.EngineWrapper.CurrentTurnIsHuman)
            {
                if (AppVM.EngineWrapper.TargetPiece == clickedPiece)
                {
                    clickedPiece = PieceName.INVALID;
                }

                AppVM.EngineWrapper.TargetPiece = clickedPiece;
                return true;
            }
            return false;
        }

        internal static bool TryCancelClick()
        {
            if (AppVM.EngineWrapper.CurrentTurnIsHuman)
            {
                AppVM.EngineWrapper.TargetPiece = PieceName.INVALID;
                return true;
            }

            return false;
        }
    }
}
