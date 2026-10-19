namespace CircuitAssistant;

// The console harness links the real ViewModel without constructing a WinUI Application.
// Only trace logging is stubbed; commands, model state, solver and file service are real.
internal static class App
{
    public static void LogTrace(string message) { }
}
