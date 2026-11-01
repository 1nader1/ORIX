using DesignToAngular.Domain.Design;
using DesignToAngular.Services;
using DesignToAngular.Services.CodeGen;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace DesignToAngular.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DesignController(DesignAnalyzer analyzer, CodeGenerator generator) : ControllerBase
{
    private static readonly string[] Allowed = ["image/png", "image/jpeg", "image/webp"];

    [HttpPost("analyze")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Analyze(IFormFile image, [FromForm] string? palette, [FromForm] int? part, [FromForm] int? parts, CancellationToken ct)
    {
        if (image is null || image.Length == 0) return BadRequest("No image.");
        if (image.Length > 5 * 1024 * 1024) return BadRequest("Max size is 5MB.");
        if (!Allowed.Contains(image.ContentType)) return BadRequest("Use PNG, JPG or WEBP.");

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct);

        var result = await analyzer.AnalyzeAsync(ms.ToArray(), image.ContentType, ParsePalette(palette), part, parts is > 1 and <= 8 ? parts : null, ct);
        return Ok(result);
    }

    private static LayoutMode ParseLayout(string? value) =>
        string.Equals(value, "model", StringComparison.OrdinalIgnoreCase) ? LayoutMode.ModelOnly : LayoutMode.Measured;

    /// <summary>The browser measures the real colors of the image and sends them as JSON text. Bad input is ignored.</summary>
    private static List<PaletteColor> ParsePalette(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4000) return new();
        try
        {
            var items = JsonSerializer.Deserialize<List<PaletteColor>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return ColorSnapper.Clean(items);
        }
        catch (JsonException)
        {
            return new();
        }
    }

    /// <summary>Design JSON in, generated Angular files + standalone preview HTML out.</summary>
    [HttpPost("generate")]
    public ActionResult<GeneratedProject> Generate([FromBody] DesignDocument design, [FromQuery] string? name, [FromQuery] string? motion, [FromQuery] string? layout)
        => Ok(generator.Generate(design, name, MotionPlanner.Parse(motion), ParseLayout(layout)));

    /// <summary>
    /// Same as generate, but returns a ZIP (Angular files, design tokens, README, preview.html and the cropped pictures).
    /// The client crops the pictures from the original image and sends them back as base64 PNGs.
    /// </summary>
    [HttpPost("download")]
    [RequestSizeLimit(100_000_000)] // replaced pictures can be large
    public IActionResult Download([FromBody] DownloadRequest request, [FromQuery] string? name)
    {
        var project = generator.Generate(request.Design, name, MotionPlanner.Parse(request.Motion), ParseLayout(request.Layout));
        return File(ZipBuilder.Build(project, request.Assets), "application/zip", $"{project.Name}-angular.zip");
    }
}

public record DownloadRequest(DesignDocument Design, List<AssetData>? Assets, string? Motion, string? Layout);
