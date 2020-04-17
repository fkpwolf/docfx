# validation for HTML sanitization

[Feature 192183](https://dev.azure.com/ceapex/Engineering/_workitems/edit/192183)

## Overview

Content authors can embed arbitrary HTML tags to markdown documents. These HTML tags could contain `<script>` tag that allows arbitrary script execution, or `style` attribute that compromises site visual experience.

### Goals

- Find HTML tags and attributes from user content that may alter site style
- Find HTML tags and attributes from user content that may allow javascript execution
- Report warning on these tags and attributes
- Apply HTML sanitization consistently for all user content

### Out of scope

- Refactor HTML processing pipeline

## Technical Design

The HTML processing pipeline in build gives us a chance to consistently apply certain HTML transformations to user input. There are two approaches to filter HTML tags and HTML attributes, _allow list_ vs _disallow list_.
The current HTML sanitizer in docfx uses _disallow list_ to strip out `<script>`, `<link>`, `<style>` tags and `style` attributes. This alone won't provide enough protection against javascript injection. 
The preferred approach is to use _allow list_ for both HTML tags and attributes, and this is what most HTML sanitizers do. 

In `HtmlUtility.PostMarkup` method, add a task to find all tags and attributes, consolidate into one error and at last return HtmlNode and the error.
In `BuildPage.LoadMarkdown` method, consume this kind of error, since we just need to process MarkDown page type.
Or in `HtmlUtility`, create new method `ScanDirtyNode` and only `BuildPage.LoadMarkdown` will invoke this method.

The warning message will be like “HTML ‘{tag1, tage2}’ isn’t allowed. Disallowed HTML poses a security risk and must be replaced with approved Docs Markdown syntax.” 

#### Sanitize HTML Tags

Using _allow list_ for HTML tags is very likely to cause content visual diffs. Sometimes writers surround text (especially variables) with angle bracket not knowing they are treated as HTML tags. These tags can appear as a standalone tag (without closing tag) and be parsed into an incorrect HTML DOM tree. Removing these tags can cause a huge chunk of content to be stripped from output.

The danger of using _allow list_ for HTML tags overweight its advantage at this time period, so we keep using the current _disallow list_ approach for HTML tags. The disallowed HTML tags are:

- `<script>`
- `<link>`
- `<style>`

#### Sanitize HTML attribute

For HTML attributes, we _MUST_ use _allow list_ because attributes that allow javascript execution is an open set.

For reference, https://developer.mozilla.org/en-US/docs/Web/Events lists all the possible DOM events, some of them are standard events, but it also contains lots of non-standard events that are browser specific. Some non-standard events are prefixed with `moz`, but others aren't like `dataError`.

The HTML attribute allow list is defined as the sum of:

- Standard HTML5 attributes for allowed HTML tag names.
- Attribute names starting with `data-`.
- Accessibility attributes: `role` and names starting with `aria-`.
- Existing attribute names respected by `docs-ui` that does not have `data-` prefix: `highlight-lines`

> ‼️ Due to how the HTML processing pipeline works today, we cannot tell _user HTML_ from _system generated HTML_, so this sanitizer works for both. 
All new `docs-ui` features that depend on build output with custom HTML attributes __MUST__ use `data-` prefix, overwise they'll be stripped by the sanitizer. 

#### Sanitize URL

To prevent script execution like `<a href="javascript://void">`, only `http://` or `https://` are allowed URL schemas for all _external_ links. Absolute URLs (like `/azure`) and relative URLs (like `azure`) are not sanitized.

## Dependencies

The rollout of this feature impact v3 migration tool since there are likely HTML diffs.
We can either make the change simultaneously in v2 and v3, or change the v3 migration tool to ignore these diffs.

## Appendix

#### HTML5 attribute name allowlist

- This list is based on https://developer.mozilla.org/en-US/docs/Web/HTML/Element.
- This list excludes experimental and obsolete attributes.
- This list excludes user interactive DOM elements and attributes (like `<button>`, `<menu>`).
- This list excludes media DOM elements (like `<video>`, `<audio>`).
- This list excludes DOM elements that affect main site structure (like `<body>` and `<header>`)

**Global attributes**

```html
class, dir, hidden, id,
itemid, itemprop, itemref, itemscope, itemtype,
lang, part, slot, spellcheck, tabindex, title
```

**Attributes by element name**

```html
<!-- Content sectioning -->
<address>: 
<h1>-<h6>: 
<section>:

<!-- Text content -->
<blockquote>: cite
<dd>:
<div>:
<dl>:
<dt>:
<figcaption>:
<figure>:
<hr>:
<li>: value
<ol>: reversed, start, type
<p>:
<pre>:
<ul>:

<!-- Inline text semantics -->
<a>: download, href, hreflang, ping, rel, target, type
<abbr>:
<b>:
<bdi>:
<bdo>:
<br>:
<cite>:
<code>:
<data>: value
<dfn>:
<em>:
<i>:
<mark>:
<q>: cite
<s>:
<samp>:
<small>:
<span>:
<strong>:
<sub>:
<sup>:
<time>: datetime
<u>:
<var>:

<!-- Image and multimedia -->
<img>: alt, decoding, height, intrinsicsize, loading, sizes, src, width

<!-- Demarcating edits -->
<del>: cite, datetime
<ins>: cite, datetime

<!-- table -->
<caption>:
<col>:
<colgroup>:
<table>:
<tbody>:
<td>: colspan, headers, rowspan
<tfoot>:
<th>: abbr, colspan, headers, rowspan, scope
<thead>:
<tr>:

<pre>:
<iframe>: allow, allowfullscreen, allowpaymentrequest, height, name, referrerpolicy, sandbox, src, srcdoc, width
```
