using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DesignToAngular.Domain.Design;

namespace DesignToAngular.Services.CodeGen;

public record FormField(string Name, string Entity, string TsType, string Initial, bool Required, bool IsEmail);

/// <summary>Renders the node tree as HTML. angular=true emits Angular template syntax, false emits a standalone preview.</summary>
public class HtmlBuilder(DesignDocument doc, Dictionary<DesignNode, string> names, Dictionary<DesignNode, GeneratedAsset> assets, MotionPlan plan, bool angular)
{
    private const string PlaceholderImage =
        "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='400' height='300'%3E%3C/svg%3E";

    private readonly Dictionary<DesignNode, string> _labelFor = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<DesignNode> _labeled = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _usedFieldNames = new();
    private bool _h1Used;

    public List<FormField> Fields { get; } = new();
    public bool HasForm { get; private set; }
    public bool NeedsShowPassword { get; private set; }
    public bool UsesReveal { get; private set; }

    public string Build()
    {
        var sb = new StringBuilder();
        Write(sb, doc.Root, 0, false);
        return sb.ToString();
    }

    /// <summary>Inner HTML of a text element: its runs (differently styled parts) or its plain text.</summary>
    private string Inner(DesignNode n, string cls, string? fallback)
    {
        var runs = n.Content?.Runs;
        if (runs is { Count: > 0 })
            return string.Concat(runs.Select((r, i) => $"<span class=\"{cls}-run-{i}\">{Text(r.Text)}</span>"));
        return Text(fallback);
    }

    private string Text(string? s)
    {
        var e = WebUtility.HtmlEncode(s ?? "");
        if (angular) e = e.Replace("@", "&#64;").Replace("{", "&#123;").Replace("}", "&#125;");
        return e;
    }

    private void WriteChildren(StringBuilder sb, DesignNode n, int depth, bool inForm, bool wrapLi = false)
    {
        var kids = NodeRules.Kids(n);
        for (var i = 0; i < kids.Count; i++)
        {
            var k = kids[i];
            if (k.Type == "text" && (k.Role ?? "").Contains("label", StringComparison.OrdinalIgnoreCase) && i + 1 < kids.Count)
            {
                var target = NodeRules.FirstInput(kids[i + 1]);
                if (target != null)
                {
                    _labelFor[k] = names[target];
                    _labeled.Add(target);
                }
            }

            if (wrapLi)
            {
                var pad = new string(' ', (depth) * 2);
                sb.AppendLine($"{pad}<li>");
                Write(sb, k, depth + 1, inForm);
                sb.AppendLine($"{pad}</li>");
            }
            else
            {
                Write(sb, k, depth, inForm);
            }
        }
    }

