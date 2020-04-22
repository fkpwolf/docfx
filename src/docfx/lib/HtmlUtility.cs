// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Web;
using HtmlAgilityPack;
using Markdig.Syntax;
using Newtonsoft.Json.Linq;

namespace Microsoft.Docs.Build
{
    internal static class HtmlUtility
    {
        private static readonly string[] s_allowedStyles = new[] { "text-align: right;", "text-align: left;", "text-align: center;" };

        public static HtmlNode LoadHtml(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc.DocumentNode;
        }

        public static HtmlNode PostMarkup(this HtmlNode node, bool dryRun)
        {
            return dryRun ? node : node.StripTags().RemoveRerunCodepenIframes();
        }

        public static long CountWord(this HtmlNode node)
        {
            // TODO: word count does not work for CJK locales...
            if (node.NodeType == HtmlNodeType.Comment)
            {
                return 0;
            }

            if (node is HtmlTextNode textNode)
            {
                return CountWordInText(textNode.Text);
            }

            var total = 0L;
            foreach (var child in node.ChildNodes)
            {
                total += CountWord(child);
            }
            return total;
        }

        public static HashSet<string> GetBookmarks(this HtmlNode html)
        {
            var result = new HashSet<string>();

            foreach (var node in html.DescendantsAndSelf())
            {
                var id = node.GetAttributeValue("id", "");
                if (!string.IsNullOrEmpty(id))
                {
                    result.Add(id);
                }
                var name = node.GetAttributeValue("name", "");
                if (!string.IsNullOrEmpty(name))
                {
                    result.Add(name);
                }
            }

            return result;
        }

        public static string TransformLinks(string html, Func<string, int, string> transform)
        {
            // Fast pass it does not have <a> tag or <img> tag
            if (!((html.Contains("<a", StringComparison.OrdinalIgnoreCase) && html.Contains("href", StringComparison.OrdinalIgnoreCase)) ||
                  (html.Contains("<img", StringComparison.OrdinalIgnoreCase) && html.Contains("src", StringComparison.OrdinalIgnoreCase))))
            {
                return html;
            }

            // <a>b</a> generates 3 inline markdown tokens: <a>, b, </a>.
            // `HtmlNode.OuterHtml` turns <a> into <a></a>, and generates <a></a>b</a> for the above input.
            // The following code ensures we preserve the original html when changing links.
            var pos = 0;
            var result = new StringBuilder(html.Length + 64);
            var reader = new HtmlReader(html);

            // TODO: remove this column offset hack while we have accurate line info for link in HTML block
            var columnOffset = 0;

            while (reader.Read())
            {
                if (reader.Type != HtmlTokenType.StartTag)
                {
                    continue;
                }

                var attributeName = reader.NameIs("a") ? "href" : reader.NameIs("img") ? "src" : null;
                if (attributeName is null)
                {
                    continue;
                }

                foreach (var attribute in reader.Attributes)
                {
                    if (!attribute.NameIs(attributeName))
                    {
                        continue;
                    }

                    var start = attribute.ValueRange.start;
                    if (start > pos)
                    {
                        result.Append(html, pos, start - pos);
                    }

                    var transformed = transform(HttpUtility.HtmlDecode(attribute.Value.ToString()), columnOffset);
                    if (!string.IsNullOrEmpty(transformed))
                    {
                        result.Append(HttpUtility.HtmlEncode(transformed));
                    }

                    pos = attribute.ValueRange.start + attribute.ValueRange.length;
                    columnOffset++;
                }
            }

            if (html.Length > pos)
            {
                result.Append(html, pos, html.Length - pos);
            }
            return result.ToString();
        }

