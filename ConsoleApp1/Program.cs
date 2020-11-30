using System;

namespace Microsoft.Docs.Build
{
    public static class Program
    {
        public static void Main()
        {
            Console.WriteLine("Hello World!");
            var e1 = Errors.System.NeedRestore("hh");
        }
    }
}