    private void Write(StringBuilder sb, DesignNode n, int depth, bool inForm)
    {
        var pad = new string(' ', depth * 2);
        var cls = names[n];
        var a = new StringBuilder($"class=\"{cls}\"");
        if (ReferenceEquals(n, doc.Root) && doc.Meta.Direction == "rtl") a.Append(" dir=\"rtl\"");
        if (plan.Reveal.TryGetValue(n, out var motion))
        {
            a.Append($" data-reveal=\"{motion.Kind}\"");
            if (motion.DelayMs > 0) a.Append($" data-reveal-delay=\"{motion.DelayMs}\"");
            UsesReveal = true;
        }
        var c = n.Content;

        switch (n.Type)
        {
            case "heading":
            {
                var tag = _h1Used ? "h2" : "h1";
                _h1Used = true;
                sb.AppendLine($"{pad}<{tag} {a}>{Inner(n, cls, c?.Text)}</{tag}>");
                break;
            }
            case "text":
            {
                if (_labelFor.TryGetValue(n, out var forId))
                    sb.AppendLine($"{pad}<label {a} for=\"{forId}\">{Inner(n, cls, c?.Text ?? c?.Label)}</label>");
                else
                    sb.AppendLine($"{pad}<p {a}>{Inner(n, cls, c?.Text ?? c?.Label)}</p>");
                break;
            }
            case "button":
            {
                var submit = inForm && n.Data?.Action == "submit";
                var extra = submit ? "type=\"submit\"" : "type=\"button\"";
                if (angular && submit) extra += " [disabled]=\"loading\"";
                sb.AppendLine($"{pad}<button {a} {extra}>{Inner(n, cls, c?.Text ?? c?.Label)}</button>");
                break;
            }
            case "input":
            case "textarea":
            case "select":
            {
                var type = c?.InputType ?? "text";
                var name = inForm ? RegisterField(n, type) : FieldName(n);
                var attrs = new StringBuilder(a.ToString());
                if (_labeled.Contains(n)) attrs.Append($" id=\"{cls}\"");
                if (n.Type == "input")
                {
                    if (angular && inForm && type == "password")
                    {
                        attrs.Append(" [type]=\"showPassword ? 'text' : 'password'\"");
                        NeedsShowPassword = true;
                    }
                    else attrs.Append($" type=\"{type}\"");
                    if (!angular && type == "password") attrs.Append(" data-pw=\"1\"");
                    if (type == "email") attrs.Append(" autocomplete=\"email\"");
                    if (type == "password") attrs.Append(" autocomplete=\"current-password\"");
                }
                if (!string.IsNullOrEmpty(c?.Placeholder) && n.Type != "select")
                    attrs.Append($" placeholder=\"{Text(c.Placeholder)}\"");
                if (c?.Required == true) attrs.Append(" required");
                attrs.Append(angular && inForm ? $" formControlName=\"{name}\"" : $" name=\"{name}\"");

                if (n.Type == "input") sb.AppendLine($"{pad}<input {attrs} />");
                else if (n.Type == "textarea") sb.AppendLine($"{pad}<textarea {attrs}></textarea>");
                else
                {
                    sb.AppendLine($"{pad}<select {attrs}>");
                    sb.AppendLine($"{pad}  <option value=\"\">{Text(c?.Placeholder ?? "Select an option")}</option>");
                    sb.AppendLine($"{pad}  <!-- TODO: add your options -->");
                    sb.AppendLine($"{pad}</select>");
                }
                break;
            }
            case "checkbox":
            {
                var name = inForm ? RegisterField(n, "checkbox") : FieldName(n);
                var bind = angular && inForm ? $"formControlName=\"{name}\"" : $"name=\"{name}\"";
                sb.AppendLine($"{pad}<label {a}>");
                sb.AppendLine($"{pad}  <input type=\"checkbox\" {bind} />");
                sb.AppendLine($"{pad}  <span>{Text(c?.Label ?? c?.Text)}</span>");
                sb.AppendLine($"{pad}</label>");
                break;
            }
            case "link":
                sb.AppendLine($"{pad}<a {a} href=\"{Text(CssSafe.Href(c?.Href))}\">{Inner(n, cls, c?.Text ?? c?.Label)}</a>");
                break;
            case "image":
            {
                var src = assets.TryGetValue(n, out var asset) ? $"assets/images/{asset.FileName}" : PlaceholderImage;
                if (asset != null && plan.Marquee.Contains(n))
                {
                    // A row of logos that scrolls sideways: the picture twice, so the loop has no gap.
                    sb.AppendLine($"{pad}<div class=\"{cls}-marquee\">");
                    sb.AppendLine($"{pad}  <div class=\"{cls}-marquee-track\">");
                    sb.AppendLine($"{pad}    <img {a} src=\"{src}\" alt=\"{Text(c?.ImageDescription)}\" />");
                    sb.AppendLine($"{pad}    <img class=\"{cls}\" src=\"{src}\" alt=\"\" aria-hidden=\"true\" />");
                    sb.AppendLine($"{pad}  </div>");
                    sb.AppendLine($"{pad}</div>");
                    break;
                }
                sb.AppendLine($"{pad}<img {a} src=\"{src}\" alt=\"{Text(c?.ImageDescription)}\" />");
                break;
            }
            case "divider":
                sb.AppendLine($"{pad}<hr {a} />");
                break;
            case "icon":
            {
                if (assets.TryGetValue(n, out var iconAsset))
                {
                    // Not in the icon set (a brand logo, for example): use the picture cropped from the design.
                    sb.AppendLine($"{pad}<img {a} src=\"assets/images/{iconAsset.FileName}\" alt=\"\" />");
                    break;
                }

                var svg = IconLibrary.Render(c?.IconName, c?.IconStyle);
                if (NodeRules.IsToggle(n))
                {
                    string click;
                    if (angular)
                    {
                        click = "(click)=\"showPassword = !showPassword\"";
                        NeedsShowPassword = true;
                    }
                    else
                    {
                        click = "onclick=\"var i=document.querySelector('[data-pw]');if(i){i.type=i.type==='password'?'text':'password'}\"";
                    }
                    sb.AppendLine($"{pad}<button {a} type=\"button\" aria-label=\"Toggle password visibility\" {click}>{svg}</button>");
                }
                else
                {
                    sb.AppendLine($"{pad}<span {a} aria-hidden=\"true\">{svg}</span>");
                }
                break;
            }
            case "form":
            {
                HasForm = true;
                var attrs = angular
                    ? $"{a} [formGroup]=\"form\" (ngSubmit)=\"onSubmit()\" novalidate"
                    : $"{a} onsubmit=\"return false\"";
                sb.AppendLine($"{pad}<form {attrs}>");
                WriteChildren(sb, n, depth + 1, true);
                sb.AppendLine($"{pad}</form>");
                break;
            }
            case "list":
                sb.AppendLine($"{pad}<ul {a}>");
                WriteChildren(sb, n, depth + 1, inForm, wrapLi: true);
                sb.AppendLine($"{pad}</ul>");
                break;
            default:
            {
                var tag = n.Type switch { "section" => "section", "navbar" => "nav", "footer" => "footer", _ => "div" };
                if (NodeRules.Kids(n).Count == 0 && n.Type != "table")
                {
                    sb.AppendLine($"{pad}<{tag} {a}></{tag}>");
                    break;
                }
                sb.AppendLine($"{pad}<{tag} {a}>");
                if (n.Type == "table") sb.AppendLine($"{pad}  <!-- TODO: tables are not supported yet -->");
                WriteChildren(sb, n, depth + 1, inForm);
                sb.AppendLine($"{pad}</{tag}>");
                break;
            }
        }
    }

    private string FieldName(DesignNode n)
    {
        var raw = n.Data?.Field;
        if (string.IsNullOrWhiteSpace(raw))
            raw = Regex.Replace(n.Id, "(_?(input|field|textarea|select|checkbox))$", "", RegexOptions.IgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) raw = n.Id;
        var name = Naming.Camel(raw);
        var unique = name;
        var i = 2;
        while (!_usedFieldNames.Add(unique)) unique = $"{name}{i++}";
        return unique;
    }

    private string RegisterField(DesignNode n, string inputType)
    {
        var name = FieldName(n);
        var tsType =
            inputType == "checkbox" || n.Data?.FieldType == "boolean" ? "boolean" :
            inputType == "number" || n.Data?.FieldType == "number" ? "number" : "string";
        var initial = tsType switch { "boolean" => "false", "number" => "0", _ => "''" };
        var entity = Naming.Pascal(n.Data?.Entity ?? "FormData");
        Fields.Add(new FormField(name, entity, tsType, initial, n.Content?.Required == true, inputType == "email"));
        return name;
    }
}
