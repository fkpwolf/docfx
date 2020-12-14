// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.Docs.Build
{
    public static class Program
    {
        public static void Main()
        {
            Console.WriteLine("Hello World!");

            var err = Errors.System.ValidationIncomplete2();
        }
    }
}
