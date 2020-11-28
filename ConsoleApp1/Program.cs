using System;

namespace Microsoft.Docs.Build
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            var e1 = Errors.System.NeedRestore();
            var e2 = Errors.Link.NewsRestore();
        }
    }
}