        public static string TransformXref(string html, MarkdownObject block, Func<SourceInfo<string>?, SourceInfo<string>?, bool, (string? href, string display)> resolveXref)
        {
            // Fast pass it does not have <xref> tag
            if (!(html.Contains("<xref", StringComparison.OrdinalIgnoreCase)
                && (html.Contains("href", StringComparison.OrdinalIgnoreCase) || html.Contains("uid", StringComparison.OrdinalIgnoreCase))))
            {
                return html;
            }

            var pos = 0;
            var result = new StringBuilder(html.Length + 64);
            var reader = new HtmlReader(html);

            // TODO: remove this column offset hack while we have accurate line info for link in HTML block
            var columnOffset = 0;

            while (reader.Read())
            {
                if (!reader.NameIs("xref"))
                {
                    continue;
                }

                var start = reader.TokenRange.start;
                if (start > pos)
                {
                    result.Append(html, pos, start - pos);
                }

                if (reader.Type == HtmlTokenType.StartTag)
                {
                    var rawHtml = default(string);
                    var rawSource = default(string);
                    var href = default(string);
                    var uid = default(string);

                    foreach (var attribute in reader.Attributes)
                    {
                        if (attribute.NameIs("data-raw-html"))
                        {
                            rawHtml = HttpUtility.HtmlDecode(attribute.Value.ToString());
                        }
                        else if (attribute.NameIs("data-raw-source"))
                        {
                            rawSource = HttpUtility.HtmlDecode(attribute.Value.ToString());
                        }
                        else if (attribute.NameIs("href"))
                        {
                            href = HttpUtility.HtmlDecode(attribute.Value.ToString());
                        }
                        else if (attribute.NameIs("uid"))
                        {
                            uid = HttpUtility.HtmlDecode(attribute.Value.ToString());
                        }
                    }

                    var isShorthand = (rawHtml ?? rawSource)?.StartsWith("@") ?? false;

                    var (resolvedHref, display) = resolveXref(
                        href == null ? null : (SourceInfo<string>?)new SourceInfo<string>(href, block.ToSourceInfo(columnOffset: columnOffset)),
                        uid == null ? null : (SourceInfo<string>?)new SourceInfo<string>(uid, block.ToSourceInfo(columnOffset: columnOffset)),
                        isShorthand);

                    var resolvedNode = string.IsNullOrEmpty(resolvedHref)
                        ? rawHtml ?? rawSource ?? $"<span class=\"xref\">{(!string.IsNullOrEmpty(display) ? display : (href != null ? UrlUtility.SplitUrl(href).path : uid))}</span>"
                        : $"<a href='{HttpUtility.HtmlEncode(resolvedHref)}'>{HttpUtility.HtmlEncode(display)}</a>";

                    result.Append(resolvedNode);
                }

                pos = reader.TokenRange.start + reader.TokenRange.length;
                columnOffset++;
            }

            if (html.Length > pos)
            {
                result.Append(html, pos, html.Length - pos);
            }
            return result.ToString();
        }

        /// <summary>
        /// Get title and raw title, remove title node if all previous nodes are invisible
        /// </summary>
        public static bool TryExtractTitle(HtmlNode node, out string? title, [NotNullWhen(true)] out string? rawTitle)
        {
            var existVisibleNode = false;

            title = null;
            rawTitle = string.Empty;
            foreach (var child in node.ChildNodes)
            {
                if (!IsInvisibleNode(child))
                {
                    if (child.NodeType == HtmlNodeType.Element && (child.Name == "h1" || child.Name == "h2" || child.Name == "h3"))
                    {
                        title = string.IsNullOrEmpty(child.InnerText) ? null : HttpUtility.HtmlDecode(child.InnerText);

                        // NOTE: for backward compatibility during migration phase, the logic of title and raw title is different...
                        if (!existVisibleNode)
                        {
                            rawTitle = child.OuterHtml;
                            child.Remove();
                        }

                        return true;
                    }

                    existVisibleNode = true;
                }
            }

            return false;

            static bool IsInvisibleNode(HtmlNode n)
            {
                return n.NodeType == HtmlNodeType.Comment ||
                    (n.NodeType == HtmlNodeType.Text && string.IsNullOrWhiteSpace(n.OuterHtml));
            }
        }

        public static string CreateHtmlMetaTags(JObject metadata, ICollection<string> htmlMetaHidden, IReadOnlyDictionary<string, string> htmlMetaNames)
        {
            var result = new StringBuilder();

            foreach (var (key, value) in metadata)
            {
                if (value is null || value is JObject || htmlMetaHidden.Contains(key))
                {
                    continue;
                }

                var content = "";
                var name = htmlMetaNames.TryGetValue(key, out var diplayName) ? diplayName : key;

                if (value is JArray arr)
                {
                    foreach (var v in value)
                    {
                        if (v is JValue)
                        {
                            result.AppendLine($"<meta name=\"{Encode(name)}\" content=\"{Encode(v.ToString())}\" />");
                        }
                    }
                    continue;
                }
                else if (value.Type == JTokenType.Boolean)
                {
                    content = (bool)value ? "true" : "false";
                }
                else
                {
                    content = value.ToString();
                }

                result.AppendLine($"<meta name=\"{Encode(name)}\" content=\"{Encode(content)}\" />");
            }

            return result.ToString();
        }

