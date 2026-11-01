using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesignToAngular.Domain.Design;

namespace DesignToAngular.Services;

public class DesignAnalyzer(HttpClient http, IConfiguration config)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // The model sometimes writes numbers as text ("700", "1.1").
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private const string Instructions = """
        You are a senior UI analyst and a precise visual designer. Look at the UI design image and
        describe it as a JSON document that follows the JSON Schema below EXACTLY.

        OUTPUT RULES
        - Output ONLY raw JSON. No markdown, no explanation.
        - OMIT any field that is unknown or not applicable. NEVER write null, empty strings or empty objects.
        - Keep the node tree as simple as possible. Do not invent elements that are not in the image.
        - Give every node a unique snake_case id and a meaningful role.

        LAYOUT STRUCTURE (the most important part)
        - Build the layout exactly as a developer would. For EVERY container with two or more children set
          layout.display ("flex" or "grid") and layout.direction ("row" or "column"), plus gap, align and justify.
        - Children that sit side by side belong in a "row"; children stacked top to bottom belong in a "column".
        - Page sections are full-width containers (layout.width "100%"). Content blocks that are narrower than the
          page get their own width or maxWidth.
        - The boxes below are measurements only. NEVER use them to position elements and never use
          position "absolute" for content.

        BOXES
        - Every node gets "box" = [yMin, xMin, yMax, xMax], integers from 0 to 1000, relative to the FULL uploaded
          image, tight around that element (a picture: just the picture; a card: the whole card; text: the text block).
        - root.box is the area of the actual page: inside any mockup frame, or the whole image [0,0,1000,1000] when
          there is no frame.

        COLORS
        - A measured palette (real pixel colors of this image) may be given in the user message. Use those
          exact hex values for tokens.colors whenever a color of the design matches one of them. Never invent
          a hex that is not close to a measured color. Small elements (buttons, badges, accents) have a small share.
        - Give DIFFERENT roles their own color entries (text, textMuted, label, placeholder, border, accent...)
          instead of reusing one color. Icons get their own color if it differs from the text.
        - A gradient background is written in style.background as a CSS gradient that uses token colors, for example
          linear-gradient(180deg, $colors.gradientStart, $colors.gradientEnd). Add those colors to tokens.colors.
        - Reference tokens in nodes as "$colors.<name>", "$radius.<name>", "$shadows.<name>".

        WHAT TO DESCRIBE
        - IGNORE presentation frames: browser windows, device mockups, drop-shadow backdrops and any outer
          background that is only there to display the design. Describe only the design inside. If you ignored a
          frame, set meta.hasMockupFrame = true; otherwise leave it out. The root background is the background of
          the actual page, not of the presentation backdrop.
        - Estimate real pixel sizes as if the design were shown at its natural size (set meta.designWidth, e.g.
          1440 desktop / 390 mobile) and keep proportions consistent between elements.

        SHAPES
        - tokens.radius must contain several sizes: sm (inputs), md, lg (cards), full = 9999 (pills). Choose the
          radius of EACH element separately by looking at it.
        - If an element has a visible shadow, put a full CSS box-shadow in tokens.shadows (offset, blur, color with
          opacity) estimated from the image.
        - For every input, select, textarea and button set layout.height and layout.padding.
        - For icons set content.iconName and content.iconStyle ("outline" or "filled") and style.color.

        LAYOUT AND EFFECTS
        - Normal content flows with flex or grid. Use layout.position = "absolute" (with top/left/right/bottom and
          zIndex) ONLY for decorative or overlapping elements such as floating shapes, stickers or badges.
        - A tilted element gets layout.rotate in degrees (negative = counter-clockwise).
        - A wrapper whose children bleed past its edges gets layout.overflow = "hidden".

        TEXT AND FONTS
        - Copy every text EXACTLY as shown, including punctuation, typos, capitalization and Arabic text. Never
          correct or rewrite the copy. Set meta.direction according to the text direction.
        - If one text has parts with a different color, weight or highlight (for example one green word in a black
          heading), write content.runs instead of content.text. The runs must concatenate to the full text,
          INCLUDING the spaces between words.
        - Identify real fonts: set tokens.typography.fontFamilyHeading and fontFamilyBody to the closest Google Fonts
          family (for example Inter, Poppins, DM Sans). If one element uses a different display font (a heavy
          condensed headline, a script), set style.fontFamily on that element, plus letterSpacing, lineHeight and
          textTransform when they are visible.

        IMAGES, ICONS AND ILLUSTRATIONS
        - Every photo, illustration, logo, mascot or decorative graphic that cannot be built with simple CSS MUST
          be an "image" node (with a tight box). Do NOT describe its inner shapes, and never give it a dark
          placeholder background.
        - A row of logos (for example partner brands) is ONE image node covering the whole row.
        - Social network icons and every other brand logo are NOT icons: make them image nodes with a box.
        - Simple interface glyphs are "icon" nodes. content.iconName MUST be a Lucide icon name in kebab-case, for
          example arrow-right, menu, search, x, check, eye, heart, star, play, mail, phone, map-pin, megaphone,
          share-2, shopping-cart, sparkles, zap, chevron-down. Set content.iconStyle and style.color.

        DATA
        - Fill the "data" hints (entity, field, fieldType, action) for forms, lists and buttons.

        JSON Schema:
        """;

    public async Task<DesignDocument> AnalyzeAsync(
        byte[] image, string mediaType, IReadOnlyList<PaletteColor>? palette, int? part, int? parts, CancellationToken ct)
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "Resources", "design-schema.json");
        var schema = await File.ReadAllTextAsync(schemaPath, ct);

        var model = config["Gemini:Model"] ?? "gemini-3.8-flash";
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", config["Gemini:ApiKey"]);
        request.Content = JsonContent.Create(new
        {
            system_instruction = new
            {
                parts = new[] { new { text = Instructions + "\n" + schema } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { inline_data = new { mime_type = mediaType, data = Convert.ToBase64String(image) } },
                        new { text = UserText(palette, part, parts) }
                    }
                }
            },
            // Gemini 3 models work best with their default temperature, so it is left out on purpose.
            generationConfig = new
            {
                responseMimeType = "application/json",
                maxOutputTokens = 16000
            }
        });

        var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"AI request failed: {(int)response.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "";

        text = text.Trim();
        if (text.StartsWith("```"))
            text = text[(text.IndexOf('\n') + 1)..text.LastIndexOf("```")].Trim();

        var design = JsonSerializer.Deserialize<DesignDocument>(text, JsonOpts)
                     ?? throw new InvalidOperationException("Empty design document.");

        return ColorSnapper.Snap(design, palette);
    }

    private static string UserText(IReadOnlyList<PaletteColor>? palette, int? part, int? parts)
    {
        var sb = new StringBuilder("Analyze this design and return the JSON.");
        if (parts is > 1 && part is >= 1 && part <= parts)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine($"This image is section {part} of {parts} of one long page, in order from top to bottom. " +
                          "Describe only what is visible in this image. The root node is this section (a container). " +
                          "Do not invent a navigation bar, hero or footer that is not visible here. " +
                          "All boxes are relative to THIS image.");
        }
        if (palette is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Measured palette from the real pixels of this image (hex, share of pixels):");
            foreach (var p in palette)
                sb.AppendLine($"{p.Hex} ({(p.Share * 100).ToString("0.0", CultureInfo.InvariantCulture)}%)");
        }
        return sb.ToString();
    }
}
