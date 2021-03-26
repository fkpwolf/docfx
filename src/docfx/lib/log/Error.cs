// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.Docs.Build
{
    internal record Error
    {
        public ErrorLevel Level { get; init; }

        public string Code { get; init; }

        public string Message { get; init; }

        public string? PropertyPath { get; init; }

        public SourceInfo? Source { get; init; }

        public PathString? OriginalPath { get; init; }

        public bool PullRequestOnly { get; init; }

        public string? DocumentUrl { get; init; }

        public object?[] MessageArguments { get; init; } = Array.Empty<object?>();

        public AdditionalErrorInfo? AdditonalErrorInfo { get; init; }

        public Error(ErrorLevel level, string code, FormattableString message, SourceInfo? source = null, string? propertyPath = null)
        {
            Level = level;
            Code = code;
            Message = message.ToString();
            MessageArguments = message.GetArguments();
            Source = source;
            PropertyPath = propertyPath;
        }

        public override string ToString()
        {
            var file = OriginalPath ?? Source?.File?.Path;
            var source = OriginalPath is null ? Source : null;
            var line = source?.Line ?? 0;
            var end_line = source?.EndLine ?? 0;
            var column = source?.Column ?? 0;
            var end_column = source?.EndColumn ?? 0;

            return JsonUtility.Serialize(new
            {
                message_severity = Level,
                Code,
                message = Message,
                file,
                line,
                end_line,
                column,
                end_column,
                log_item_type = "user",
                pull_request_only = PullRequestOnly ? (bool?)true : null,
                property_path = PropertyPath,
                ms_author = AdditonalErrorInfo?.MsAuthor,
                ms_prod = AdditonalErrorInfo?.MsProd,
                ms_technology = AdditonalErrorInfo?.MsTechnology,
                ms_service = AdditonalErrorInfo?.MsService,
                ms_subservice = AdditonalErrorInfo?.MsSubservice,
                document_url = DocumentUrl,
                date_time = DateTime.UtcNow, // Leave data_time as the last field to make regression test stable
            }).Replace("\"ms_", "\"ms.");
        }

        public DocfxException ToException(Exception? innerException = null, bool isError = true)
        {
            var error = isError && Level != ErrorLevel.Error ? this with { Level = ErrorLevel.Error } : this;
            return new DocfxException(error, innerException);
        }
    }
}
