namespace CircuitAssistant.Models;

public class CircuitSymbolItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string ValueText { get; set; } = "";
    public string Glyph { get; set; } = "";
    public string Description { get; set; } = "";
    public string Standard { get; set; } = "IEC / ANSI";
    public double DefaultNumber { get; set; } = 1.0;
    public string DefaultUnit { get; set; } = "";
    public ElementType ElementType { get; set; } = ElementType.Resistor;
}
