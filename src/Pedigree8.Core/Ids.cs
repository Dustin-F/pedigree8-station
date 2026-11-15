using System;

namespace Pedigree8.Core
{
    public static class Ids
    {
        public static string New() => Guid.NewGuid().ToString("D");
    }
}
