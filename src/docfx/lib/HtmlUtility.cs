// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Web;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;

#nullable enable

namespace Microsoft.Docs.Build
{
    internal static class HtmlUtility
    {
        private static readonly Func<HtmlAttribute, int> s_getValueStartIndex =
            ReflectionUtility.CreateInstanceFieldGetter<HtmlAttribute, int>("_valuestartindex");

        private static readonly HashSet<string> s_allowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // https://developer.mozilla.org/en-US/docs/Web/HTML/Element
            "a", "abbr", "acronym", "address", "area", "b",
            "big", "blockquote", "br", "caption", "center", "cite",
            "code", "col", "colgroup", "dd", "del", "dfn", "dir", "div", "dl", "dt",
            "em", "h1", "h2", "h3", "h4", "h5", "h6",
            "hr", "i", "iframe", "img", "ins", "label", "legend", "li", "map",
            "ol", "p", "pre", "q", "s", "samp",
            "small", "span", "strike", "strong", "sub", "sup", "table",
            "tbody", "td", "tfoot", "th", "thead", "tr", "tt", "u",
            "ul", "var",
            "section", "nav", "article", "aside", "header", "footer",
            "figure", "figcaption",
            "data", "time", "mark", "ruby", "rt", "rp", "bdi", "wbr",
            "details", "summary",

            // docs specific tags
            "image",
        };

        private static readonly HashSet<string> s_allowedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // https://developer.mozilla.org/en-US/docs/Web/HTML/Attributes
            "align", "alt", "cite", "class", "colspan", "datetime", "decoding", "dir", "download", "headers", "height", "hidden",
            "href", "hreflang", "id", "name", "ping", "rel", "reversed", "rowspan", "scope", "shape", "sizes", "span", "spellcheck",
            "src", "srcset", "start", "summary", "tabindex", "target", "title", "translate", "value", "width",
            "frameborder", "allowfullscreen",

            // docs specific attributes
            "aria-controls", "aria-hidden", "aria-selected", "role", "highlight-lines", "renderon",
        };

        private static readonly HashSet<string> s_allowedTableStyles = new HashSet<string>
        {
            "text-align: right;", "text-align: left;", "text-align: center;",
        };

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
                return 0;

            if (node is HtmlTextNode textNode)
                return CountWordInText(textNode.Text);

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

        public static HtmlNode TransformLinks(HtmlNode html, Func<string, int, string> transform)
        {
            var columnOffset = 0;
            foreach (var node in html.Descendants())
            {
                var link = node.Name == "a" ? node.Attributes["href"]
                    : node.Name == "img" ? node.Attributes["src"]
                    : null;

                if (link is null)
                {
                    continue;
                }

                var transformed = transform(HttpUtility.HtmlDecode(link.Value), columnOffset);

                if (!string.IsNullOrEmpty(transformed))
                    link.Value = HttpUtility.HtmlEncode(transformed);
            }

            return html;
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
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var pos = 0;
            var result = new StringBuilder(html.Length + 64);

            // TODO: remove this column offset hack while we have accurate line info for link in HTML block
            var columnOffset = 0;
            foreach (var node in doc.DocumentNode.Descendants())
            {
                var link = node.Name == "a" ? node.Attributes["href"]
                         : node.Name == "img" ? node.Attributes["src"]
                         : null;

                if (link is null)
                {
                    continue;
                }

                var valueStartIndex = s_getValueStartIndex(link);
                if (valueStartIndex > pos)
                {
                    result.Append(html, pos, valueStartIndex - pos);
                }
                var transformed = transform(HttpUtility.HtmlDecode(link.Value), columnOffset);
                if (!string.IsNullOrEmpty(transformed))
                {
                    result.Append(HttpUtility.HtmlEncode(transformed));
                }
                pos = valueStartIndex + link.Value.Length;
                columnOffset++;
            }

            if (html.Length > pos)
            {
                result.Append(html, pos, html.Length - pos);
            }
            return result.ToString();
        }

        public static string TransformXref(string html, Func<string, bool, int, (string href, string display)> transform)
        {
            // Fast pass it does not have <xref> tag
            if (!(html.Contains("<xref", StringComparison.OrdinalIgnoreCase) && html.Contains("href", StringComparison.OrdinalIgnoreCase)))
            {
                return html;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // TODO: remove this column offset hack while we have accurate line info for link in HTML block
            var columnOffset = 0;
            var replacingNodes = new List<(HtmlNode, HtmlNode)>();
            foreach (var node in doc.DocumentNode.Descendants())
            {
                if (node.Name != "xref")
                {
                    continue;
                }

                var xref = HttpUtility.HtmlDecode(node.GetAttributeValue("href", ""));

                var raw = HttpUtility.HtmlDecode(
                    node.GetAttributeValue("data-raw-html", null) ?? node.GetAttributeValue("data-raw-source", null) ?? $"<span class=\"xref\">{HttpUtility.HtmlEncode(UrlUtility.SplitUrl(xref).path)}</span>");

                var isShorthand = raw.StartsWith("@");

                var (resolvedHref, display) = transform(xref, isShorthand, columnOffset);

                var resolvedNode = new HtmlDocument();
                if (string.IsNullOrEmpty(resolvedHref))
                {
                    resolvedNode.LoadHtml(raw);
                }
                else
                {
                    resolvedNode.LoadHtml($"<a href='{HttpUtility.HtmlEncode(resolvedHref)}'>{HttpUtility.HtmlEncode(display)}</a>");
                }
                replacingNodes.Add((node, resolvedNode.DocumentNode));
                columnOffset++;
            }

            foreach (var (node, resolvedNode) in replacingNodes)
            {
                node.ParentNode.ReplaceChild(resolvedNode, node);
            }

            return doc.DocumentNode.WriteTo();
        }

        /// <summary>
        /// Get title and raw title, remove title node if all previous nodes are invisible
        /// </summary>
        public static bool TryExtractTitle(HtmlNode node, [NotNullWhen(true)] out string? title, [NotNullWhen(true)] out string? rawTitle)
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
            // the rerun button on codepen iframes isn't accessibile.
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
            var attributesToRemove = new List<HtmlAttribute>();

            foreach (var node in html.DescendantsAndSelf())
            {
                if (node.NodeType != HtmlNodeType.Element)
                {
                    continue;
                }

                if (!s_allowedTags.Contains(node.Name))
                {
                    nodesToRemove.Add(node);
                }
                else
                {
                    attributesToRemove.Clear();
                    foreach (var attribute in node.Attributes)
                    {
                        if (attribute.Name.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        else if (string.Equals(attribute.Name, "style"))
                        {
                            if ((node.Name != "th" && node.Name != "td") || !s_allowedTableStyles.Contains(attribute.Value))
                            {
                                attributesToRemove.Add(attribute);
                            }
                        }
                        else if (!s_allowedAttributes.Contains(attribute.Name))
                        {
                            attributesToRemove.Add(attribute);
                        }
                    }

                    foreach (var attribute in attributesToRemove)
                    {
                        node.Attributes.Remove(attribute);
                    }
                }
            }

            foreach (var node in nodesToRemove)
            {
                node.Remove();
            }
            return html;
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
                if (ch == ' ' || ch == '\t' || ch == '\n')
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
