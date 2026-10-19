using System.Text.Json;

namespace Arcana.Core;

public sealed record DesktopOAuthConfiguration(string ClientId, string ClientSecret)
{
    public static bool IsClientId(string value) =>
        value.Length is > 35 and < 200 &&
        value.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) &&
        value[..^".apps.googleusercontent.com".Length].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static bool TryParse(string json, out DesktopOAuthConfiguration? configuration, out string error)
    {
        configuration = null;
        error = "No es una credencial OAuth válida de Aplicación de escritorio.";
        if (json.Length is <= 0 or > 8192) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("installed", out var installed) ||
                installed.ValueKind != JsonValueKind.Object || !ReadString(installed, "client_id", out string id) ||
                !ReadString(installed, "client_secret", out string secret) || !ReadString(installed, "project_id", out string project) ||
                !IsClientId(id) || secret.Length > 256) return false;
            if (project.Length > 0 && project != "arcana-arena-f2997")
            {
                error = "El cliente OAuth pertenece a otro proyecto.";
                return false;
            }
            configuration = new(id, secret);
            error = "";
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool ReadString(JsonElement parent, string name, out string value)
    {
        value = "";
        if (!parent.TryGetProperty(name, out var field)) return true;
        if (field.ValueKind != JsonValueKind.String) return false;
        value = field.GetString()!;
        return true;
    }
}
