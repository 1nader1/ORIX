using System.Text.Json.Serialization;

namespace DesignToAngular.Domain.Design;

public record DesignDocument(
    Meta Meta,
    Tokens Tokens,
    DesignNode Root);

public record Meta(
    string PageType,
    string Direction,
    string Language,
    Viewport? Viewport = null,
    double? Confidence = null,
    bool? HasMockupFrame = null,
    int? DesignWidth = null,
    int? ImageWidth = null,
    int? ImageHeight = null);

public record Viewport(int Width, int Height);

public record Tokens(
    Dictionary<string, string> Colors,
    Typography Typography,
    Spacing Spacing,
    Dictionary<string, double> Radius,
    Dictionary<string, string>? Shadows = null);

public record Typography(
    string? FontFamilyHeading,
    string? FontFamilyBody,
    Dictionary<string, double>? Scale);

public record Spacing(int Unit = 8);

public record DesignNode(
    string Id,
    string Type,
    string? Role = null,
    LayoutInfo? Layout = null,
    StyleInfo? Style = null,
    ContentInfo? Content = null,
    DataHint? Data = null,
    List<DesignNode>? Children = null,
    int[]? Box = null);

public record LayoutInfo(
    string? Display = null,
    string? Direction = null,
    double? Gap = null,
    string? Align = null,
    string? Justify = null,
    int? Columns = null,
    double[]? Padding = null,
    double[]? Margin = null,
    string? Width = null,
    string? Height = null,
    string? MaxWidth = null,
    string? Position = null,
    string? Top = null,
    string? Right = null,
    string? Bottom = null,
    string? Left = null,
    int? ZIndex = null,
    double? Rotate = null,
    string? Overflow = null,
    string? AspectRatio = null,
    string? MinHeight = null);

public record StyleInfo(
    string? Background = null,
    string? Color = null,
    string? PlaceholderColor = null,
    string? FontSize = null,
    int? FontWeight = null,
    string? TextAlign = null,
    string? Radius = null,
    string? Border = null,
    string? Shadow = null,
    double? Opacity = null,
    string? FontFamily = null,
    string? LetterSpacing = null,
    string? LineHeight = null,
    string? TextTransform = null,
    bool? Italic = null);

public record ContentInfo(
    string? Text = null,
    string? Label = null,
    string? Placeholder = null,
    string? Href = null,
    string? InputType = null,
    bool? Required = null,
    string? ImageDescription = null,
    string? IconName = null,
    string? IconStyle = null,
    int[]? ImageBox = null,
    List<TextRun>? Runs = null);

/// <summary>A piece of a text with its own look, e.g. one green word inside a black heading.</summary>
public record TextRun(
    string Text,
    string? Color = null,
    int? FontWeight = null,
    bool? Italic = null,
    string? Background = null);

public record DataHint(
    string? Entity = null,
    string? Field = null,
    string? FieldType = null,
    bool? Repeated = null,
    string? Action = null);
