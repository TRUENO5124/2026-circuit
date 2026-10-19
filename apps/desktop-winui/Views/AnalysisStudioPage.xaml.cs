using System.Linq;
using System.Numerics;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CircuitAssistant.Models;
using CircuitAssistant.ViewModels;

namespace CircuitAssistant.Views;

public sealed partial class AnalysisStudioPage : Page
{
    private bool _canvasEventsConnected;
    public AnalysisStudioViewModel ViewModel { get; } = new();

    public AnalysisStudioPage()
    {
        App.LogTrace("AnalysisStudioPage.ctor: Starting");
        try
        {
            InitializeComponent();
            App.LogTrace("AnalysisStudioPage.ctor: InitializeComponent complete");
        }
        catch (System.Exception ex)
        {
            App.LogTrace($"AnalysisStudioPage.ctor: InitializeComponent exception: {ex}");
            throw;
        }

        Loaded += (s, e) =>
        {
            if (_canvasEventsConnected) return;
            _canvasEventsConnected = true;
            App.LogTrace("AnalysisStudioPage: Loaded event fired");
            if (MainCircuitCanvas != null)
            {
                MainCircuitCanvas.CursorCoordinatesChanged += (x, y) =>
                {
                    ViewModel.CursorCoordinateText = $"X: {(int)x} | Y: {(int)y}";
                };
                MainCircuitCanvas.StatusMessageChanged += (msg) =>
                {
                    ViewModel.StatusMessage = msg;
                };
                MainCircuitCanvas.PushUndoRequested += () => ViewModel.PushUndo();
                MainCircuitCanvas.UndoRequested += () =>
                {
                    ViewModel.Undo();
                    MainCircuitCanvas.InvalidateCanvas();
                };
                MainCircuitCanvas.RedoRequested += () =>
                {
                    ViewModel.Redo();
                    MainCircuitCanvas.InvalidateCanvas();
                };
            }
        };

        KeyDown += AnalysisStudioPage_KeyDown;
        ViewModel.ConfirmDiscardChangesAsync = async () =>
        {
            if (XamlRoot == null) return false;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "工程尚未保存",
                Content = "是否保存当前工程的修改？",
                PrimaryButtonText = "保存",
                SecondaryButtonText = "不保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };
            var choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.Secondary) return true;
            if (choice != ContentDialogResult.Primary) return false;
            await ViewModel.SaveProjectAsync();
            return !ViewModel.IsProjectModified;
        };
    }

    private void AnalysisStudioPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var focused = FocusManager.GetFocusedElement(this.XamlRoot);
        if (focused is TextBox || focused is NumberBox || focused is AutoSuggestBox)
        {
            return; // In text input mode, don't trigger canvas shortcuts
        }

        if (e.Key is VirtualKey.Space or VirtualKey.R)
        {
            if (ViewModel.SelectedElement != null)
            {
                ViewModel.RotateSelectedElement();
                MainCircuitCanvas?.InvalidateCanvas();
                e.Handled = true;
            }
        }
        else if (e.Key == VirtualKey.Z && (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)))
        {
            ViewModel.Undo();
            MainCircuitCanvas?.InvalidateCanvas();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Y && (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)))
        {
            ViewModel.Redo();
            MainCircuitCanvas?.InvalidateCanvas();
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.Delete or VirtualKey.Back)
        {
            if (ViewModel.SelectedElement != null)
            {
                ViewModel.DeleteSelectedElement();
                MainCircuitCanvas?.InvalidateCanvas();
                e.Handled = true;
            }
            else if (ViewModel.SelectedWire != null)
            {
                ViewModel.DeleteSelectedWire();
                MainCircuitCanvas?.InvalidateCanvas();
                e.Handled = true;
            }
            else if (MainCircuitCanvas?.SelectedWire != null)
            {
                MainCircuitCanvas.DeleteSelected();
                e.Handled = true;
            }
        }
        else if (e.Key == VirtualKey.Escape)
        {
            MainCircuitCanvas?.DeselectSelected();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.W)
        {
            if (MainCircuitCanvas != null)
            {
                MainCircuitCanvas.IsWireMode = !MainCircuitCanvas.IsWireMode;
                e.Handled = true;
            }
        }
    }

    private void OnCtrlRInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SolveCircuitCommand.CanExecute(null))
        {
            ViewModel.SolveCircuitCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void InspectorEdit_GettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (ViewModel.HasActiveProject) ViewModel.PushUndo();
    }

    private void OnCtrlZInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var focused = FocusManager.GetFocusedElement(this.XamlRoot);
        if (focused is TextBox || focused is NumberBox || focused is AutoSuggestBox) return;

        ViewModel.Undo();
        MainCircuitCanvas?.InvalidateCanvas();
        args.Handled = true;
    }

    private void OnCtrlYInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var focused = FocusManager.GetFocusedElement(this.XamlRoot);
        if (focused is TextBox || focused is NumberBox || focused is AutoSuggestBox) return;

        ViewModel.Redo();
        MainCircuitCanvas?.InvalidateCanvas();
        args.Handled = true;
    }

    private void OnCtrlSInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SaveProjectCommand.CanExecute(null))
        {
            ViewModel.SaveProjectCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnCtrlShiftSInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SaveProjectAsCommand.CanExecute(null))
        {
            ViewModel.SaveProjectAsCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnCtrlOInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.OpenProjectCommand.CanExecute(null))
        {
            ViewModel.OpenProjectCommand.Execute(null);
            MainCircuitCanvas?.InvalidateCanvas();
            args.Handled = true;
        }
    }

    private void OnDeleteInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var focused = FocusManager.GetFocusedElement(this.XamlRoot);
        if (focused is TextBox || focused is NumberBox || focused is AutoSuggestBox) return;

        if (ViewModel.SelectedElement != null)
        {
            ViewModel.DeleteSelectedElement();
            MainCircuitCanvas?.InvalidateCanvas();
            args.Handled = true;
        }
        else if (ViewModel.SelectedWire != null)
        {
            ViewModel.DeleteSelectedWire();
            MainCircuitCanvas?.InvalidateCanvas();
            args.Handled = true;
        }
        else if (MainCircuitCanvas?.SelectedWire != null)
        {
            MainCircuitCanvas.DeleteSelected();
            args.Handled = true;
        }
    }

    private void OnSpaceInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var focused = FocusManager.GetFocusedElement(this.XamlRoot);
        if (focused is TextBox || focused is NumberBox || focused is AutoSuggestBox) return;

        if (ViewModel.SelectedElement != null)
        {
            ViewModel.RotateSelectedElement();
            MainCircuitCanvas?.InvalidateCanvas();
            args.Handled = true;
        }
    }

    private void OnAltXInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var focused = FocusManager.GetFocusedElement(this.XamlRoot);
        if (focused is TextBox || focused is NumberBox || focused is AutoSuggestBox) return;

        if (MainCircuitCanvas != null)
        {
            MainCircuitCanvas.MirrorSelected();
            args.Handled = true;
        }
        else if (ViewModel.SelectedElement != null)
        {
            ViewModel.MirrorSelectedElement();
            args.Handled = true;
        }
    }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        MainCircuitCanvas?.DeselectSelected();
        args.Handled = true;
    }

    private void OnWInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var focused = FocusManager.GetFocusedElement(this.XamlRoot);
        if (focused is TextBox || focused is NumberBox || focused is AutoSuggestBox) return;

        if (MainCircuitCanvas != null)
        {
            MainCircuitCanvas.IsWireMode = !MainCircuitCanvas.IsWireMode;
            args.Handled = true;
        }
    }

    private void AddSymbolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CircuitSymbolItem item)
        {
            ViewModel.AddSymbolToCanvas(item);
        }
    }

    private void SymbolListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CircuitSymbolItem item)
        {
            ViewModel.AddSymbolToCanvas(item);
        }
    }

    private void SymbolListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count > 0 && e.Items[0] is CircuitSymbolItem item)
        {
            e.Data.SetText(item.Id);
        }
    }

    private void MainCircuitCanvas_ElementSelected(CircuitElement? el)
    {
        ViewModel.OnCanvasElementSelected(el);
    }

    private void MainCircuitCanvas_ElementDroppedFromLibrary(string symbolId, Vector2 worldPos)
    {
        var sym = ViewModel.AllSymbols.FirstOrDefault(s => s.Id == symbolId);
        if (sym != null)
        {
            ViewModel.AddSymbolToCanvasWithPosition(sym, worldPos);
        }
    }

    private void MainCircuitCanvas_WireCreated(CircuitWire wire)
    {
        ViewModel.StatusMessage = $"已建立导线连接: {wire.StartElementId}.{wire.StartPinName} ⟷ {wire.EndElementId}.{wire.EndPinName}";
    }

    private void MainCircuitCanvas_StatusMessageChanged(string msg)
    {
        ViewModel.StatusMessage = msg;
    }

    private void MainCircuitCanvas_WireSelected(CircuitWire? wire)
    {
        ViewModel.SelectedWire = wire;
    }

    private void BranchFromSelectedWireButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedWire != null)
        {
            MainCircuitCanvas?.StartBranchFromWire(ViewModel.SelectedWire);
        }
    }

    private void ElementCard_SelectClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CircuitElement el })
        {
            ViewModel.SelectElement(el);
            MainCircuitCanvas?.InvalidateCanvas();
        }
    }

    private void ElementCard_RotateClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CircuitElement el })
        {
            ViewModel.RotateElement(el);
            MainCircuitCanvas?.InvalidateCanvas();
        }
    }

    private void ElementCard_MirrorClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CircuitElement el })
        {
            ViewModel.MirrorElement(el);
            MainCircuitCanvas?.InvalidateCanvas();
        }
    }

    private void ElementCard_DeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CircuitElement el })
        {
            ViewModel.DeleteElement(el);
            MainCircuitCanvas?.InvalidateCanvas();
        }
    }

    private string GetUnitDisplay(double number, UnitOption? unit)
    {
        string symbol = unit?.Symbol ?? "";
        return $"{number} {symbol}";
    }

    private string GetBetaDisplay(double beta)
    {
        return $"β = {beta}";
    }
}