        public static HtmlNode AddLinkType(this HtmlNode html, string locale)
        {
            AddLinkType(html, "a", "href", locale);
            AddLinkType(html, "img", "src", locale);
            return html;
        }

        /// <summary>
        /// Special HTML encode logic designed only for <see cref="CreateHtmlMetaTags"/>.
        /// </summary>
        internal static string Encode(string s)
        {
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&#39;");
        }

        internal static HtmlNode RemoveRerunCodepenIframes(this HtmlNode html)
        {
            // the rerun button on codepen iframes isn't accessible.
            // rather than get acc bugs or ban codepen, we're just hiding the rerun button using their iframe api
            foreach (var node in html.Descendants("iframe"))
            {
                var src = node.GetAttributeValue("src", null);
                if (src != null && src.Contains("codepen.io", StringComparison.OrdinalIgnoreCase))
                {
                    node.SetAttributeValue("src", src + "&rerun-position=hidden&");
                }
            }
            return html;
        }

        internal static HtmlNode StripTags(this HtmlNode html)
        {
            var nodesToRemove = new List<HtmlNode>();

            foreach (var node in html.DescendantsAndSelf())
            {
                if (node.Name.Equals("script", StringComparison.OrdinalIgnoreCase) ||
                    node.Name.Equals("link", StringComparison.OrdinalIgnoreCase) ||
                    node.Name.Equals("style", StringComparison.OrdinalIgnoreCase))
                {
                    nodesToRemove.Add(node);
                }
                else
                {
                    if (node.Name != "th" && node.Name != "td" && node.Attributes.Contains("style"))
                    {
                        var value = node.Attributes["style"].Value ?? "";
                        if (!s_allowedStyles.Any(l => l == value))
                        {
                            node.Attributes.Remove("style");
                        }
                    }
                }
            }

            foreach (var node in nodesToRemove)
            {
                node.Remove();
            }
            return html;
        }

        internal static List<Error> ScanDirtyNode(this HtmlNode html)
        {
            var errors = new List<Error>();

            foreach (var node in html.DescendantsAndSelf())
            {
                if (node.Name.Equals("script", StringComparison.OrdinalIgnoreCase) ||
                    node.Name.Equals("link", StringComparison.OrdinalIgnoreCase) ||
                    node.Name.Equals("style", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new Error(ErrorLevel.Warning, "html-embed", "html contain script tag"));
                }
                else
                {
                    if (node.Name != "th" && node.Name != "td" && node.Attributes.Contains("style"))
                    {
                        var value = node.Attributes["style"].Value ?? "";
                        if (!s_allowedStyles.Any(l => l == value))
                        {
                            node.Attributes.Remove("style");
                        }
                    }
                }
            }

            return errors;
        }

        private static void AddLinkType(this HtmlNode html, string tag, string attribute, string locale)
        {
            foreach (var node in html.Descendants(tag))
            {
                var href = node.GetAttributeValue(attribute, null);

                if (string.IsNullOrEmpty(href))
                {
                    continue;
                }

                switch (UrlUtility.GetLinkType(href))
                {
                    case LinkType.SelfBookmark:
                        node.SetAttributeValue("data-linktype", "self-bookmark");
                        break;
                    case LinkType.AbsolutePath:
                        node.SetAttributeValue("data-linktype", "absolute-path");
                        node.SetAttributeValue(attribute, AddLocaleIfMissing(href, locale));
                        break;
                    case LinkType.RelativePath:
                        node.SetAttributeValue("data-linktype", "relative-path");
                        break;
                    case LinkType.External:
                        node.SetAttributeValue("data-linktype", "external");
                        break;
                }
            }
        }

        private static string AddLocaleIfMissing(string href, string locale)
        {
            var pos = href.IndexOfAny(new[] { '/', '\\' }, 1);
            if (pos >= 1)
            {
                if (LocalizationUtility.IsValidLocale(href[1..pos]))
                {
                    return href;
                }
            }
            return '/' + locale + href;
        }

        private static int CountWordInText(string text)
        {
            var total = 0;
            var word = false;

            foreach (var ch in text)
            {
                if (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r')
                {
                    if (word)
                    {
                        word = false;
                        total++;
                    }
                }
                else if (
                    ch != '.' && ch != '?' && ch != '!' &&
                    ch != ';' && ch != ':' && ch != ',' &&
                    ch != '(' && ch != ')' && ch != '[' &&
                    ch != ']')
                {
                    word = true;
                }
            }

            if (word)
            {
                total++;
            }

            return total;
        }
    }
}
