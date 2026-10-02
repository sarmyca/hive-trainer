// Copyright (c) Jon Thysell <http://jonthysell.com>
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using Mzinga.Core;
using Mzinga.Viewer.ViewModels;

namespace Mzinga.Viewer
{
    public class XamlBoardRenderer
    {
        public MainViewModel VM { get; private set; }

        public Canvas BoardCanvas { get; private set; }
        public StackPanel WhiteHandStackPanel { get; private set; }
        public StackPanel BlackHandStackPanel { get; private set; }

        private Point? DragStartPoint = null;
        private PieceName DragStartPieceName = PieceName.INVALID;
        private double DragStartCanvasOffsetX = 0.0;
        private double DragStartCanvasOffsetY = 0.0;
        private bool MoveAfterDragStart = false;

        private double MinDragDistanceToStartPan => BoardPieceSize / 3.0;

        private readonly double PieceCanvasMargin = 3.0;

        private double CanvasOffsetX = 0.0;
        private double CanvasOffsetY = 0.0;

        private double CurrentZoomFactor = 0.0;

        private double BoardPieceSize => GetDefaultPieceSize() * Math.Pow(2.0, CurrentZoomFactor);

        private double HandPieceSize => GetDefaultPieceSize();

        public bool RaiseStackedPieces
        {
            get
            {
                return _raiseStackedPieces;
            }
            set
            {
                bool oldValue = _raiseStackedPieces;
                if (oldValue != value)
                {
                    _raiseStackedPieces = value;
                    DrawBoard(LastBoard);
                }
            }
        }
        private bool _raiseStackedPieces;

        private double StackShiftRatio
        {
            get
            {
                return RaiseStackedPieces ? RaisedStackShiftLevel : BaseStackShiftLevel;
            }
        }

        private const double BaseStackShiftLevel = 0.1;
        private const double RaisedStackShiftLevel = 0.5;

        private Board LastBoard;

        private readonly SolidColorBrush SelectedMoveEdgeBrush;
        private readonly SolidColorBrush SelectedMoveBodyBrush;

        private readonly SolidColorBrush LastMoveEdgeBrush;

        private readonly SolidColorBrush HintEdgeBrush;
        private readonly SolidColorBrush HintBodyBrush;

