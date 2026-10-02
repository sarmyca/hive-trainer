// Copyright (c) Jon Thysell <http://jonthysell.com>
// Licensed under the MIT License.

using System;
using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

using Mzinga.Viewer;
using Mzinga.Viewer.ViewModels;

namespace Mzinga.Viewer.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel VM
        {
            get
            {
                return DataContext as MainViewModel;
            }
            private set
            {
                DataContext = value;
                value.RequestClose = Close;
            }
        }

        public XamlBoardRenderer BoardRenderer { get; private set; }

        public MainWindow()
        {
            VM = AppViewModel.Instance.MainVM;

            InitializeComponent();

            BoardRenderer = new XamlBoardRenderer(VM, BoardCanvas, WhiteHandStackPanel, BlackHandStackPanel);

            // Tunnel so arrow keys reach us before a focused Button/ListBox swallows them for focus nav.
            AddHandler(KeyDownEvent, MainWindow_PreviewKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

            VM.PropertyChanged += VM_PropertyChanged;

            Opened += MainWindow_Opened;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (VM is null || e.Source is TextBox)
            {
                // Don't hijack arrows while the user is typing (e.g. move commentary).
                return;
            }

            switch (e.Key)
            {
                case Key.Left:
                    TryRunNav(VM.MoveBack, e);
                    break;
                case Key.Right:
                    TryRunNav(VM.MoveForward, e);
                    break;
                case Key.Up:
                case Key.Home:
                    TryRunNav(VM.MoveToStart, e);
                    break;
                case Key.Down:
                case Key.End:
                    TryRunNav(VM.MoveToEnd, e);
                    break;
                case Key.H when e.KeyModifiers == KeyModifiers.None:
                    TryRunNav(VM.ToggleShowBestMove, e);
                    break;
            }
        }

        private static void TryRunNav(System.Windows.Input.ICommand command, KeyEventArgs e)
        {
            if (command is not null && command.CanExecute(null))
            {
                command.Execute(null);
                e.Handled = true;
            }
        }

        private void MainWindow_Opened(object sender, EventArgs e)
        {
            try
            {
                if (MainViewModel.AppVM.EngineExceptionOnStart is not null)
                {
                    throw new Exception("Unable to start the external engine so used the internal one instead.", MainViewModel.AppVM.EngineExceptionOnStart);
                }

                VM.OnLoaded();
            }
            catch (Exception ex)
            {
                ExceptionUtils.HandleException(ex);
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            EngineConsoleWindow.Instance?.Close();
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
        }

        private void MainWindow_KeyUp(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Z:
                    ReZoomButton_Click(sender, e);
                    break;
                case Key.X:
                    LiftButton_Click(sender, e);
                    break;
                case Key.C:
                    ReCenterButton_Click(sender, e);
                    break;
            }
        }

        private void ReZoomButton_Click(object sender, RoutedEventArgs e)
        {
            if (!VM.AutoZoomBoard)
            {
                BoardRenderer.TryRedraw(false, true);
                e.Handled = true;
            }
        }

        private void LiftButton_Click(object sender, RoutedEventArgs e)
        {
            if (VM.CanRaiseStackedPieces)
            {
                BoardRenderer.RaiseStackedPieces = !BoardRenderer.RaiseStackedPieces;
                e.Handled = true;
            }
        }

        private void ReCenterButton_Click(object sender, RoutedEventArgs e)
        {
            if (!VM.AutoCenterBoard)
            {
                BoardRenderer.TryRedraw(true, false);
                e.Handled = true;
            }
        }

        private void VM_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentMoveRowIndex))
            {
                // Keep the highlighted move visible in the move list.
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    int row = VM.CurrentMoveRowIndex;
                    if (row >= 0)
                    {
                        MoveListItems.ContainerFromIndex(row)?.BringIntoView();
                    }
                    else
                    {
                        MoveListScrollViewer.ScrollToHome();
                    }
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
        }
    }
}
