# Sanitize HTML

[Feature 178608](https://dev.azure.com/ceapex/Engineering/_workitems/edit/178608/)

## Overview

Content authors can embed arbitrary HTML tags to markdown documents. These HTML tags could contain `<script>` tag that allows arbitrary script execution, or `style` attribute that compromises site visual experience.

### Goals

- Remove HTML tags and attributes from user content that may alter site style
- Remove HTML tags and attributes from user content that may allow javascript execution
- Apply HTML sanitization consistently for all user content

### Out of scope

- Report warnings for removing disallowed HTML tags and attributes
- Refactor HTML processing pipeline

## Technical Design

The HTML processing pipeline in build gives us a chance to consistently apply certain HTML transformations to user input. There are two approaches to filter HTML tags and HTML attributes, _allowlist_ vs _disallowlist_.
The current HTML sanitizer in docfx uses _disallowlist_ to strip out `<script>`, `<link>`, `<style>` tags and `style` attributes. This alone won't provide enough protection against javascript injection. The prefered approach is to use _allowlist_ for both HTML tags and attributes, and this is what most HTML sanitizers do. 

#### Sanitize HTML Tags

Using _allowlist_ for HTML tags is very likely to cause content visual diffs. Sometimes writers surround text (especially variables) with angle bracket not knowing they are treated as HTML tags. These tags can appear as a standalone tag (without closing tag) and be parsed into an incorrect HTML DOM tree. Removing these tags can cause a huge chunk of content to be stripped from output.

The danger of using _allowlist_ for HTML tags overweight it's advantage at this time period, so we keep using the current _disallowlist_ approach for HTML tags. The disallowed HTML tags are:

- `<script>`
- `<link>`
- `<style>`

#### Sanitize HTML attribute

For HTML attributes, we _MUST_ use _allowlist_ because attributes that allow javascript execution is an open set.

For reference, https://developer.mozilla.org/en-US/docs/Web/Events lists all the possible DOM events, some of them are standard events, but it also contains lots of non-standard events that are browser specific. Some non-stardard events are prefixed with `moz`, but others aren't like `dataError`.

The HTML attribute allowlist is defined as the sum of:

- Standard HTML5 attributes for allowed HTML tag names.
- Attribute names starting with `data-`.
- Existing attribute names respected by `docs-ui` that does not have `data-` prefix.

> ‼️ Due to how the HTML processing pipeline works today, we cannot tell _user HTML_ from _system generated HTML_, so this sanitizer works for both. 
All new `docs-ui` features that depend on build output with custom HTML attributes __MUST__ use `data-` prefix, overwise they'll be stripped by the sanitizer. 

#### Sanitize URL

To prevent script execution like `<a href="javascript://void">`, only `http://` or `https://` are allowed URL schemas for all _external_ links. Absolute URLs (like `/azure`) and relative URLs (like `azure`) are not sanitized.

## Dependencies

The ruleout of this feature impact v3 migration tool since there are likely HTML diffs.
We can either make the change simultaneously in v2 and v3, or change the v3 migration tool to ignore these diffs.

## Appendix

#### HTML5 attribute name allowlist

TBD:

#### docs-ui non-standard attribute name allowlist

TBD:
