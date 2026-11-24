// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Builds Vitomir's web site from site/: each page in site/pages is Markdown, laid into site/template.html with the menu
// site/site.json names, and site/assets is copied beside them. Plain static files, so any web host serves them (GitHub
// Pages does) and a browser opens them straight from the folder for a preview.
//
//   dotnet run tools/site/build.cs -- [--out <folder>] [--strict]
//
// --out: where the site goes (default out/site under the repository). --strict: refuse (exit 1) while any page still has
// a place for Whacker007's own words unfilled (a quote that starts "**Fill in"), so the published site never shows one.
#:package Markdig@0.42.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property InvariantGlobalization=true
#:property PublishAot=false

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;

string? outArg = null;
bool strict = false;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--out" && i + 1 < args.Length) outArg = args[++i];
    else if (args[i] == "--strict") strict = true;
    else { Console.Error.WriteLine($"site: unknown switch {args[i]} (--out <folder>, --strict)"); return 2; }
}

string repo = FindRepo(Directory.GetCurrentDirectory());
string site = Path.Combine(repo, "site");
string output = Path.GetFullPath(outArg ?? Path.Combine(repo, "out", "site"));
if (output.StartsWith(site + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || output == site)
{
    Console.Error.WriteLine("site: the output must not be inside site/");
    return 2;
}

var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(site, "site.json")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }).RootElement;
var pages = config.GetProperty("pages").EnumerateArray()
    .Select(p => (File: p.GetProperty("file").GetString()!, Menu: p.GetProperty("menu").GetString()!, Description: p.GetProperty("description").GetString()!))
    .ToList();
string baseUrl = config.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";   // the site's own address
string template = File.ReadAllText(Path.Combine(site, "template.html"));
var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
Directory.CreateDirectory(output);

int unfilled = 0;
foreach (var page in pages)
{
    string source = File.ReadAllText(Path.Combine(site, "pages", page.File + ".md"));
    string title = Regex.Match(source, @"^# (.+)$", RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value.Trim() : page.Menu;
    int here = Regex.Matches(source, @"^> \*\*Fill in", RegexOptions.Multiline).Count;
    if (here > 0) Console.WriteLine($"site: {page.File}.md has {here} place(s) still to fill in");
    unfilled += here;

    // Links between pages are written as page.md, so they work on GitHub too; the site's own files are page.html.
    string body = Markdown.ToHtml(source, pipeline);
    body = Regex.Replace(body, @"href=""([a-z0-9-]+)\.md(#[^""]*)?""", m2 => $"href=\"{m2.Groups[1].Value}.html{m2.Groups[2].Value}\"");

    var menu = new StringBuilder();
    foreach (var p in pages)
        menu.Append(p.File == page.File
            ? $"<a href=\"{p.File}.html\" aria-current=\"page\">{Html(p.Menu)}</a>"
            : $"<a href=\"{p.File}.html\">{Html(p.Menu)}</a>");

    string html = template
        .Replace("{{title}}", Html(page.File == "index" ? "Vitomir" : title + " – Vitomir"))
        .Replace("{{description}}", Html(page.Description))
        .Replace("{{menu}}", menu.ToString())
        .Replace("{{page}}", page.File)
        .Replace("{{url}}", Html(baseUrl))
        .Replace("{{address}}", Html(baseUrl + (page.File == "index" ? "" : page.File + ".html")))
        .Replace("{{content}}", body);
    File.WriteAllText(Path.Combine(output, page.File + ".html"), html, new UTF8Encoding(false));
}

foreach (string file in Directory.EnumerateFiles(Path.Combine(site, "assets"), "*", SearchOption.AllDirectories))
{
    string to = Path.Combine(output, "assets", Path.GetRelativePath(Path.Combine(site, "assets"), file));
    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
    File.Copy(file, to, overwrite: true);
}
// GitHub Pages runs Jekyll on a plain upload unless told not to; the site is already built.
File.WriteAllText(Path.Combine(output, ".nojekyll"), "");

Console.WriteLine($"site: {pages.Count} pages into {output}" + (unfilled > 0 ? $"; {unfilled} place(s) still to fill in" : ""));
if (strict && unfilled > 0)
{
    Console.Error.WriteLine("site: refused (--strict): fill in every place first");
    return 1;
}
return 0;

static string FindRepo(string from)
{
    for (var d = new DirectoryInfo(from); d is not null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, "site")) && File.Exists(Path.Combine(d.FullName, "site", "site.json"))) return d.FullName;
    throw new InvalidOperationException("site: run this from the repository (no site/site.json found above the current folder)");
}

static string Html(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
