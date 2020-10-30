// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Docs.Validation;

namespace Microsoft.Docs.Build
{
    internal class JsonSchemaValidatorExtension // todo rename
    {
        private readonly DocumentProvider _documentProvider;
        private readonly Lazy<PublishUrlMap> _publishUrlMap;
        private readonly MonikerProvider _monikerProvider;
        private readonly ErrorBuilder _errorLog;
        private readonly Config _config;
        private readonly Dictionary<string, List<SourceInfo<CustomRule>>> _customRules = new Dictionary<string, List<SourceInfo<CustomRule>>>();

        public JsonSchemaValidatorExtension(
            Config config,
            FileResolver fileResolver,
            DocumentProvider documentProvider,
            Lazy<PublishUrlMap> publishUrlMap,
            MonikerProvider monikerProvider,
            ErrorBuilder errorLog)
        {
            _documentProvider = documentProvider;
            _publishUrlMap = publishUrlMap;
            _monikerProvider = monikerProvider;
            _errorLog = errorLog;
            _config = config;

            var contentValidationRules = GetContentValidationRules(config, fileResolver);
            var buildValidationRules = GetBuildValidationRules(config, fileResolver);
            _customRules = MergeCustomRules(contentValidationRules, buildValidationRules);
        }

        public bool IsEnable(FilePath filePath, CustomRule customRule, string? moniker = null)
        {
            var canonicalVersion = _publishUrlMap.Value.GetCanonicalVersion(filePath);

            // If content versioning not enabled for this depot, canonicalVersion will be null, content will always be the canonical version;
            // If content versioning enabled and moniker is null, we should check file-level monikers to be sure;
            // If content versioning enabled and moniker is not null, just compare canonicalVersion and moniker.
            var isCanonicalVersion = string.IsNullOrEmpty(canonicalVersion) ? true :
                string.IsNullOrEmpty(moniker) ? _monikerProvider.GetFileLevelMonikers(_errorLog, filePath).IsCanonicalVersion(canonicalVersion) :
                canonicalVersion == moniker;

            if (customRule.CanonicalVersionOnly && !isCanonicalVersion)
            {
                return false;
            }

            var pageType = _documentProvider.GetPageType(filePath);

            return customRule.ContentTypes is null || customRule.ContentTypes.Contains(pageType);
        }

        public Error WithCustomRule(Error error)
        {
            if (TryGetCustomRule(error, _customRules, out var customRule))
            {
                error = WithCustomRule(error, customRule);
            }
            return error;
        }

        public static Error WithCustomRule(Error error, CustomRule customRule, bool? enabled = null)
        {
            var level = customRule.Severity ?? error.Level;

            if (level != ErrorLevel.Off && customRule.ExcludeMatches(error.OriginalPath ?? error.Source?.File?.Path ?? ""))
            {
                level = ErrorLevel.Off;
            }

            if (enabled != null && !enabled.Value)
            {
                level = ErrorLevel.Off;
            }

            var message = error.Message;

            if (!string.IsNullOrEmpty(customRule.Message))
            {
                try
                {
                    message = string.Format(customRule.Message, error.MessageArguments);
                }
                catch (FormatException)
                {
                    message += "ERROR: custom message format is invalid, e.g., too many parameters {n}.";
                }
            }

            message = string.IsNullOrEmpty(customRule.AdditionalMessage) ?
                message : $"{message}{(message.EndsWith('.') ? "" : ".")} {customRule.AdditionalMessage}";

            return new Error(
                level,
                string.IsNullOrEmpty(customRule.Code) ? error.Code : customRule.Code,
                message,
                error.MessageArguments,
                error.Source,
                error.PropertyPath,
                error.OriginalPath,
                customRule.PullRequestOnly);
        }

        private bool TryGetCustomRule(
            Error error,
            Dictionary<string, List<SourceInfo<CustomRule>>> allCustomRules,
            out SourceInfo<CustomRule>? customRule)
        {
            if (allCustomRules.TryGetValue(error.Code, out var customRules))
            {
                foreach (var rule in customRules)
                {
                    // todo need confirm override order
                    var r = rule.Value;
                    if (r.PropertyPath != null)
                    {
                        // compare with code + propertyPath + contentType
                        var source = error.Source?.File;
                        var pageType = source != null ? _documentProvider.GetPageType(source) : null;
                        if (r.PropertyPath.Equals(error.PropertyPath) && r.ContentTypes.Contains(pageType))
                        {
                            customRule = rule;
                            return true;
                        }
                        continue;
                    }
                    else
                    {
                        customRule = rule; // system error
                        return true;
                    }
                }
            }
            customRule = null;
            return false;
        }

