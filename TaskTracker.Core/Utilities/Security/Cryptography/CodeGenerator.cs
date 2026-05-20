using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace TaskTracker.Core.Utilities.Security.Cryptography
{
    public static class CodeGenerator
    {
        public static string Generate6DigitCode()
        {
            byte[] bytes = new byte[4];
            RandomNumberGenerator.Fill(bytes);

            int value = BitConverter.ToInt32(bytes, 0);
            value = Math.Abs(value % 1_000_000);

            return value.ToString("D6");
        }
    }
}
