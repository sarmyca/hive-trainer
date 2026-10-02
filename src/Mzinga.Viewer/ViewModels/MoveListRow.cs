// Copyright (c) Jon Thysell <http://jonthysell.com>
// Licensed under the MIT License.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Mzinga.Viewer.ViewModels
{
    // One row of the chess-style move list: "12.  <white ply>  <black ply>".
    public class MoveListRow
    {
        public string Number { get; }

        public MovePly White { get; }

        public MovePly Black { get; }

        public bool HasBlack => Black is not null;

        // Zebra striping
        public bool IsAlternate { get; }

        public MoveListRow(int rowIndex, MovePly white, MovePly black)
        {
            Number = $"{rowIndex + 1}.";
            White = white;
            Black = black;
            IsAlternate = rowIndex % 2 == 1;
        }
    }

    // A single move (ply) in the move list; clicking it jumps the board to that position.
    public class MovePly : ObservableObject
    {
        public int Index { get; }

        public string Notation { get; }

        public RelayCommand JumpCommand { get; }

        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetProperty(ref _isCurrent, value);
        }
        private bool _isCurrent;

        // Moves after the position currently on screen
        public bool IsFuture
        {
            get => _isFuture;
            set => SetProperty(ref _isFuture, value);
        }
        private bool _isFuture;

        public MovePly(int index, string notation, System.Action<int> jump)
        {
            Index = index;
            Notation = notation;
            JumpCommand = new RelayCommand(() => jump(Index));
        }
    }
}