        private Dictionary<string, List<SourceInfo<CustomRule>>> MergeCustomRules(
            Dictionary<string, ValidationRules>? contentValidationRules,
            Dictionary<string, ValidationRules>? buildValidationRules)
        {
            var customRules = _config != null ?
                _config.Rules.ToDictionary(
                    item => item.Key,
                    item => new List<SourceInfo<CustomRule>> { item.Value })
                :
                new Dictionary<string, List<SourceInfo<CustomRule>>>();

            if (contentValidationRules != null)
            {
                foreach (var validationRule in contentValidationRules.SelectMany(rules => rules.Value.Rules).Where(rule => !rule.DocfxOverride))
                {
                    if (customRules.ContainsKey(validationRule.Code))
                    {
                        _errorLog.Add(Errors.Logging.RuleOverrideInvalid(validationRule.Code, customRules[validationRule.Code].First().Source));
                        customRules.Remove(validationRule.Code);
                    }
                }
                foreach (var validationRule in contentValidationRules.SelectMany(rules => rules.Value.Rules).Where(rule => rule.PullRequestOnly))
                {
                    if (customRules.TryGetValue(validationRule.Code, out var customRule))
                    {
                        var list = new List<SourceInfo<CustomRule>>();
                        list.Add(new SourceInfo<CustomRule>(
                            new CustomRule(
                                customRule.First().Value.Severity,
                                customRule.First().Value.Code,
                                null,
                                customRule.First().Value.AdditionalMessage,
                                null,
                                customRule.First().Value.CanonicalVersionOnly,
                                validationRule.PullRequestOnly,
                                null),
                            customRule.First().Source));
                        customRules[validationRule.Code] = list;
                    }
                    else
                    {
                        var list = new List<SourceInfo<CustomRule>>();
                        list.Add(new SourceInfo<CustomRule>(new CustomRule(null, null, null, null, null, false, validationRule.PullRequestOnly, null)));
                        customRules.Add(
                            validationRule.Code,
                            list);
                    }
                }
            }

            if (buildValidationRules != null)
            {
                foreach (var validationRule in buildValidationRules.SelectMany(rules => rules.Value.Rules))
                {
                    var oldCode = ConvertTypeToCode(validationRule.Type);
                    var newRule = new SourceInfo<CustomRule>(new CustomRule(
                                ConvertSeverity(validationRule.Severity),
                                validationRule.Code,
                                validationRule.Message,
                                validationRule.AdditionalErrorMessage,
                                validationRule.PropertyPath,
                                validationRule.CanonicalVersionOnly,
                                validationRule.PullRequestOnly,
                                validationRule.ContentTypes));

                    // won't override docfx custom rules
                    if (!customRules.ContainsKey(oldCode))
                    {
                        var list = new List<SourceInfo<CustomRule>> { newRule };
                        customRules.Add(oldCode, list);
                    }
                    else
                    {
                        customRules[oldCode].Add(newRule); // append
                    }
                }
            }

            return customRules;
        }

        // MissingAttribute -> missing-attribute
        private static string ConvertTypeToCode(string str)
        {
            return string.Concat(str.Select((x, i) => i > 0 && char.IsUpper(x) ? "-" + x.ToString() : x.ToString())).ToLowerInvariant();
        }

        private static ErrorLevel ConvertSeverity(ValidationSeverity severity)
        {
            return severity switch
            {
                ValidationSeverity.SUGGESTION => ErrorLevel.Suggestion,
                ValidationSeverity.WARNING => ErrorLevel.Warning,
                ValidationSeverity.ERROR => ErrorLevel.Error,
                _ => ErrorLevel.Info,
            };
        }

        private static Dictionary<string, ValidationRules>? GetContentValidationRules(Config? config, FileResolver fileResolver)
            => !string.IsNullOrEmpty(config?.MarkdownValidationRules.Value)
            ? JsonUtility.DeserializeData<Dictionary<string, ValidationRules>>(
                fileResolver.ReadString(config.MarkdownValidationRules),
                config.MarkdownValidationRules.Source?.File)
            : null;

        private static Dictionary<string, ValidationRules>? GetBuildValidationRules(Config? config, FileResolver fileResolver)
            => !string.IsNullOrEmpty(config?.BuildValidationRules.Value)
            ? JsonUtility.DeserializeData<Dictionary<string, ValidationRules>>(
                fileResolver.ReadString(config.BuildValidationRules),
                config.BuildValidationRules.Source?.File)
            : null;
    }
}
