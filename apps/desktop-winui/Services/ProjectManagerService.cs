using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using CircuitAssistant.Models;

namespace CircuitAssistant.Services;

public class ProjectManagerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string GetDefaultProjectsDirectory()
    {
        var docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var projectDir = Path.Combine(docPath, "CircuitAssistant", "Projects");
        if (!Directory.Exists(projectDir))
        {
            Directory.CreateDirectory(projectDir);
        }
        return projectDir;
    }

    public static async Task SaveProjectToFileAsync(string filePath, CircuitProjectData project)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不可为空", nameof(filePath));

        project.Validate();
        project.ModifiedAt = DateTime.Now;
        var json = JsonSerializer.Serialize(project, JsonOptions);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var fullPath = Path.GetFullPath(filePath);
        var temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
            else File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static async Task<CircuitProjectData> LoadProjectFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"工程文件未找到: {filePath}", filePath);

        var json = await File.ReadAllTextAsync(filePath);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !new[] { "Version", "Elements", "Wires" }.All(name => document.RootElement.EnumerateObject()
                .Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("文件不是 CircuitAssistant 工程；不支持直接打开 Web CircuitGraph 或第三方原理图。");
        var data = JsonSerializer.Deserialize<CircuitProjectData>(json, JsonOptions);
        if (data == null)
            throw new InvalidDataException("无法解析工程文件数据，格式可能已损坏。");

        data.Validate();
        return data;
    }

    public static Func<IntPtr>? WindowHandleProvider { get; set; }
    public static IntPtr WindowHandle => WindowHandleProvider?.Invoke() ?? IntPtr.Zero;

    public static async Task<StorageFile?> PickSaveProjectFileAsync(string suggestedFileName)
    {
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("电路工程文件 (*.circuit)", new List<string> { ".circuit" });
        picker.FileTypeChoices.Add("工程配置 JSON (*.json)", new List<string> { ".json" });
        picker.SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? "MyCircuit.circuit" : suggestedFileName;

        var hwnd = WindowHandle;
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        return await picker.PickSaveFileAsync();
    }

    public static async Task<StorageFile?> PickOpenProjectFileAsync()
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.ViewMode = PickerViewMode.List;
        picker.FileTypeFilter.Add(".circuit");
        picker.FileTypeFilter.Add(".json");

        var hwnd = WindowHandle;
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        return await picker.PickSingleFileAsync();
    }

    public static async Task<StorageFile?> PickExportSpiceFileAsync(string suggestedFileName)
    {
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("SPICE 网表 (*.cir)", new List<string> { ".cir" });
        picker.FileTypeChoices.Add("SPICE 仿真网表 (*.sp)", new List<string> { ".sp" });
        picker.FileTypeChoices.Add("文本文件 (*.txt)", new List<string> { ".txt" });
        picker.SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? "CircuitNetlist.cir" : suggestedFileName;

        var hwnd = WindowHandle;
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        return await picker.PickSaveFileAsync();
    }

    /// <summary>
    /// Generic save picker used by the native analysis report exporter. The
    /// report service itself stays platform-free; this method only chooses a
    /// destination file for Markdown / LaTeX / Word(.docx) / HTML reports.
    /// </summary>
    public static async Task<StorageFile?> PickExportReportFileAsync(
        string suggestedFileName,
        string fileTypeDescription,
        IReadOnlyList<string> extensions)
    {
        if (extensions == null || extensions.Count == 0)
            throw new ArgumentException("报告格式至少需要提供一个扩展名。", nameof(extensions));

        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        var normalized = extensions.Select(e => e.StartsWith('.') ? e : "." + e).ToList();
        picker.FileTypeChoices.Add(fileTypeDescription, normalized);
        var defaultName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? "CircuitAnalysisReport" + normalized[0]
            : suggestedFileName;
        picker.SuggestedFileName = defaultName;

        var hwnd = WindowHandle;
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        return await picker.PickSaveFileAsync();
    }
}