        public XamlBoardRenderer(MainViewModel vm, Canvas boardCanvas, StackPanel whiteHandStackPanel, StackPanel blackHandStackPanel)
        {
            VM = vm ?? throw new ArgumentNullException(nameof(vm));
            BoardCanvas = boardCanvas ?? throw new ArgumentNullException(nameof(boardCanvas));
            WhiteHandStackPanel = whiteHandStackPanel ?? throw new ArgumentNullException(nameof(whiteHandStackPanel));
            BlackHandStackPanel = blackHandStackPanel ?? throw new ArgumentNullException(nameof(blackHandStackPanel));

            // Init brushes

            SelectedMoveEdgeBrush = new SolidColorBrush(Colors.Orange);
            SelectedMoveBodyBrush = new SolidColorBrush(Colors.Aqua)
            {
                Opacity = 0.25
            };

            LastMoveEdgeBrush = new SolidColorBrush(Colors.SeaGreen);

            // Best-move hint: blue, so it never gets confused with selection (orange) or last move (green)
            HintEdgeBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0xA3, 0xFF));
            HintBodyBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0xA3, 0xFF))
            {
                Opacity = 0.25
            };

            // Bind board updates to VM
            if (VM is not null)
            {
                VM.PropertyChanged += VM_PropertyChanged;
            }

            // Attach events
            BoardCanvas.PropertyChanged += BoardCanvas_SizeChanged;
            BoardCanvas.PointerPressed += BoardCanvas_PointerPressed;
            BoardCanvas.PointerMoved += BoardCanvas_PointerMoved;
            BoardCanvas.PointerReleased += BoardCanvas_PointerReleased;
            BoardCanvas.PointerWheelChanged += BoardCanvas_PointerWheelChanged;
            WhiteHandStackPanel.PointerReleased += CancelClick;
            BlackHandStackPanel.PointerReleased += CancelClick;
        }

        public void TryRedraw(bool forceAutoCenter = false, bool forceAutoZoom = false)
        {
            if (DateTime.Now - LastRedrawOnSizeChange > TimeSpan.FromMilliseconds(10))
            {
                DrawBoard(LastBoard, forceAutoCenter, forceAutoZoom);
                LastRedrawOnSizeChange = DateTime.Now;
            }
        }

        public bool TrySetZoom(double value)
        {
            var newValue = Math.Clamp(value, -2.0, 2.0);
            if (CurrentZoomFactor != newValue)
            {
                CurrentZoomFactor = newValue;
                return true;
            }
            return false;
        }

        public bool TryIncreaseZoom()
        {
            return TrySetZoom(CurrentZoomFactor + 0.1);
        }

        public bool TryDecreaseZoom()
        {
            return TrySetZoom(CurrentZoomFactor - 0.1);
        }

        private double GetDefaultPieceSize()
        {
            int numPiecesToDisplay = MainViewModel.ViewerConfig.StackPiecesInHand ? 2 + Enums.NumBugTypes(LastBoard?.GameType ?? GameType.Base) : 2 + (Enums.NumPieceNames(LastBoard?.GameType ?? GameType.Base) / 2);
            return 0.5 * ((BoardCanvas.Bounds.Height / numPiecesToDisplay) - (2 * PieceCanvasMargin));
        }

        private void VM_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.Board):
                case nameof(MainViewModel.ValidMoves):
                case nameof(MainViewModel.TargetMove):
                case nameof(MainViewModel.ViewerConfig):
                case nameof(MainViewModel.AutoCenterBoard):
                case nameof(MainViewModel.AutoZoomBoard):
                case nameof(MainViewModel.HintMove):
                    AppViewModel.Instance.DoOnUIThread(() =>
                    {
                        DrawBoard(MainViewModel.DisplayBoard);
                    });
                    break;
            }
        }

        private void DrawBoard(Board board, bool forceAutoCenter = false, bool forceAutoZoom = false)
        {
            BoardCanvas.Children.Clear();
            WhiteHandStackPanel.Children.Clear();
            BlackHandStackPanel.Children.Clear();

            int z = BoardCanvas.ZIndex;

            bool autoCenter = forceAutoCenter || MainViewModel.ViewerConfig.AutoCenterBoard || LastBoard is null || LastBoard.BoardState == BoardState.NotStarted;
            bool autoZoom = forceAutoZoom || MainViewModel.ViewerConfig.AutoZoomBoard || LastBoard is null || LastBoard.BoardState == BoardState.NotStarted;

            if (board is not null)
            {
                Point minPoint = new Point(double.MaxValue, double.MaxValue);
                Point maxPoint = new Point(double.MinValue, double.MinValue);

                double boardCanvasWidth = BoardCanvas.Bounds.Width;
                double boardCanvasHeight = BoardCanvas.Bounds.Height;

                var piecesInPlay = GetPiecesOnBoard(board, out int numPieces, out int maxStack);

                int whiteHandCount = board.GetWhiteHand().Count();
                int blackHandCount = board.GetBlackHand().Count();

                if (autoZoom && !_fittingZoom)
                {
                    TrySetZoom(0.0);
                }

                WhiteHandStackPanel.MinWidth = whiteHandCount > 0 ? (HandPieceSize + PieceCanvasMargin) * 2 : 0;
                BlackHandStackPanel.MinWidth = blackHandCount > 0 ? (HandPieceSize + PieceCanvasMargin) * 2 : 0;

                // When peeking at an earlier position, highlight that position's last move and
                // hide live-game selection/valid-move markers (they belong to the live position).
                bool peeking = MainViewModel.IsPeeking;

                Position? lastMoveStart = board?.BoardHistory.LastMove?.Source;
                Position? lastMoveEnd = board?.BoardHistory.LastMove?.Destination;

                PieceName selectedPieceName = peeking ? PieceName.INVALID : MainViewModel.AppVM.EngineWrapper.TargetPiece;
                Position? targetPosition = peeking ? null : MainViewModel.AppVM.EngineWrapper.TargetPosition;

                MoveSet validMoves = peeking ? null : MainViewModel.AppVM.EngineWrapper.ValidMoves;

                HexOrientation hexOrientation = MainViewModel.ViewerConfig.HexOrientation;

                Dictionary<BugType, Stack<Canvas>> pieceCanvasesByBugType = new Dictionary<BugType, Stack<Canvas>>();

                // Draw the pieces in white's hand
                foreach (PieceName pieceName in board.GetWhiteHand())
                {
                    if (pieceName != selectedPieceName || (pieceName == selectedPieceName && targetPosition is null))
                    {
                        BugType bugType = Enums.GetBugType(pieceName);

                        bool disabled = MainViewModel.ViewerConfig.DisablePiecesInHandWithNoMoves && !(validMoves is not null && validMoves.Any(m => m.PieceName == pieceName));
                        Canvas pieceCanvas = GetPieceInHandCanvas(pieceName, HandPieceSize, hexOrientation, disabled);

                        if (!pieceCanvasesByBugType.ContainsKey(bugType))
                        {
                            pieceCanvasesByBugType[bugType] = new Stack<Canvas>();
                        }

                        pieceCanvasesByBugType[bugType].Push(pieceCanvas);
                    }
                }

                DrawHand(WhiteHandStackPanel, pieceCanvasesByBugType);

                pieceCanvasesByBugType.Clear();

                // Draw the pieces in black's hand
                foreach (PieceName pieceName in board.GetBlackHand())
                {
                    if (pieceName != selectedPieceName || (pieceName == selectedPieceName && targetPosition is null))
                    {
                        BugType bugType = Enums.GetBugType(pieceName);

                        bool disabled = MainViewModel.ViewerConfig.DisablePiecesInHandWithNoMoves && !(validMoves is not null && validMoves.Any(m => m.PieceName == pieceName));
                        Canvas pieceCanvas = GetPieceInHandCanvas(pieceName, HandPieceSize, hexOrientation, disabled);

                        if (!pieceCanvasesByBugType.ContainsKey(bugType))
                        {
                            pieceCanvasesByBugType[bugType] = new Stack<Canvas>();
                        }

                        pieceCanvasesByBugType[bugType].Push(pieceCanvas);
                    }
                }

                DrawHand(BlackHandStackPanel, pieceCanvasesByBugType);

                // Draw the pieces in play
                z++;
                for (int stack = 0; stack <= maxStack; stack++)
                {
                    if (piecesInPlay.ContainsKey(stack))
                    {
                        foreach (var tuple in piecesInPlay[stack])
                        {
                            var pieceName = tuple.Item1;
                            var position = tuple.Item2;

                            if (pieceName == selectedPieceName && targetPosition.HasValue)
                            {
                                position = targetPosition.Value;
                            }

                            Point center = GetPoint(position, BoardPieceSize, hexOrientation, true);

                            bool disabled = MainViewModel.ViewerConfig.DisablePiecesInPlayWithNoMoves && !(validMoves is not null && validMoves.Any(m => m.PieceName == pieceName));

                            var pieceTile = new TileControl()
                            {
                                PieceName = pieceName,
                                HexOrientation = hexOrientation,
                                HexSize = BoardPieceSize,
                                PieceStyle = MainViewModel.ViewerConfig.PieceStyle,
                                UseColoredPieces = MainViewModel.ViewerConfig.PieceColors,
                                AddPieceNumbers = MainViewModel.ViewerConfig.AddPieceNumbers,
                                IsEnabled = !disabled,
                                ZIndex = z,
                            };

                            Canvas.SetLeft(pieceTile, center.X - BoardPieceSize);
                            Canvas.SetTop(pieceTile, center.Y - BoardPieceSize);

                            BoardCanvas.Children.Add(pieceTile);

                            minPoint = Min(center, BoardPieceSize, minPoint);
                            maxPoint = Max(center, BoardPieceSize, maxPoint);
                        }
                        z++;
                    }
                }

                // Extent of the hive itself, used to fit the zoom (selection highlights are left out so
                // the zoom doesn't change every time a piece is clicked)
                Point hiveMinPoint = minPoint;
                Point hiveMaxPoint = maxPoint;

                // Highlight last move played
                if (MainViewModel.ViewerConfig.HighlightLastMovePlayed)
                {
                    z++;
                    // Highlight the lastMove start position
                    if (lastMoveStart.HasValue && lastMoveStart.Value.Stack >= 0)
                    {
                        Point center = GetPoint(lastMoveStart.Value, BoardPieceSize, hexOrientation, true);

                        Shape hex = GetHex(center, BoardPieceSize, HexType.LastMove, hexOrientation);
                        hex.ZIndex = z;
                        BoardCanvas.Children.Add(hex);

                        minPoint = Min(center, BoardPieceSize, minPoint);
                        maxPoint = Max(center, BoardPieceSize, maxPoint);
                    }

                    // Highlight the lastMove end position
                    if (lastMoveEnd.HasValue)
                    {
                        Point center = GetPoint(lastMoveEnd.Value, BoardPieceSize, hexOrientation, true);

                        Shape hex = GetHex(center, BoardPieceSize, HexType.LastMove, hexOrientation);
                        hex.ZIndex = z;
                        BoardCanvas.Children.Add(hex);

                        minPoint = Min(center, BoardPieceSize, minPoint);
                        maxPoint = Max(center, BoardPieceSize, maxPoint);
                    }
                }

                // Highlight the selected piece
                if (MainViewModel.ViewerConfig.HighlightTargetMove)
                {
                    z++;
                    if (selectedPieceName != PieceName.INVALID)
                    {
                        Position selectedPiecePosition = board.GetPosition(selectedPieceName);

                        if (selectedPiecePosition != Position.NullPosition)
                        {
                            Point center = GetPoint(selectedPiecePosition, BoardPieceSize, hexOrientation, true);

                            Shape hex = GetHex(center, BoardPieceSize, HexType.SelectedPiece, hexOrientation);
                            hex.ZIndex = z;
                            BoardCanvas.Children.Add(hex);

                            minPoint = Min(center, BoardPieceSize, minPoint);
                            maxPoint = Max(center, BoardPieceSize, maxPoint);
                        }
                    }
                }

                // Draw the valid moves for that piece
                if (MainViewModel.ViewerConfig.HighlightValidMoves)
                {
                    z++;
                    if (selectedPieceName != PieceName.INVALID && validMoves is not null)
                    {
                        foreach (Move validMove in validMoves)
                        {
                            if (validMove.PieceName == selectedPieceName)
                            {
                                Point center = GetPoint(validMove.Destination, BoardPieceSize, hexOrientation);

                                Shape hex = GetHex(center, BoardPieceSize, HexType.ValidMove, hexOrientation);
                                hex.ZIndex = z;
                                BoardCanvas.Children.Add(hex);

                                minPoint = Min(center, BoardPieceSize, minPoint);
                                maxPoint = Max(center, BoardPieceSize, maxPoint);
                            }
                        }
                    }
                }

                // Highlight the target position
                if (MainViewModel.ViewerConfig.HighlightTargetMove)
                {
                    z++;
                    if (targetPosition.HasValue)
                    {
                        Point center = GetPoint(targetPosition.Value, BoardPieceSize, hexOrientation, true);

                        Shape hex = GetHex(center, BoardPieceSize, HexType.SelectedMove, hexOrientation);
                        hex.ZIndex = z;
                        BoardCanvas.Children.Add(hex);

                        minPoint = Min(center, BoardPieceSize, minPoint);
                        maxPoint = Max(center, BoardPieceSize, maxPoint);
                    }
                }

                // Best-move hint from the viewer's own analyzer (only while the hint is switched on)
                Move? hint = VM.HintMove;
                if (hint.HasValue)
                {
                    z++;
                    Move hintMove = hint.Value;

                    Point to = GetPoint(hintMove.Destination, BoardPieceSize, hexOrientation, true);
                    Shape destinationHex = GetHex(to, BoardPieceSize, HexType.Hint, hexOrientation);
                    destinationHex.ZIndex = z;
                    destinationHex.IsHitTestVisible = false;
                    BoardCanvas.Children.Add(destinationHex);

                    minPoint = Min(to, BoardPieceSize, minPoint);
                    maxPoint = Max(to, BoardPieceSize, maxPoint);

                    // Pieces already on the board also get a source outline and an arrow;
                    // pieces placed from hand are highlighted in the hand instead.
                    Position from = board.GetPosition(hintMove.PieceName);
                    if (from != Position.NullPosition)
                    {
                        Point fromPoint = GetPoint(from, BoardPieceSize, hexOrientation, true);

                        Shape sourceHex = GetHex(fromPoint, BoardPieceSize, HexType.HintSource, hexOrientation);
                        sourceHex.ZIndex = z;
                        sourceHex.IsHitTestVisible = false;
                        BoardCanvas.Children.Add(sourceHex);

                        foreach (Shape arrowPart in GetHintArrow(fromPoint, to, BoardPieceSize))
                        {
                            arrowPart.ZIndex = z + 1;
                            BoardCanvas.Children.Add(arrowPart);
                        }
                    }
                }

                // Re-center the game board
                if (autoCenter)
                {
                    double boardWidth = Math.Abs(maxPoint.X - minPoint.X);
                    double boardHeight = Math.Abs(maxPoint.Y - minPoint.Y);

                    if (!double.IsInfinity(boardWidth) && !double.IsInfinity(boardHeight))
                    {
                        double boardCenterX = minPoint.X + (boardWidth / 2);
                        double boardCenterY = minPoint.Y + (boardHeight / 2);

                        double canvasCenterX = boardCanvasWidth / 2;
                        double canvasCenterY = boardCanvasHeight / 2;

                        CanvasOffsetX = canvasCenterX - boardCenterX;
                        CanvasOffsetY = canvasCenterY - boardCenterY;
                    }
                }

                TranslateBoardChildren();

                VM.CanvasHexRadius = BoardPieceSize;
                VM.CanRaiseStackedPieces = maxStack > 0;

                // Auto-zoom: shrink the board until the hive (plus a one-hex margin for move
                // highlights) fits the canvas, then draw once more at that size.
                if (autoZoom && !_fittingZoom && numPieces > 0 && boardCanvasWidth > 0 && boardCanvasHeight > 0)
                {
                    double hiveWidth = (hiveMaxPoint.X - hiveMinPoint.X) + (4 * BoardPieceSize);
                    double hiveHeight = (hiveMaxPoint.Y - hiveMinPoint.Y) + (4 * BoardPieceSize);

                    if (hiveWidth > 0 && hiveHeight > 0 && !double.IsInfinity(hiveWidth) && !double.IsInfinity(hiveHeight))
                    {
                        double fit = Math.Min(boardCanvasWidth / hiveWidth, boardCanvasHeight / hiveHeight);
                        if (fit < 1.0 && TrySetZoom(CurrentZoomFactor + Math.Log2(fit)))
                        {
                            _fittingZoom = true;
                            try
                            {
                                DrawBoard(board, forceAutoCenter, false);
                            }
                            finally
                            {
                                _fittingZoom = false;
                            }
                            return;
                        }
                    }
                }
            }

            LastBoard = board;
        }

        // True while redrawing at a fitted zoom level, so that pass doesn't reset or refit the zoom.
        private bool _fittingZoom = false;

        private void TranslateBoardChildren()
        {
            // Translate all game elements on the board
            TranslateTransform translate = new TranslateTransform()
            {
                X = CanvasOffsetX,
                Y = CanvasOffsetY
            };

            foreach (var child in BoardCanvas.Children)
            {
                child.RenderTransform = translate;
            }
        }

        private static Point Min(Point center, double size, Point minPoint)
        {
            double minX = Math.Min(minPoint.X, center.X - size);
            double minY = Math.Min(minPoint.Y, center.Y - size);

            return new Point(minX, minY);
        }

        private static Point Max(Point center, double size, Point maxPoint)
        {
            double maxX = Math.Max(maxPoint.X, center.X + size);
            double maxY = Math.Max(maxPoint.Y, center.Y + size);

            return new Point(maxX, maxY);
        }

        private Point GetPoint(Position position, double size, HexOrientation hexOrientation, bool stackShift = false)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            double x = hexOrientation == HexOrientation.FlatTop ? size * 1.5 * position.Q : size * Math.Sqrt(3.0) * (position.Q + (0.5 * position.R));
            double y = hexOrientation == HexOrientation.FlatTop ? size * Math.Sqrt(3.0) * (position.R + (0.5 * position.Q)) : size * 1.5 * position.R;

            if (stackShift && position.Stack > 0)
            {
                x += hexOrientation == HexOrientation.FlatTop ? size * 1.5 * StackShiftRatio * position.Stack : size * Math.Sqrt(3.0) * StackShiftRatio * position.Stack;
                y -= hexOrientation == HexOrientation.FlatTop ? size * Math.Sqrt(3.0) * StackShiftRatio * position.Stack : size * 1.5 * StackShiftRatio * position.Stack;
            }

            return new Point(x, y);
        }

        private static Dictionary<int, List<Tuple<PieceName, Position>>> GetPiecesOnBoard(Board board, out int numPieces, out int maxStack)
        {
            if (board is null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            numPieces = 0;
            maxStack = -1;

            Dictionary<int, List<Tuple<PieceName, Position>>> pieces = new Dictionary<int, List<Tuple<PieceName, Position>>>
            {
                [0] = new List<Tuple<PieceName, Position>>()
            };

            PieceName targetPieceName = MainViewModel.IsPeeking ? PieceName.INVALID : MainViewModel.AppVM.EngineWrapper.TargetPiece;
            Position? targetPosition = MainViewModel.IsPeeking ? null : MainViewModel.AppVM.EngineWrapper.TargetPosition;

            bool targetPieceInPlay = false;

            // Add pieces already on the board
            foreach (PieceName pieceName in board.GetPiecesInPlay())
            {
                Position position = board.GetPosition(pieceName);

                if (pieceName == targetPieceName)
                {
                    if (targetPosition.HasValue)
                    {
                        position = targetPosition.Value;
                    }
                    targetPieceInPlay = true;
                }

                int stack = position.Stack;
                maxStack = Math.Max(maxStack, stack);

                if (!pieces.ContainsKey(stack))
                {
                    pieces[stack] = new List<Tuple<PieceName, Position>>();
                }

                pieces[stack].Add(new Tuple<PieceName, Position>(pieceName, position));
                numPieces++;
            }

            // Add piece being placed on the board
            if (!targetPieceInPlay && targetPosition.HasValue)
            {
                int stack = targetPosition.Value.Stack;
                maxStack = Math.Max(maxStack, stack);

                if (!pieces.ContainsKey(stack))
                {
                    pieces[stack] = new List<Tuple<PieceName, Position>>();
                }

                pieces[stack].Add(new Tuple<PieceName, Position>(targetPieceName, targetPosition.Value));
                numPieces++;
            }

            return pieces;
        }

        private Shape GetHex(Point center, double size, HexType hexType, HexOrientation hexOrientation)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            double strokeThickness = size / 10;

            var hex = new HexShape()
            {
                StrokeThickness = strokeThickness,
                HexSize = size,
                HexOrientation = hexOrientation,
            };

            switch (hexType)
            {
                case HexType.ValidMove:
                    hex.Fill = SelectedMoveBodyBrush;
                    hex.Stroke = SelectedMoveBodyBrush;
                    break;
                case HexType.SelectedPiece:
                    hex.Stroke = SelectedMoveEdgeBrush;
                    break;
                case HexType.SelectedMove:
                    hex.Fill = SelectedMoveBodyBrush;
                    hex.Stroke = SelectedMoveEdgeBrush;
                    break;
                case HexType.LastMove:
                    hex.Stroke = LastMoveEdgeBrush;
                    break;
                case HexType.Hint:
                    hex.Fill = HintBodyBrush;
                    hex.Stroke = HintEdgeBrush;
                    break;
                case HexType.HintSource:
                    hex.Stroke = HintEdgeBrush;
                    break;
            }

            Canvas.SetLeft(hex, center.X - size);
            Canvas.SetTop(hex, center.Y - size);

            return hex;
        }

        private IEnumerable<Shape> GetHintArrow(Point from, Point to, double size)
        {
            Vector direction = to - from;
            double length = direction.Length;
            if (length < 1.0)
            {
                yield break; // e.g. a beetle moving onto its own neighbour stack drawn in place
            }

            Vector unit = direction / length;
            Vector normal = new Vector(-unit.Y, unit.X);

            double inset = size * 0.35;
            double headLength = size * 0.55;

            Point start = from + (unit * inset);
            Point tip = to - (unit * inset);
            Point headBase = tip - (unit * headLength);

            yield return new Line()
            {
                StartPoint = start,
                EndPoint = headBase,
                Stroke = HintEdgeBrush,
                StrokeThickness = size / 6,
                StrokeLineCap = PenLineCap.Round,
                Opacity = 0.9,
                IsHitTestVisible = false,
            };

            yield return new Polygon()
            {
                Points = new List<Point>()
                {
                    tip,
                    headBase + (normal * headLength * 0.6),
                    headBase - (normal * headLength * 0.6),
                },
                Fill = HintEdgeBrush,
                Opacity = 0.9,
                IsHitTestVisible = false,
            };
        }

        private enum HexType
        {
            ValidMove,
            SelectedPiece,
            SelectedMove,
            LastMove,
            Hint,
            HintSource,
        }

        private void DrawHand(StackPanel handPanel, Dictionary<BugType, Stack<Canvas>> pieceCanvases)
        {
            for (int bt = 0; bt < (int)BugType.NumBugTypes; bt++)
            {
                var bugType = (BugType)bt;
                if (pieceCanvases.ContainsKey(bugType))
                {
                    if (MainViewModel.ViewerConfig.StackPiecesInHand)
                    {
                        int startingCount = pieceCanvases[bugType].Count;

                        Canvas bugStack = new Canvas()
                        {
                            Height = pieceCanvases[bugType].Peek().Height * (1 + startingCount * BaseStackShiftLevel),
                            Width = pieceCanvases[bugType].Peek().Width * (1 + startingCount * BaseStackShiftLevel),
                            Margin = new Thickness(PieceCanvasMargin),
                            Background = new SolidColorBrush(Colors.Transparent),
                        };

                        while (pieceCanvases[bugType].Count > 0)
                        {
                            Canvas pieceCanvas = pieceCanvases[bugType].Pop();
                            Canvas.SetTop(pieceCanvas, pieceCanvas.Height * ((startingCount - pieceCanvases[bugType].Count - 1) * BaseStackShiftLevel));
                            Canvas.SetLeft(pieceCanvas, pieceCanvas.Width * ((startingCount - pieceCanvases[bugType].Count - 1) * BaseStackShiftLevel));
                            bugStack.Children.Add(pieceCanvas);
                        }

                        handPanel.Children.Add(bugStack);
                    }
                    else
                    {
                        foreach (Canvas pieceCanvas in pieceCanvases[bugType].Reverse())
                        {
                            handPanel.Children.Add(pieceCanvas);
                        }
                    }
                }
            }
        }

        private Canvas GetPieceInHandCanvas(PieceName pieceName, double size, HexOrientation hexOrientation, bool disabled)
        {
            Point center = new Point(size, size);

            Canvas pieceCanvas = new Canvas
            {
                Height = size * 2,
                Width = size * 2,
                Margin = new Thickness(PieceCanvasMargin),
            };

            var pieceTile = new TileControl()
            {
                PieceName = pieceName,
                HexOrientation = hexOrientation,
                HexSize = size,
                PieceStyle = MainViewModel.ViewerConfig.PieceStyle,
                UseColoredPieces = MainViewModel.ViewerConfig.PieceColors,
                AddPieceNumbers = MainViewModel.ViewerConfig.AddPieceNumbers,
                IsEnabled = !disabled,
            };

            pieceCanvas.Children.Add(pieceTile);

            // Add highlight if the piece is selected
            if (!MainViewModel.IsPeeking && MainViewModel.AppVM.EngineWrapper.TargetPiece == pieceName)
            {
                Shape highlightHex = GetHex(center, size, HexType.SelectedPiece, hexOrientation);
                pieceCanvas.Children.Add(highlightHex);
            }
            else if (VM.HintMove?.PieceName == pieceName)
            {
                // Best-move hint says: place this piece from hand
                Shape hintHex = GetHex(center, size, HexType.Hint, hexOrientation);
                hintHex.IsHitTestVisible = false;
                pieceCanvas.Children.Add(hintHex);
            }

            pieceCanvas.PointerReleased += PieceCanvas_Click;

            return pieceCanvas;
        }

        private void PieceCanvas_Click(object sender, PointerReleasedEventArgs e)
        {
            if (sender is Canvas pieceCanvas && pieceCanvas.Children.Count > 0 && pieceCanvas.Children[0] is TileControl pieceTile)
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                {
                    MainViewModel.TryPieceClick(pieceTile.PieceName);
                    e.Handled = true;
                }
            }
        }

        private void BoardCanvas_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            var pointerPoint = e.GetCurrentPoint(BoardCanvas);
            if (pointerPoint.Properties.IsLeftButtonPressed)
            {
                DragStartPoint = pointerPoint.Position;
                DragStartPieceName = MainViewModel.AppVM.EngineWrapper.GetPieceAt(pointerPoint.Position.X - CanvasOffsetX, pointerPoint.Position.Y - CanvasOffsetY, BoardPieceSize, MainViewModel.ViewerConfig.HexOrientation);
                DragStartCanvasOffsetX = CanvasOffsetX;
                DragStartCanvasOffsetY = CanvasOffsetY;
                e.Handled = true;
            }
            else
            {
                DragStartPoint = null;
                DragStartPieceName = PieceName.INVALID;
            }
            MoveAfterDragStart = false;
        }

        private void BoardCanvas_PointerMoved(object sender, PointerEventArgs e)
        {
            if (DragStartPoint is not null)
            {
                var pointerPoint = e.GetCurrentPoint(BoardCanvas);
                if (pointerPoint.Properties.IsLeftButtonPressed)
                {
                    var dX = pointerPoint.Position.X - DragStartPoint.Value.X;
                    var dY = pointerPoint.Position.X - DragStartPoint.Value.X;

                    if (MoveAfterDragStart || Math.Sqrt(dX * dX + dY * dY) >= MinDragDistanceToStartPan)
                    {
                        if (!VM.AutoCenterBoard && DragStartPieceName != PieceName.INVALID)
                        {
                            CanvasOffsetX = DragStartCanvasOffsetX + (pointerPoint.Position.X - DragStartPoint.Value.X);
                            CanvasOffsetY = DragStartCanvasOffsetY + (pointerPoint.Position.Y - DragStartPoint.Value.Y);

                            TranslateBoardChildren();
                        }

                        MoveAfterDragStart = true;
                        e.Handled = true;
                    }
                }
            }
        }

        private void BoardCanvas_PointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                Point point = e.GetPosition(BoardCanvas);
                if (!MoveAfterDragStart && VM.IsIdle)
                {
                    VM.CanvasClick(point.X - CanvasOffsetX, point.Y - CanvasOffsetY);
                    e.Handled = true;
                }
            }
            DragStartPoint = null;
            DragStartPieceName = PieceName.INVALID;
            MoveAfterDragStart = false;
        }

        private void CancelClick(object sender, RoutedEventArgs e)
        {
            MainViewModel.TryCancelClick();
        }

        private DateTime LastRedrawOnSizeChange = DateTime.Now;

        private void BoardCanvas_SizeChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Canvas.BoundsProperty)
            {
                TryRedraw();
            }
        }

        private void BoardCanvas_PointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            if (!VM.AutoZoomBoard)
            {
                if (e.Delta.Y > 0)
                {
                    if (TryIncreaseZoom())
                    {
                        
                        TryRedraw();
                    }
                }
                else if (e.Delta.Y < 0)
                {
                    if (TryDecreaseZoom())
                    {
                        TryRedraw();
                    }
                }
            }
        }
    }
}
