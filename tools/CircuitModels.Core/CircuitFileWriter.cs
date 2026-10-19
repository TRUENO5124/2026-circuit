// ============================================================================
// File: tools/CircuitModels.Core/CircuitFileWriter.cs
// Description: Serializes CircuitProjectData exactly like the WinUI application
//              (Services/ProjectManagerService.cs): indented JSON, PascalCase
//              property names, enums as strings, relaxed JSON escaping.
//              Writing the file here (instead of in each generator project) keeps
//              the four generator outputs byte-compatible.
// ============================================================================

using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CircuitAssistant.Models;

namespace CircuitModels;

public static class CircuitFileWriter
{
    /// <summary>JSON options mirroring ProjectManagerService.JsonOptions.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Serializes one project to the same JSON text the desktop app would save.</summary>
    public static string Serialize(CircuitProjectData data) =>
        JsonSerializer.Serialize(data, JsonOptions);

    /// <summary>Writes the project into <paramref name="outputDirectory"/> and returns the absolute path.</summary>
    public static string Write(CircuitProjectData data, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("输出目录不能为空。", nameof(outputDirectory));
        if (string.IsNullOrWhiteSpace(data.ProjectFileName) ||
            !data.ProjectFileName.EndsWith(".circuit", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"工程文件名 '{data.ProjectFileName}' 必须为 .circuit。");

        string directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, data.ProjectFileName);
        File.WriteAllText(path, Serialize(data), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"[WRITE] {path}");
        return path;
    }
}
