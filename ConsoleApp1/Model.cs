// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Docs.Build
{
    public class Error
    {
        public Error(ErrorLevel level, string code, string message)
        {
        }
    }

    public enum ErrorLevel
    {
        Off,
        Info,
        Suggestion,
        Warning,
        Error,
    }
}
