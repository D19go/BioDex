using System.Net.Http.Headers;
using System.Text.Json;
using FaunaLens.Models;

namespace FaunaLens.Services;

public sealed class RemoteIdentificationClient
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<IReadOnlyList<SpeciesMatch>?> IdentifyAsync(string endpoint, string imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        await using var stream = File.OpenRead(imagePath);
        using var content = new MultipartFormDataContent();
        using var image = new StreamContent(stream);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(image, "image", Path.GetFileName(imagePath));

        using var response = await http.PostAsync(endpoint.TrimEnd('/') + "/identify", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = JsonSerializer.Deserialize<List<RemoteMatch>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (data is null) return null;
        return data.Select(x => new SpeciesMatch(new Species { CommonName = x.CommonName, ScientificName = x.ScientificName }, x.Score)).ToList();
    }

    sealed class RemoteMatch
    {
        public string CommonName { get; set; } = "";
        public string ScientificName { get; set; } = "";
        public float Score { get; set; }
    }
}
