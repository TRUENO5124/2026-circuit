using System.Linq;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            }
        };
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
