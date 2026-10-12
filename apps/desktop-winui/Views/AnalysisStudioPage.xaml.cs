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
            App.LogTrace("AnalysisStudioPage: Loaded event fired");
            if (MainCircuitCanvas != null)
            {
                MainCircuitCanvas.CursorCoordinatesChanged += (x, y) =>
                {
                    ViewModel.CursorCoordinateText = $"X: {(int)x} mil | Y: {(int)y} mil";
                };
                MainCircuitCanvas.StatusMessageChanged += (msg) =>
                {
                    ViewModel.StatusMessage = msg;
                };
            }
        };

        KeyDown += AnalysisStudioPage_KeyDown;
    }

    private void AnalysisStudioPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var focused = FocusManager.GetFocusedElement(this.XamlRoot);
        if (focused is TextBox || focused is NumberBox || focused is AutoSuggestBox)
        {
            return; // In text input mode, don't trigger canvas shortcuts
        }

        if (e.Key == VirtualKey.R)
        {
            if (ViewModel.SelectedElement != null)
            {
                ViewModel.RotateSelectedElement();
                MainCircuitCanvas?.InvalidateCanvas();
                e.Handled = true;
            }
        }
        else if (e.Key is VirtualKey.Delete or VirtualKey.Back)
        {
            if (ViewModel.SelectedElement != null)
            {
                ViewModel.DeleteSelectedElement();
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
        else if (MainCircuitCanvas?.SelectedWire != null)
        {
            MainCircuitCanvas.DeleteSelected();
            args.Handled = true;
        }
    }

    private void OnRInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
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
