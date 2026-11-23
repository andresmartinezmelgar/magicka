using System.IO.Compression;
using System.Text;

namespace Arcana.Core;

public sealed partial class DiagnosticLog
{
    public async Task<string> ExportReportAsync(string reportsDirectory, IReadOnlyDictionary<string, object?>? gameContext = null, CancellationToken token = default)
    {
        await FlushAsync(token).ConfigureAwait(false);
        Directory.CreateDirectory(reportsDirectory);
        string target = Path.Combine(reportsDirectory, $"ArcanaArena-diagnostic-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(target, ZipArchiveMode.Create);
        foreach (string path in Directory.Exists(DirectoryPath) ? Directory.GetFiles(DirectoryPath, "*.jsonl") : [])
        {
            // Solo logs. No copia launcher.cfg, variables de entorno ni archivos del usuario.
            FileStream input;
            try
            {
                input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (input.Length > 64 * 1024 * 1024) { input.Dispose(); continue; }
            }
            catch (IOException) { continue; } // La rotación puede retirar un archivo mientras se exporta.
            using var reading = input;
            using var output = archive.CreateEntry(Path.GetFileName(path), CompressionLevel.Fastest).Open();
            await input.CopyToAsync(output, token).ConfigureAwait(false);
        }
        using var metadata = new StreamWriter(archive.CreateEntry("report.json").Open(), new UTF8Encoding(false));
        await metadata.WriteAsync(Line(DiagnosticLevel.Information, "diagnostic.report", "Informe generado por el usuario.", null, gameContext)).ConfigureAwait(false);
        return target;
    }
}
